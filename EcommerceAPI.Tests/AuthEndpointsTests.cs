using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EcommerceAPI.Contracts;
using EcommerceAPI.Tests.TestSupport;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EcommerceAPI.Tests;

public sealed class AuthEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient Client => factory.CreateClient();

    [Fact]
    public async Task Register_creates_a_customer_and_never_returns_credentials()
    {
        var email = TestHelpers.NewEmail();

        var response = await Client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = email.ToUpperInvariant(), displayName = "Ada", password = ApiFactory.CustomerPassword });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.ReadJsonAsync();
        Assert.Equal(email, json.GetProperty("email").GetString());
        Assert.Equal("Customer", json.GetProperty("role").GetString());
        Assert.DoesNotContain("password", json.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_ignores_a_role_sent_by_the_caller()
    {
        var email = TestHelpers.NewEmail();

        var response = await Client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, displayName = "Mallory", password = ApiFactory.CustomerPassword, role = "Admin", passwordHash = "x" });
        var user = await response.ReadAsync<UserDto>();
        Assert.Equal("Customer", user.Role);

        var login = await factory.LoginAsync(email, ApiFactory.CustomerPassword);
        var stats = await Client.WithBearer(login.AccessToken).GetAsync("/api/v1/products/stats");
        Assert.Equal(HttpStatusCode.Forbidden, stats.StatusCode);
    }

    [Theory]
    [InlineData("Short1A")]
    [InlineData("alllowercase1234")]
    [InlineData("ALLUPPERCASE1234")]
    [InlineData("NoDigitsInThisOne")]
    [InlineData("Password1234")]
    public async Task Register_rejects_weak_passwords_with_a_field_error(string password)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = TestHelpers.NewEmail(), displayName = "Weak", password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.ReadJsonAsync();
        Assert.True(json.GetProperty("errors").TryGetProperty("password", out var errors));
        Assert.NotEmpty(errors.EnumerateArray());
    }

    [Fact]
    public async Task Register_rejects_an_invalid_email()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = "not-an-email", displayName = "Nobody", password = ApiFactory.CustomerPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.ReadJsonAsync();
        Assert.True(json.GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Register_rejects_a_duplicate_email_ignoring_case()
    {
        var email = TestHelpers.NewEmail();
        var first = await Client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, displayName = "First", password = ApiFactory.CustomerPassword });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await Client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = email.ToUpperInvariant(), displayName = "Second", password = ApiFactory.CustomerPassword });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Login_returns_a_token_pair_and_the_user()
    {
        var (email, auth) = await factory.NewCustomerAsync();

        Assert.Equal("Bearer", auth.TokenType);
        Assert.Equal(15 * 60, auth.ExpiresIn);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.True(auth.RefreshToken.Length >= 40);
        Assert.Equal(email, auth.User.Email);
        Assert.Equal("Customer", auth.User.Role);
    }

    [Fact]
    public async Task Login_failures_do_not_reveal_whether_the_account_exists()
    {
        var (email, _) = await factory.NewCustomerAsync();

        var wrongPassword = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Wrong-Password-1" });
        var unknownAccount = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = TestHelpers.NewEmail("ghost"), password = "Wrong-Password-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownAccount.StatusCode);
        var a = await wrongPassword.ReadJsonAsync();
        var b = await unknownAccount.ReadJsonAsync();
        Assert.Equal(a.GetProperty("detail").GetString(), b.GetProperty("detail").GetString());
        Assert.Equal(a.GetProperty("title").GetString(), b.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_until_the_lockout_expires()
    {
        var (email, _) = await factory.NewCustomerAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Wrong-Password-1" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var whileLocked = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = ApiFactory.CustomerPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, whileLocked.StatusCode);

        factory.Clock.Advance(TimeSpan.FromMinutes(16));

        var afterLockout = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = ApiFactory.CustomerPassword });
        Assert.Equal(HttpStatusCode.OK, afterLockout.StatusCode);
    }

    [Fact]
    public async Task A_successful_login_resets_the_failure_counter()
    {
        var (email, _) = await factory.NewCustomerAsync();

        for (var round = 0; round < 3; round++)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Wrong-Password-1" });
            }

            var success = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = ApiFactory.CustomerPassword });
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        }
    }

    [Fact]
    public async Task Me_requires_a_token_and_returns_the_signed_in_user()
    {
        var anonymous = await Client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("application/problem+json", anonymous.Content.Headers.ContentType?.MediaType);

        var (email, auth) = await factory.NewCustomerAsync();
        var me = await Client.WithBearer(auth.AccessToken).GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(email, (await me.ReadAsync<UserDto>()).Email);
    }

    [Fact]
    public async Task Access_tokens_stop_working_after_their_lifetime()
    {
        var (_, auth) = await factory.NewCustomerAsync();
        var client = Client.WithBearer(auth.AccessToken);

        factory.Clock.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);

        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_the_pair_and_a_replayed_token_revokes_the_whole_session()
    {
        var (_, first) = await factory.NewCustomerAsync();

        var rotated = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var second = await rotated.ReadAsync<AuthResponse>();
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(first.AccessToken, second.AccessToken);

        // The old token was already used once: presenting it again means a copy leaked.
        var replay = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // ...so even the legitimate newest token is now dead.
        var afterReplay = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = second.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterReplay.StatusCode);
    }

    [Fact]
    public async Task Refresh_tokens_expire()
    {
        var (_, auth) = await factory.NewCustomerAsync();

        factory.Clock.Advance(TimeSpan.FromDays(15));

        var response = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_rejects_unknown_tokens()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "not-a-real-token" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session_and_is_safe_to_repeat()
    {
        var (_, auth) = await factory.NewCustomerAsync();

        var first = await Client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = auth.RefreshToken });
        var again = await Client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = auth.RefreshToken });
        var unknown = await Client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = "never-issued" });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);

        var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Tokens_with_a_tampered_role_are_rejected()
    {
        var (_, auth) = await factory.NewCustomerAsync();
        var parts = auth.AccessToken.Split('.');
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]));
        Assert.Contains("\"role\":\"Customer\"", payload);

        var forgedPayload = Base64UrlEncoder.Encode(payload.Replace("\"role\":\"Customer\"", "\"role\":\"Admin\""));
        var forged = $"{parts[0]}.{forgedPayload}.{parts[2]}";

        var response = await Client.WithBearer(forged).GetAsync("/api/v1/products/stats");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tokens_signed_with_another_key_are_rejected()
    {
        var forged = ForgeToken(signingKey: "a-completely-different-signing-key-0123456789", audience: "ecommerce-clients");

        var response = await Client.WithBearer(forged).GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unsigned_tokens_are_rejected()
    {
        var header = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var body = Base64UrlEncoder.Encode(
            "{\"sub\":\"1\",\"role\":\"Admin\",\"iss\":\"ecommerce-api\",\"aud\":\"ecommerce-clients\",\"exp\":4102444800}");

        var response = await Client.WithBearer($"{header}.{body}.").GetAsync("/api/v1/products/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Legacy_auth_paths_still_work_and_point_to_their_replacement()
    {
        var (email, _) = await factory.NewCustomerAsync();

        var response = await Client.PostAsJsonAsync("/api/auth/login", new { email, password = ApiFactory.CustomerPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("true", response.Headers.GetValues("Deprecation").Single());
        Assert.Contains("</api/v1/auth/login>", response.Headers.GetValues("Link").Single());
    }

    private static string ForgeToken(string signingKey, string audience)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "ecommerce-api",
            Audience = audience,
            Expires = DateTime.UtcNow.AddYears(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { ["sub"] = "1", ["role"] = "Admin" }
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
