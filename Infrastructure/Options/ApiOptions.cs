using System.ComponentModel.DataAnnotations;

namespace EcommerceAPI.Infrastructure.Options;

/// <summary>Token settings. The signing key is never stored in the repository; see README "Configuration".</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinKeyBytes = 32;

    [Required]
    public string Issuer { get; set; } = "ecommerce-api";

    [Required]
    public string Audience { get; set; } = "ecommerce-clients";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Supply via <c>Jwt__SigningKey</c> or user-secrets.</summary>
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>BCrypt cost factor. 12 is the production default; tests lower it for speed.</summary>
    [Range(4, 15)]
    public int BcryptWorkFactor { get; set; } = 12;

    [Range(1, 20)]
    public int MaxFailedAttempts { get; set; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending EF Core migrations when the app starts. On by default only in Development.</summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Insert the demo catalog and demo users. Only permitted in the Development environment.</summary>
    public bool DemoData { get; set; }

    /// <summary>Optional bootstrap administrator, created once if no administrator exists.</summary>
    public string? AdminEmail { get; set; }

    public string? AdminPassword { get; set; }

    public string AdminDisplayName { get; set; } = "Administrator";
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    [Range(1, 100_000)]
    public int AuthPerMinute { get; set; } = 10;

    [Range(1, 100_000)]
    public int RecommendPerMinute { get; set; } = 20;

    [Range(1, 100_000)]
    public int GlobalPerMinute { get; set; } = 240;
}

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    /// <summary>Browser origins allowed to call the API. Empty means no cross-origin access.</summary>
    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class RecommendationOptions
{
    public const string SectionName = "Recommendations";

    [Range(1, 10)]
    public int MaxResults { get; set; } = 5;

    /// <summary>Upper bound on catalog rows loaded per recommendation request.</summary>
    [Range(1, 5000)]
    public int MaxCatalogItems { get; set; } = 500;

    public GeminiOptions Gemini { get; set; } = new();
}

public sealed class GeminiOptions
{
    /// <summary>Off by default. When off, only the deterministic rule-based engine runs and nothing leaves the server.</summary>
    public bool Enabled { get; set; }

    /// <summary>Sent in the <c>x-goog-api-key</c> header, never in the URL. Supply via <c>Recommendations__Gemini__ApiKey</c>.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gemini-2.5-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/";

    /// <summary>Maximum number of catalog items sent to the model per request.</summary>
    [Range(1, 100)]
    public int MaxCandidates { get; set; } = 40;

    [Range(0, 60)]
    public int CacheMinutes { get; set; } = 5;

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 8;

    /// <summary>Outcomes the circuit breaker must see within its sampling window before it may open.</summary>
    [Range(2, 1000)]
    public int CircuitBreakerMinimumRequests { get; set; } = 10;

    /// <summary>How long the breaker stays open (requests fail fast and use the rule engine) before probing again.</summary>
    [Range(1, 600)]
    public int CircuitBreakerSeconds { get; set; } = 30;
}
