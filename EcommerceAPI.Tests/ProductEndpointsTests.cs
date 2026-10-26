using System.Net;
using System.Net.Http.Json;
using EcommerceAPI.Contracts;
using EcommerceAPI.Tests.TestSupport;

namespace EcommerceAPI.Tests;

public sealed class ProductEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Creating_a_product_returns_201_with_a_location_and_a_version()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/products",
            new { name = "  Trim Me  ", description = "  padded  ", price = 12.34m, stock = 7 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = await response.ReadAsync<ProductDto>();
        Assert.Equal("Trim Me", product.Name);
        Assert.Equal("padded", product.Description);
        Assert.Equal(12.34m, product.Price);
        Assert.Equal(7, product.Stock);
        Assert.False(string.IsNullOrEmpty(product.Version));
        Assert.EndsWith($"/api/v1/products/{product.Id}", response.Headers.Location?.AbsolutePath);

        var fetched = await factory.CreateClient().GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Theory]
    [InlineData("", "desc", 10, 5, "name")]
    [InlineData("   ", "desc", 10, 5, "name")]
    [InlineData("name", "", 10, 5, "description")]
    [InlineData("name", "desc", 0, 5, "price")]
    [InlineData("name", "desc", -5, 5, "price")]
    [InlineData("name", "desc", 1.234, 5, "price")]
    [InlineData("name", "desc", 10000000, 5, "price")]
    [InlineData("name", "desc", 10, -1, "stock")]
    public async Task Invalid_products_are_rejected_with_camel_case_field_errors(
        string name, string description, double price, int stock, string expectedField)
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/products",
            new { name, description, price = (decimal)price, stock });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.ReadJsonAsync();
        Assert.True(json.GetProperty("errors").TryGetProperty(expectedField, out _), json.ToString());
    }

    [Fact]
    public async Task Names_longer_than_the_limit_are_rejected()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/products",
            new { name = new string('x', 101), description = "d", price = 1m, stock = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_product_is_a_404_problem_with_trace_and_correlation_ids()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/products/424242");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.ReadJsonAsync();
        Assert.Equal(404, json.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
        Assert.Equal(response.Headers.GetValues("X-Correlation-ID").Single(), json.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Updating_with_the_current_version_changes_the_product_and_issues_a_new_version()
    {
        using var admin = await factory.AdminClientAsync();
        var original = await admin.CreateProductAsync("Before", "old", 10m, 5);

        var response = await admin.PutAsJsonAsync($"/api/v1/products/{original.Id}",
            new { name = "After", description = "new", price = 15.5m, stock = 9, version = original.Version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadAsync<ProductDto>();
        Assert.Equal("After", updated.Name);
        Assert.Equal(15.5m, updated.Price);
        Assert.NotEqual(original.Version, updated.Version);

        var reread = await (await admin.GetAsync($"/api/v1/products/{original.Id}")).ReadAsync<ProductDto>();
        Assert.Equal(updated.Version, reread.Version);
        Assert.Equal(9, reread.Stock);
    }

    [Fact]
    public async Task Updating_with_a_stale_version_returns_409_and_keeps_the_newer_data()
    {
        using var admin = await factory.AdminClientAsync();
        var original = await admin.CreateProductAsync("Contended", "d", 10m, 5);

        var first = await admin.PutAsJsonAsync($"/api/v1/products/{original.Id}",
            new { name = "Winner", description = "d", price = 10m, stock = 5, version = original.Version });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await admin.PutAsJsonAsync($"/api/v1/products/{original.Id}",
            new { name = "Loser", description = "d", price = 10m, stock = 5, version = original.Version });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var current = await (await admin.GetAsync($"/api/v1/products/{original.Id}")).ReadAsync<ProductDto>();
        Assert.Equal("Winner", current.Name);

        // Re-reading and applying the edit again succeeds.
        var retry = await admin.PutAsJsonAsync($"/api/v1/products/{original.Id}",
            new { name = "Loser", description = "d", price = 10m, stock = 5, version = current.Version });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task Concurrent_updates_from_the_same_version_let_exactly_one_win()
    {
        using var admin = await factory.AdminClientAsync();
        var original = await admin.CreateProductAsync("Race", "d", 10m, 5);

        var attempts = Enumerable.Range(0, 6).Select(i => admin.PutAsJsonAsync($"/api/v1/products/{original.Id}",
            new { name = $"Racer {i}", description = "d", price = 10m, stock = 5, version = original.Version })).ToArray();
        var statuses = (await Task.WhenAll(attempts)).Select(r => r.StatusCode).ToList();

        Assert.Single(statuses, HttpStatusCode.OK);
        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.Conflict));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task Updating_without_a_usable_version_is_rejected(string version)
    {
        using var admin = await factory.AdminClientAsync();
        var original = await admin.CreateProductAsync("Versioned", "d", 10m, 5);

        var response = await admin.PutAsJsonAsync($"/api/v1/products/{original.Id}",
            new { name = "x", description = "d", price = 10m, stock = 5, version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.ReadJsonAsync();
        Assert.True(json.GetProperty("errors").TryGetProperty("version", out _));
    }

    [Fact]
    public async Task Updating_an_unknown_product_returns_404()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PutAsJsonAsync("/api/v1/products/987654",
            new { name = "x", description = "d", price = 10m, stock = 5, version = Guid.NewGuid().ToString("N") });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_removes_the_product_and_a_second_delete_is_a_404()
    {
        using var admin = await factory.AdminClientAsync();
        var product = await admin.CreateProductAsync("Doomed", "d", 10m, 5);

        var first = await admin.DeleteAsync($"/api/v1/products/{product.Id}");
        var second = await admin.DeleteAsync($"/api/v1/products/{product.Id}");
        var fetched = await admin.GetAsync($"/api/v1/products/{product.Id}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);
    }

    [Fact]
    public async Task Legacy_paths_can_still_create_products_for_admins()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/product",
            new { name = "Legacy Create", description = "via alias", price = 5m, stock = 1 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("true", response.Headers.GetValues("Deprecation").Single());
    }

    [Fact]
    public async Task Unknown_ids_that_do_not_fit_an_int_are_a_plain_404()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/products/99999999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
