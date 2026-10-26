using System.Text;
using EcommerceAPI.Domain;
using EcommerceAPI.Infrastructure.Middleware;
using EcommerceAPI.Infrastructure.Options;
using EcommerceAPI.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EcommerceAPI.Tests.Unit;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("Correct-Horse-9")]
    [InlineData("aB3defghij")]
    [InlineData("Sup3r-Secret-Pass")]
    public void Acceptable_passwords_pass(string password) => Assert.Empty(PasswordPolicy.Validate(password, "someone@shop.test"));

    [Theory]
    [InlineData("Sh0rt", "at least 10 characters")]
    [InlineData("alllowercase123", "upper-case")]
    [InlineData("ALLUPPERCASE123", "lower-case")]
    [InlineData("NoDigitsAtAllHere", "digit")]
    [InlineData("password1234", "too common")]
    public void Each_rule_reports_its_own_message(string password, string expectedFragment)
    {
        var errors = PasswordPolicy.Validate(password);

        Assert.Contains(errors, e => e.Contains(expectedFragment));
    }

    [Fact]
    public void Passwords_longer_than_the_maximum_are_rejected()
    {
        Assert.Contains(PasswordPolicy.Validate("Aa1" + new string('x', PasswordPolicy.MaxLength)), e => e.Contains("at most"));
    }

    [Fact]
    public void The_name_part_of_the_email_may_not_be_used_inside_the_password()
    {
        var errors = PasswordPolicy.Validate("My-Johnsmith-1234", "johnsmith@shop.test");

        Assert.Contains(errors, e => e.Contains("email"));
    }
}

public sealed class MoneyTests
{
    [Theory]
    [InlineData(12.34, 1234)]
    [InlineData(0.01, 1)]
    [InlineData(19.99, 1999)]
    [InlineData(1000000, 100000000)]
    public void Amounts_convert_to_exact_cents(double amount, long cents) => Assert.Equal(cents, Money.ToCents((decimal)amount));

    [Fact]
    public void Half_a_cent_rounds_away_from_zero() => Assert.Equal(2L, Money.ToCents(0.015m));

    [Theory]
    [InlineData(1.99, true)]
    [InlineData(2, true)]
    [InlineData(1.999, false)]
    public void Only_two_decimal_places_are_allowed(double amount, bool allowed) =>
        Assert.Equal(allowed, Money.HasAtMostTwoDecimals((decimal)amount));

    [Fact]
    public void The_product_price_property_is_a_view_over_cents()
    {
        var product = new Product { Name = "n", Description = "d", Price = 5.55m };

        Assert.Equal(555L, product.PriceCents);
        product.PriceCents = 1999;
        Assert.Equal(19.99m, product.Price);
    }
}

public sealed class CorrelationIdTests
{
    [Theory]
    [InlineData("abc-123_DEF.9", true)]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("has space", false)]
    [InlineData("semi;colon", false)]
    [InlineData("new\nline", false)]
    public void Only_short_safe_ids_are_accepted(string? candidate, bool accepted) =>
        Assert.Equal(accepted, CorrelationIdMiddleware.Accept(candidate) is not null);

    [Fact]
    public void Ids_over_64_characters_are_refused() => Assert.Null(CorrelationIdMiddleware.Accept(new string('a', 65)));
}

public sealed class TokenServiceTests
{
    private const string Key = "unit-test-signing-key-with-more-than-32-bytes";

    private static JwtTokenService NewService(TimeProvider? clock = null) => new(
        Microsoft.Extensions.Options.Options.Create(new JwtOptions { SigningKey = Key, AccessTokenMinutes = 15 }),
        clock ?? TimeProvider.System);

    private static AppUser User(UserRole role = UserRole.Customer) => new()
    {
        Id = 42,
        Email = "ada@shop.test",
        DisplayName = "Ada",
        PasswordHash = "hash",
        Role = role
    };

    [Fact]
    public async Task Access_tokens_carry_identity_role_and_a_fifteen_minute_lifetime()
    {
        var token = NewService().CreateAccessToken(User(UserRole.Admin));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, new TokenValidationParameters
        {
            ValidIssuer = "ecommerce-api",
            ValidAudience = "ecommerce-clients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key))
        });

