using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EcommerceAPI.Contracts;

namespace EcommerceAPI.Tests.TestSupport;

public static class TestHelpers
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string NewEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@shop.test";

    public static HttpClient WithBearer(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(Json);
        return value ?? throw new InvalidOperationException($"Response body was empty ({(int)response.StatusCode}).");
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public static async Task<AuthResponse> LoginAsync(this ApiFactory factory, string email, string password)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<AuthResponse>();
    }

    /// <summary>Registers a fresh customer and signs in. Returns the credentials and the token pair.</summary>
    public static async Task<(string Email, AuthResponse Auth)> NewCustomerAsync(this ApiFactory factory)
    {
        var email = NewEmail("customer");
        using var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, displayName = "Test Customer", password = ApiFactory.CustomerPassword });
        Assert.Equal(System.Net.HttpStatusCode.Created, register.StatusCode);
        return (email, await factory.LoginAsync(email, ApiFactory.CustomerPassword));
    }

    public static Task<AuthResponse> LoginAdminAsync(this ApiFactory factory) =>
        factory.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

    public static async Task<HttpClient> AdminClientAsync(this ApiFactory factory)
    {
        var admin = await factory.LoginAdminAsync();
        return factory.CreateClient().WithBearer(admin.AccessToken);
    }

    public static async Task<HttpClient> CustomerClientAsync(this ApiFactory factory)
    {
        var (_, auth) = await factory.NewCustomerAsync();
        return factory.CreateClient().WithBearer(auth.AccessToken);
    }

    public static async Task<ProductDto> CreateProductAsync(
        this HttpClient admin, string name, string description = "A test product", decimal price = 10m, int stock = 5)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/products", new { name, description, price, stock });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<ProductDto>();
    }
}
