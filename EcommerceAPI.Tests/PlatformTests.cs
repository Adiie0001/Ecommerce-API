using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EcommerceAPI.Tests.TestSupport;

namespace EcommerceAPI.Tests;

public sealed class PlatformTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient Client => factory.CreateClient();

    [Fact]
    public async Task Liveness_answers_without_needing_the_database()
    {
        var response = await Client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_confirms_the_database_is_reachable()
    {
        var response = await Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_supplied_correlation_id_is_echoed_back()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        request.Headers.Add("X-Correlation-ID", "order-4711.retry_2");

        var response = await Client.SendAsync(request);

        Assert.Equal("order-4711.retry_2", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Theory]
    [InlineData("has spaces in it")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Unsafe_correlation_ids_are_replaced_instead_of_trusted(string supplied)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", supplied);

        var response = await Client.SendAsync(request);

        var echoed = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.NotEqual(supplied, echoed);
        Assert.Matches("^[0-9a-f]{32}$", echoed);
    }

    [Fact]
    public async Task Overlong_correlation_ids_are_replaced()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", new string('a', 65));

        var response = await Client.SendAsync(request);

        Assert.Equal(32, response.Headers.GetValues("X-Correlation-ID").Single().Length);
    }

    [Fact]
    public async Task Unknown_routes_return_a_problem_document()
    {
        var response = await Client.GetAsync("/api/v1/nothing-here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Wrong_methods_return_a_problem_document()
    {
        var response = await Client.DeleteAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Malformed_json_is_a_400_problem()
    {
        using var admin = await factory.AdminClientAsync();
        using var content = new StringContent("{ this is not json", Encoding.UTF8, "application/json");

        var response = await admin.PostAsync("/api/v1/products", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unsupported_content_types_are_a_415_problem()
    {
        using var admin = await factory.AdminClientAsync();
        using var content = new StringContent("name=x", Encoding.UTF8, "text/plain");

        var response = await admin.PostAsync("/api/v1/products", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unhandled_exceptions_become_a_generic_500_without_internal_detail()
    {
        var response = await Client.GetAsync("/test/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("internal detail", text);
        Assert.DoesNotContain("InvalidOperationException", text);
        Assert.DoesNotContain("   at ", text);
        using var json = JsonDocument.Parse(text);
        Assert.Equal("An unexpected error occurred.", json.RootElement.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("traceId").GetString()));
        Assert.Equal(response.Headers.GetValues("X-Correlation-ID").Single(), json.RootElement.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData("/api/v1/products")]
    [InlineData("/api/v1/products/424242")]
    [InlineData("/test/boom")]
    public async Task Security_headers_are_present_on_success_and_error_responses(string path)
    {
        var response = await Client.GetAsync(path);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Swagger_document_describes_the_versioned_api_with_bearer_security()
    {
        var response = await Client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await response.ReadJsonAsync();
        var paths = doc.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/products", out var products));
        Assert.True(paths.TryGetProperty("/api/v1/auth/login", out _));
        Assert.False(paths.TryGetProperty("/api/product", out _), "legacy aliases must not be documented");

        var bearer = doc.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        var create = products.GetProperty("post");
        Assert.True(create.TryGetProperty("security", out _));
        var codes = create.GetProperty("responses").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("401", codes);
        Assert.Contains("403", codes);
    }

    [Fact]
    public async Task Swagger_ui_page_is_served_when_enabled()
    {
        var response = await Client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_pages_are_not_served_when_disabled()
    {
        using var production = ApiFactory.With(new() { ["Swagger:Enabled"] = "false" }, "Production");

        var response = await production.CreateClient().GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public sealed class DatabaseAvailabilityTests
{
    [Fact]
    public async Task Readiness_fails_while_liveness_stays_up_when_the_database_is_unreachable()
    {
        using var factory = ApiFactory.With(new()
        {
            ["ConnectionStrings:DefaultConnection"] = "Data Source=Z:/definitely/not/here/shop.db;Mode=ReadOnly",
            ["Database:ApplyMigrationsOnStartup"] = "false",
            ["Seed:AdminEmail"] = null,
            ["Seed:AdminPassword"] = null
        });
        var client = factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }
}

public sealed class CorsTests
{
    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/products");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }

    [Fact]
    public async Task Only_configured_origins_may_call_the_api_from_a_browser()
    {
        using var factory = ApiFactory.With(new() { ["Cors:AllowedOrigins:0"] = "https://admin.shop.test" });
        var client = factory.CreateClient();

        var allowed = await client.SendAsync(Preflight("https://admin.shop.test"));
        var other = await client.SendAsync(Preflight("https://evil.test"));

        Assert.Equal("https://admin.shop.test", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(other.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task With_no_origins_configured_cross_origin_access_is_off()
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateClient().SendAsync(Preflight("https://admin.shop.test"));

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}

public sealed class RateLimitTests
{
    private static readonly Dictionary<string, string?> Limits = new()
    {
        ["RateLimiting:Enabled"] = "true",
        ["RateLimiting:AuthPerMinute"] = "3",
        ["RateLimiting:RecommendPerMinute"] = "2",
        ["RateLimiting:GlobalPerMinute"] = "1000"
    };

    [Fact]
    public async Task Sign_in_attempts_are_limited_and_the_rejection_explains_when_to_retry()
    {
        using var factory = ApiFactory.With(new(Limits));
        var client = factory.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@shop.test", password = "Wrong-Password-1" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var limited = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@shop.test", password = "Wrong-Password-1" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.True(int.Parse(limited.Headers.GetValues("Retry-After").Single()) > 0);
    }

    [Fact]
    public async Task Recommendations_have_their_own_tighter_limit()
    {
        using var factory = ApiFactory.With(new(Limits));
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/products/recommend?query=mouse")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/products/recommend?query=mouse")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/v1/products/recommend?query=mouse")).StatusCode);

        // Other endpoints are unaffected by the recommendation budget.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/products")).StatusCode);
    }

    [Fact]
    public async Task The_global_limit_applies_to_the_api_but_never_to_health_probes()
    {
        var settings = new Dictionary<string, string?>(Limits) { ["RateLimiting:GlobalPerMinute"] = "5" };
        using var factory = ApiFactory.With(settings);
        var client = factory.CreateClient();

        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++)
        {
            statuses.Add((await client.GetAsync("/api/v1/products")).StatusCode);
        }

        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}