        Assert.True(result.IsValid);
        var jwt = (JsonWebToken)result.SecurityToken;
        Assert.Equal("42", jwt.Subject);
        Assert.Equal("Admin", jwt.GetClaim("role").Value);
        Assert.Equal("ada@shop.test", jwt.GetClaim("email").Value);
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.ValidFrom);
    }

    [Fact]
    public void Every_access_token_has_a_unique_id()
    {
        var service = NewService();

        Assert.NotEqual(
            new JsonWebToken(service.CreateAccessToken(User()).Value).Id,
            new JsonWebToken(service.CreateAccessToken(User()).Value).Id);
    }

    [Fact]
    public void Refresh_tokens_are_long_unique_and_url_safe()
    {
        var service = NewService();
        var tokens = Enumerable.Range(0, 50).Select(_ => service.CreateRefreshToken()).ToList();

        Assert.Equal(50, tokens.Distinct().Count());
        Assert.All(tokens, t =>
        {
            Assert.True(t.Length >= 43);
            Assert.Matches("^[A-Za-z0-9_-]+$", t);
        });
    }

    [Fact]
    public void Hashing_is_deterministic_hex_sha256()
    {
        var service = NewService();

        var hash = service.HashRefreshToken("token");

        Assert.Equal(hash, service.HashRefreshToken("token"));
        Assert.NotEqual(hash, service.HashRefreshToken("token2"));
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }
}

public sealed class PasswordHasherTests
{
    private static BCryptPasswordHasher NewHasher() =>
        new(Microsoft.Extensions.Options.Options.Create(new AuthOptions { BcryptWorkFactor = 4 }));

    [Fact]
    public void A_hash_verifies_only_the_original_password()
    {
        var hasher = NewHasher();
        var hash = hasher.Hash("Correct-Horse-9");

        Assert.True(hasher.Verify("Correct-Horse-9", hash));
        Assert.False(hasher.Verify("Correct-Horse-8", hash));
        Assert.NotEqual(hash, hasher.Hash("Correct-Horse-9"));
    }

    [Fact]
    public void Long_passwords_are_not_silently_truncated()
    {
        var hasher = NewHasher();
        var prefix = new string('a', 80);
        var hash = hasher.Hash(prefix + "X");

        Assert.False(hasher.Verify(prefix + "Y", hash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-bcrypt-hash")]
    public void Missing_or_malformed_hashes_never_authenticate(string? hash) =>
        Assert.False(NewHasher().Verify("anything", hash));
}

public sealed class OptionsValidatorTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void Demo_data_is_allowed_only_in_development()
    {
        var options = new SeedOptions { DemoData = true };

        Assert.True(new SeedOptionsValidator(new FakeEnvironment("Development")).Validate(null, options).Succeeded);
        Assert.True(new SeedOptionsValidator(new FakeEnvironment("Production")).Validate(null, options).Failed);
        Assert.True(new SeedOptionsValidator(new FakeEnvironment("Staging")).Validate(null, options).Failed);
    }

    [Fact]
    public void Demo_passwords_are_allowed_for_the_bootstrap_admin_only_in_development()
    {
        var options = new SeedOptions { AdminEmail = "owner@shop.test", AdminPassword = EcommerceAPI.Data.DemoCredentials.AdminPassword };

        Assert.True(new SeedOptionsValidator(new FakeEnvironment("Development")).Validate(null, options).Succeeded);
        Assert.True(new SeedOptionsValidator(new FakeEnvironment("Production")).Validate(null, options).Failed);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("short", false)]
    [InlineData("YourSecretKeyHereMustBeVeryLongForSecurity", false)]
    [InlineData("a-perfectly-fine-random-looking-key-0123456789", true)]
    public void Signing_keys_must_be_long_and_not_placeholders(string key, bool valid) =>
        Assert.Equal(valid, new JwtOptionsValidator().Validate(null, new JwtOptions { SigningKey = key }).Succeeded);

    [Fact]
    public void Gemini_settings_are_only_checked_when_it_is_enabled()
    {
        var validator = new RecommendationOptionsValidator();

        Assert.True(validator.Validate(null, new RecommendationOptions()).Succeeded);
        Assert.True(validator.Validate(null, new RecommendationOptions { Gemini = new GeminiOptions { Enabled = true } }).Failed);
        Assert.True(validator.Validate(null, new RecommendationOptions
        {
            Gemini = new GeminiOptions { Enabled = true, ApiKey = "k", BaseUrl = "ftp://nope" }
        }).Failed);
        Assert.True(validator.Validate(null, new RecommendationOptions
        {
            Gemini = new GeminiOptions { Enabled = true, ApiKey = "k", MaxCandidates = 0 }
        }).Failed);
    }
}
