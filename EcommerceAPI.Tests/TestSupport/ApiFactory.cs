using System.Security.Cryptography;
using EcommerceAPI.Data;
using EcommerceAPI.Services;
using EcommerceAPI.Services.Recommendations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EcommerceAPI.Tests.TestSupport;

/// <summary>
/// Boots the real application in-process against a private temporary SQLite file that has the real migrations
/// applied (a file, not :memory:, so concurrent requests behave like they do in production). Time and the Gemini HTTP endpoint are replaced by fakes; everything else is the production wiring.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "root@shop.test";
    public const string AdminPassword = "Sup3r-Secret-Pass";
    public const string CustomerPassword = "Correct-Horse-9";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"ecommerce-api-test-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;
    private readonly Dictionary<string, string?> _settings;
    private readonly string _environment;

    public ApiFactory() : this(null, "Testing")
    {
    }

    // xUnit class fixtures may expose only one public constructor, so variants are created through With().
    private ApiFactory(Dictionary<string, string?>? overrides, string environment)
    {
        _environment = environment;
        _connectionString = $"Data Source={_databasePath}";
        _settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = _connectionString,
            ["Auth:BcryptWorkFactor"] = "4",
            ["Database:ApplyMigrationsOnStartup"] = "true",
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = AdminPassword,
            ["RateLimiting:Enabled"] = "false",
            ["Swagger:Enabled"] = "true",
            ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Recommendations:Gemini:Enabled"] = "false"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                _settings[key] = value;
            }
        }
    }

    /// <summary>A factory with extra or replaced settings, optionally under a different environment name.</summary>
    public static ApiFactory With(Dictionary<string, string?> overrides, string environment = "Testing") =>
        new(overrides, environment);

    public OffsetTimeProvider Clock { get; } = new();

    public FakeGeminiHandler Gemini { get; } = new();

    public string ConnectionString => _connectionString;

    /// <summary>Settings that turn the AI engine on against <see cref="Gemini"/>.</summary>
    public static Dictionary<string, string?> WithGemini() => new()
    {
        ["Recommendations:Gemini:Enabled"] = "true",
        ["Recommendations:Gemini:ApiKey"] = GeminiKey,
        ["Recommendations:Gemini:BaseUrl"] = "https://gemini.test/",
        ["Recommendations:Gemini:CacheMinutes"] = "5",
        // Keep the breaker out of the way of tests that deliberately cause failures.
        ["Recommendations:Gemini:CircuitBreakerMinimumRequests"] = "1000"
    };

    public const string GeminiKey = "test-gemini-key-0123456789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        // UseSetting feeds host configuration, which exists before Program.cs starts reading anything.
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.AddHttpClient<IGeminiClient, GeminiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Gemini);

            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            // Release pooled connections so the file can be removed on Windows.
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try
                {
                    File.Delete(_databasePath + suffix);
                }
                catch (IOException)
                {
                    // Best effort: a leftover temp file is harmless.
                }
            }
        }
    }
}
