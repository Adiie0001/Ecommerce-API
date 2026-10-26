using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using EcommerceAPI.Data;
using EcommerceAPI.Domain;
using EcommerceAPI.Infrastructure.Middleware;
using EcommerceAPI.Infrastructure.Options;
using EcommerceAPI.Services;
using EcommerceAPI.Services.Recommendations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace EcommerceAPI.Infrastructure.Startup;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything the API needs. Configuration is read through options at resolve time, never eagerly,
    /// so every source (files, environment, user-secrets, test overrides) is honoured the same way.
    /// </summary>
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
        builder.ConfigureLogging();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 1_048_576);

        var services = builder.Services;
        services.AddApiOptions(builder.Environment);
        services.AddPersistence();
        services.AddSecurity();
        services.AddCatalog();
        services.AddRecommendations();
        services.AddHttpApi();
        return builder;
    }

    private static void ConfigureLogging(this WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.AddSimpleConsole(o =>
            {
                o.IncludeScopes = true;
                o.SingleLine = true;
            });
        }
        else
        {
            builder.Logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = true;
                o.UseUtcTimestamp = true;
                o.TimestampFormat = "O";
            });
        }
    }

    private static void AddApiOptions(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        if (environment.IsDevelopment())
        {
            // A fresh random key per process: nothing to configure locally, nothing to leak. Tokens die on restart.
            services.PostConfigure<JwtOptions>(o =>
            {
                if (string.IsNullOrWhiteSpace(o.SigningKey))
                {
                    o.SigningKey = DevelopmentSigningKey.Value;
                }
            });
        }

        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName).ValidateOnStart();

        services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<SeedOptions>, SeedOptionsValidator>();

        services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<CorsSettings>().BindConfiguration(CorsSettings.SectionName).ValidateOnStart();

        services.AddOptions<RecommendationOptions>()
            .BindConfiguration(RecommendationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RecommendationOptions>, RecommendationOptionsValidator>();
    }

    private static void AddPersistence(this IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            var connection = provider.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
                             ?? "Data Source=ecommerce.db";
            options.UseSqlite(connection);
        });
        services.AddScoped<DatabaseInitializer>();
    }

    private static void AddSecurity(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<AuthService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((bearer, jwtOptions, clock) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                    RoleClaimType = JwtTokenService.RoleClaim,
                    // Validate lifetime against the injected clock so expiry is testable without waiting.
                    LifetimeValidator = (notBefore, expires, _, parameters) =>
                    {
                        var now = clock.GetUtcNow().UtcDateTime;
                        return expires is { } end && end + parameters.ClockSkew > now &&
                               (notBefore is not { } start || start - parameters.ClockSkew <= now);
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.AdminOnly, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(nameof(UserRole.Admin)));
    }

    private static void AddCatalog(this IServiceCollection services) => services.AddScoped<ProductService>();

    private static void AddRecommendations(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<RuleBasedRecommender>();
        services.AddScoped<GeminiRecommender>();

        services.AddHttpClient<IGeminiClient, GeminiClient>((provider, client) =>
            {
                var gemini = provider.GetRequiredService<IOptions<RecommendationOptions>>().Value.Gemini;
                if (Uri.TryCreate(gemini.BaseUrl, UriKind.Absolute, out var baseAddress))
                {
                    client.BaseAddress = baseAddress;
                }

                // The resilience pipeline owns the real deadline; this only stops a hung connection from living forever.
                client.Timeout = TimeSpan.FromSeconds(gemini.TimeoutSeconds + 10);
            })
            .AddStandardResilienceHandler()
            .Configure((resilience, provider) =>
            {
                var gemini = provider.GetRequiredService<IOptions<RecommendationOptions>>().Value.Gemini;
                var seconds = gemini.TimeoutSeconds;
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(seconds);
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(Math.Max(1, seconds / 2.0));
                resilience.Retry.MaxRetryAttempts = 1;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(300);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, seconds * 2 + 1));
                resilience.CircuitBreaker.MinimumThroughput = gemini.CircuitBreakerMinimumRequests;
                resilience.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(gemini.CircuitBreakerSeconds);
            });

        services.AddScoped<IProductRecommender>(provider =>
        {
            var aiEnabled = provider.GetRequiredService<IOptions<RecommendationOptions>>().Value.Gemini.Enabled;
            return new ResilientRecommender(
                provider.GetRequiredService<RuleBasedRecommender>(),
                aiEnabled ? provider.GetRequiredService<GeminiRecommender>() : null,
                provider.GetRequiredService<ILogger<ResilientRecommender>>());
        });
    }

    private static void AddHttpApi(this IServiceCollection services)
    {
        services.AddControllers().ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
                var details = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState, StatusCodes.Status400BadRequest);
                var camelCased = new ValidationProblemDetails(
                    details.Errors.ToDictionary(e => CamelCase(e.Key), e => e.Value))
                {
                    Status = details.Status,
                    Title = details.Title,
                    Type = details.Type,
                    Detail = details.Detail,
                    Instance = details.Instance
                };
                foreach (var extension in details.Extensions)
                {
                    camelCased.Extensions[extension.Key] = extension.Value;
                }

                return new BadRequestObjectResult(camelCased) { ContentTypes = { "application/problem+json" } };
            };
        });

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var http = context.HttpContext;
            context.ProblemDetails.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? http.TraceIdentifier;
            if (http.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
            {
                context.ProblemDetails.Extensions["correlationId"] = correlationId;
            }

            context.ProblemDetails.Instance ??= $"{http.Request.Method} {http.Request.Path}";
        });
        services.AddExceptionHandler<ApiExceptionHandler>();

        services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc()
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Ecommerce API",
                Version = "v1",
                Description = "Product catalog with JWT authentication, refresh-token rotation and product search.",
                Contact = new OpenApiContact
                {
                    Name = "Aditya Maisuriya",
                    Url = new Uri("https://adityamaisuriya.pages.dev")
                }
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Paste the access token from /auth/login. Swagger adds the \"Bearer\" prefix."
            });
            options.OperationFilter<BearerSecurityOperationFilter>();

            var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml))
            {
                options.IncludeXmlComments(xml);
            }
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, _) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Rate limit exceeded. Try again later."
                    }
                });
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                http => Partition(http, "global", s => s.GlobalPerMinute));
            options.AddPolicy(RateLimitPolicies.Auth, http => Partition(http, "auth", s => s.AuthPerMinute));
            options.AddPolicy(RateLimitPolicies.Recommend, http => Partition(http, "recommend", s => s.RecommendPerMinute));
        });

        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<CorsSettings>>((cors, settings) =>
        {
            var origins = settings.Value.AllowedOrigins;
            cors.AddDefaultPolicy(policy =>
            {
                if (origins.Length == 0)
                {
                    return;
                }

                policy.WithOrigins(origins)
                    .WithMethods("GET", "POST", "PUT", "DELETE")
                    .WithHeaders("Authorization", "Content-Type", CorrelationIdMiddleware.HeaderName)
                    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName, "Retry-After");
            });
        });

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);
    }

    private static RateLimitPartition<string> Partition(HttpContext http, string name, Func<RateLimitOptions, int> permits)
    {
        var settings = http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        if (!settings.Enabled || http.Request.Path.StartsWithSegments("/health"))
        {
            return RateLimitPartition.GetNoLimiter($"{name}:unlimited");
        }

        var client = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"{name}:{client}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits(settings),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    private static string CamelCase(string key)
    {
        if (string.IsNullOrEmpty(key) || key.StartsWith('$'))
        {
            return key;
        }

        return string.Join('.', key.Split('.').Select(part =>
            part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
    }
}

/// <summary>Random per-process key used only when the environment is Development and none is configured.</summary>
internal static class DevelopmentSigningKey
{
    public static readonly string Value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}
