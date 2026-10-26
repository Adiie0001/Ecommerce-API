using System.Net;
using System.Net.Http.Json;
using EcommerceAPI.Contracts;
using EcommerceAPI.Data;
using EcommerceAPI.Domain;
using EcommerceAPI.Infrastructure;
using EcommerceAPI.Infrastructure.Options;
using EcommerceAPI.Services;
using EcommerceAPI.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace EcommerceAPI.Tests;

public sealed class AccountEdgeCaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_valid_token_for_a_deleted_account_is_rejected_by_me()
    {
        var (email, auth) = await factory.NewCustomerAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Email == email).ExecuteDeleteAsync();
        }

        var response = await factory.CreateClient().WithBearer(auth.AccessToken).GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_locked_out_account_cannot_mint_new_tokens_with_an_old_refresh_token()
    {
        var (email, auth) = await factory.NewCustomerAsync();
        var client = factory.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Wrong-Password-1" });
        }

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = auth.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_service_itself_validates_email_and_display_name()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() => auth.RegisterAsync(
            new RegisterRequest { Email = "nope", DisplayName = "   ", Password = "short" }, CancellationToken.None));

        Assert.Contains("email", failure.Errors.Keys);
        Assert.Contains("displayName", failure.Errors.Keys);
        Assert.Contains("password", failure.Errors.Keys);
    }

    [Fact]
    public async Task Expired_refresh_tokens_of_a_user_are_cleaned_up_on_their_next_login()
    {
        var (email, first) = await factory.NewCustomerAsync();
        factory.Clock.Advance(TimeSpan.FromDays(15));

        await factory.LoginAsync(email, ApiFactory.CustomerPassword);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = scope.ServiceProvider.GetRequiredService<ITokenService>().HashRefreshToken(first.RefreshToken);
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.TokenHash == hash));
    }
}

public sealed class DatabaseModeTests
{
    [Fact]
    public async Task The_database_file_uses_write_ahead_logging_after_startup_migration()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var connection = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        Assert.Equal("wal", (string?)await command.ExecuteScalarAsync());
    }
}

public sealed class HttpsRedirectionTests
{
    [Fact]
    public async Task When_enabled_plain_http_requests_are_redirected_to_https()
    {
        using var factory = ApiFactory.With(new() { ["Security:HttpsRedirection"] = "true", ["https_port"] = "443" });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/v1/products");

        Assert.True(response.StatusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect);
        Assert.StartsWith("https://localhost/api/v1/products", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task By_default_the_app_does_not_redirect_because_tls_usually_ends_at_a_proxy()
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class ExceptionHandlerTests
{
    private sealed class RecordingProblemDetails : IProblemDetailsService
    {
        public ProblemDetailsContext? Written { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class DevEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static (ApiExceptionHandler Handler, RecordingProblemDetails Problems) Create(string environment = "Production")
    {
        var problems = new RecordingProblemDetails();
        return (new ApiExceptionHandler(problems, new DevEnvironment(environment), NullLogger<ApiExceptionHandler>.Instance), problems);
    }

    [Fact]
    public async Task A_client_that_went_away_gets_no_response()
    {
        var (handler, problems) = Create();
        using var cts = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cts.Token };
        await cts.CancelAsync();

        var handled = await handler.TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

        Assert.True(handled);
        Assert.Null(problems.Written);
    }

    [Fact]
    public async Task Bad_http_requests_keep_their_own_status_code()
    {
        var (handler, problems) = Create();
        var context = new DefaultHttpContext();

        await handler.TryHandleAsync(context, new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge), CancellationToken.None);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, problems.Written!.ProblemDetails.Status);
        Assert.Equal("too large", problems.Written.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Internal_errors_expose_their_message_only_in_development()
    {
        var production = Create("Production");
        var development = Create("Development");

        await production.Handler.TryHandleAsync(new DefaultHttpContext(), new InvalidOperationException("db password is hunter2"), CancellationToken.None);
        await development.Handler.TryHandleAsync(new DefaultHttpContext(), new InvalidOperationException("db password is hunter2"), CancellationToken.None);

        Assert.Equal("An unexpected error occurred.", production.Problems.Written!.ProblemDetails.Detail);
        Assert.Equal("db password is hunter2", development.Problems.Written!.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Domain_failures_map_to_their_status_codes()
    {
        var (handler, problems) = Create();

        await handler.TryHandleAsync(new DefaultHttpContext(), new NotFoundException("gone"), CancellationToken.None);
        Assert.Equal(404, problems.Written!.ProblemDetails.Status);

        await handler.TryHandleAsync(new DefaultHttpContext(), new ConflictException("clash"), CancellationToken.None);
        Assert.Equal(409, problems.Written.ProblemDetails.Status);

        await handler.TryHandleAsync(new DefaultHttpContext(), new ValidationFailedException("field", "bad"), CancellationToken.None);
        Assert.Equal(400, problems.Written.ProblemDetails.Status);
        Assert.IsType<ValidationProblemDetails>(problems.Written.ProblemDetails);
    }
}

public sealed class SmallUnitTests
{
    [Fact]
    public void The_design_time_factory_builds_a_sqlite_context_without_the_web_host()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
    }

    [Theory]
    [InlineData("a@b.co", true)]
    [InlineData("  padded@shop.test  ", true)]
    [InlineData("", false)]
    [InlineData("no-at-sign", false)]
    public void Email_validation_trims_and_rejects_nonsense(string email, bool valid) =>
        Assert.Equal(valid, EmailAddress.IsValid(email));

    [Fact]
    public void Overlong_emails_are_invalid() =>
        Assert.False(EmailAddress.IsValid(new string('a', 250) + "@shop.test"));

    [Fact]
    public void Normalising_an_email_trims_and_lower_cases_it() =>
        Assert.Equal("ada@shop.test", EmailAddress.Normalize("  ADA@Shop.Test "));

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(41, 20, 3)]
    [InlineData(10, 0, 0)]
    public void Total_pages_round_up_and_tolerate_a_zero_page_size(int total, int size, int pages) =>
        Assert.Equal(pages, new PagedResult<int>([], 1, size, total).TotalPages);

    [Fact]
    public void Recommendation_options_report_every_out_of_range_gemini_setting()
    {
        var gemini = new GeminiOptions
        {
            Enabled = true,
            ApiKey = "k",
            CacheMinutes = 61,
            TimeoutSeconds = 0,
            MaxCandidates = 101,
            CircuitBreakerMinimumRequests = 1,
            CircuitBreakerSeconds = 0
        };

        var result = new RecommendationOptionsValidator().Validate(null, new RecommendationOptions { Gemini = gemini });

        Assert.True(result.Failed);
        Assert.Equal(5, result.Failures!.Count());
    }

    [Fact]
    public void Seed_options_flag_a_bad_bootstrap_email_and_weak_password()
    {
        var env = new DevEnvironment("Production");
        var validator = new SeedOptionsValidator(env);

        var result = validator.Validate(null, new SeedOptions { AdminEmail = "not-an-email", AdminPassword = "weak" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("AdminEmail"));
        Assert.Contains(result.Failures!, f => f.Contains("AdminPassword"));
    }

    private sealed class DevEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
