using System.Text;
using System.Text.RegularExpressions;
using EcommerceAPI.Data;
using EcommerceAPI.Services;
using Microsoft.Extensions.Options;

namespace EcommerceAPI.Infrastructure.Options;

/// <summary>Fails startup when the signing key is missing, short or a known placeholder.</summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private static readonly string[] Placeholders =
    [
        "YourSecretKey", "change-me", "changeme", "replace-me", "your-secret"
    ];

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (Encoding.UTF8.GetByteCount(options.SigningKey) < JwtOptions.MinKeyBytes)
        {
            return ValidateOptionsResult.Fail(
                $"Jwt:SigningKey must be at least {JwtOptions.MinKeyBytes} bytes. " +
                "Set the Jwt__SigningKey environment variable (or use dotnet user-secrets).");
        }

        if (Placeholders.Any(p => options.SigningKey.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            return ValidateOptionsResult.Fail("Jwt:SigningKey looks like a placeholder; generate a random key.");
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>Keeps demo data and demo credentials out of every environment except Development.</summary>
public sealed class SeedOptionsValidator(IHostEnvironment environment) : IValidateOptions<SeedOptions>
{
    public ValidateOptionsResult Validate(string? name, SeedOptions options)
    {
        var failures = new List<string>();
        var isDevelopment = environment.IsDevelopment();

        if (options.DemoData && !isDevelopment)
        {
            failures.Add("Seed:DemoData is only allowed in the Development environment.");
        }

        var hasEmail = !string.IsNullOrWhiteSpace(options.AdminEmail);
        var hasPassword = !string.IsNullOrEmpty(options.AdminPassword);

        if (hasEmail != hasPassword)
        {
            failures.Add("Seed:AdminEmail and Seed:AdminPassword must be provided together.");
        }
        else if (hasEmail)
        {
            if (!EmailAddress.IsValid(options.AdminEmail!))
            {
                failures.Add("Seed:AdminEmail is not a valid email address.");
            }

            var policyErrors = PasswordPolicy.Validate(options.AdminPassword!, options.AdminEmail!);
            failures.AddRange(policyErrors.Select(e => $"Seed:AdminPassword {e}"));

            if (!isDevelopment && DemoCredentials.IsDemoPassword(options.AdminPassword!))
            {
                failures.Add("Seed:AdminPassword is a published demo password and cannot be used outside Development.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public sealed partial class RecommendationOptionsValidator : IValidateOptions<RecommendationOptions>
{
    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex ModelNamePattern();

    public ValidateOptionsResult Validate(string? name, RecommendationOptions options)
    {
        var gemini = options.Gemini;
        if (!gemini.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(gemini.ApiKey))
        {
            failures.Add("Recommendations:Gemini:ApiKey is required when Gemini is enabled.");
        }

        if (!ModelNamePattern().IsMatch(gemini.Model))
        {
            failures.Add("Recommendations:Gemini:Model contains unsupported characters.");
        }

        if (!Uri.TryCreate(gemini.BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Recommendations:Gemini:BaseUrl must be an absolute http(s) URL.");
        }

        // DataAnnotations validation does not reach nested option objects, so range-check them here.
        if (gemini.MaxCandidates is < 1 or > 100)
        {
            failures.Add("Recommendations:Gemini:MaxCandidates must be between 1 and 100.");
        }

        if (gemini.CacheMinutes is < 0 or > 60)
        {
            failures.Add("Recommendations:Gemini:CacheMinutes must be between 0 and 60.");
        }

        if (gemini.TimeoutSeconds is < 1 or > 60)
        {
            failures.Add("Recommendations:Gemini:TimeoutSeconds must be between 1 and 60.");
        }

        if (gemini.CircuitBreakerMinimumRequests is < 2 or > 1000)
        {
            failures.Add("Recommendations:Gemini:CircuitBreakerMinimumRequests must be between 2 and 1000.");
        }

        if (gemini.CircuitBreakerSeconds is < 1 or > 600)
        {
            failures.Add("Recommendations:Gemini:CircuitBreakerSeconds must be between 1 and 600.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
