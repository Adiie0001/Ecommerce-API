using System.Net;
using System.Net.Http.Json;
using EcommerceAPI.Tests.TestSupport;

namespace EcommerceAPI.Tests;

/// <summary>
/// One table that states, per route, what an anonymous caller, a customer and an admin may do. A route that gains or
/// loses protection changes a row here, so a missing attribute fails a test instead of shipping.
/// </summary>
public sealed class AuthorizationMatrixTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    public static TheoryData<string, string, HttpStatusCode, HttpStatusCode, HttpStatusCode> Matrix => new()
    {
        // method, path, anonymous, customer, admin
        { "GET", "/api/v1/products", HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK },
        { "GET", "/api/v1/products/recommend?query=mouse", HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK },
        { "GET", "/api/v1/products/stats", HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.OK },
        { "POST", "/api/v1/products", HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.Created },
        { "PUT", "/api/v1/products/987654", HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
        { "DELETE", "/api/v1/products/987654", HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
        { "GET", "/api/v1/auth/me", HttpStatusCode.Unauthorized, HttpStatusCode.OK, HttpStatusCode.OK },
        { "POST", "/api/product", HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.Created },
        { "DELETE", "/api/product/987654", HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound }
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Each_route_enforces_its_role_requirements(
        string method, string path, HttpStatusCode anonymous, HttpStatusCode customer, HttpStatusCode admin)
    {
        Assert.Equal(anonymous, (await SendAsync(factory.CreateClient(), method, path)).StatusCode);

        using var customerClient = await factory.CustomerClientAsync();
        Assert.Equal(customer, (await SendAsync(customerClient, method, path)).StatusCode);

        using var adminClient = await factory.AdminClientAsync();
        Assert.Equal(admin, (await SendAsync(adminClient, method, path)).StatusCode);
    }

    [Fact]
    public async Task Rejected_requests_never_reach_the_database()
    {
        using var customerClient = await factory.CustomerClientAsync();
        var name = $"should-not-exist-{Guid.NewGuid():N}";

        var denied = await customerClient.PostAsJsonAsync("/api/v1/products",
            new { name, description = "d", price = 1m, stock = 1 });
        var list = await factory.CreateClient().GetAsync($"/api/v1/products?q={name}");

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, (await list.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST")
        {
            request.Content = JsonContent.Create(new { name = $"matrix-{Guid.NewGuid():N}", description = "d", price = 1m, stock = 1 });
        }
        else if (method is "PUT")
        {
            request.Content = JsonContent.Create(new
            {
                name = "x", description = "d", price = 1m, stock = 1, version = Guid.NewGuid().ToString("N")
            });
        }

        return client.SendAsync(request);
    }
}
