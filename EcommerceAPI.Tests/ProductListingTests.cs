using System.Net;
using EcommerceAPI.Contracts;
using EcommerceAPI.Tests.TestSupport;

namespace EcommerceAPI.Tests;

public sealed class ProductListingTests(SeededCatalogFixture fixture) : IClassFixture<SeededCatalogFixture>
{
    private HttpClient Client => fixture.Factory.CreateClient();

    private async Task<PagedResult<ProductDto>> ListAsync(string query = "")
    {
        var response = await Client.GetAsync($"/api/v1/products{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PagedResult<ProductDto>>();
    }

    [Fact]
    public async Task Listing_is_public_and_defaults_to_name_order_with_a_paging_envelope()
    {
        var page = await ListAsync();

        Assert.Equal(7, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(1, page.TotalPages);
        Assert.Equal(page.Items.Select(p => p.Name).Order(StringComparer.Ordinal), page.Items.Select(p => p.Name));
    }

    [Fact]
    public async Task Paging_returns_the_requested_slice_and_reports_totals()
    {
        var second = await ListAsync("?pageSize=3&page=2");
        var last = await ListAsync("?pageSize=3&page=3");
        var beyond = await ListAsync("?pageSize=3&page=4");

        Assert.Equal(3, second.Items.Count);
        Assert.Equal(3, second.TotalPages);
        Assert.Single(last.Items);
        Assert.Empty(beyond.Items);
        Assert.Equal(7, beyond.TotalCount);
    }

    [Fact]
    public async Task Paging_through_ties_neither_repeats_nor_skips_items()
    {
        var seen = new List<int>();
        for (var page = 1; page <= 4; page++)
        {
            seen.AddRange((await ListAsync($"?sort=stock&pageSize=2&page={page}")).Items.Select(p => p.Id));
        }

        Assert.Equal(7, seen.Count);
        Assert.Equal(7, seen.Distinct().Count());
    }

    [Theory]
    [InlineData("?sort=price&order=desc", "Gaming Monitor")]
    [InlineData("?sort=price&order=asc", "USB-C Cable")]
    [InlineData("?sort=stock&order=desc", "USB-C Cable")]
    [InlineData("?sort=name&order=desc", "Wireless Mouse")]
    [InlineData("?sort=name&order=asc", "Desk Lamp")]
    [InlineData("?sort=stock&order=asc", "Mechanical Keyboard")]
    [InlineData("?sort=created&order=asc", "Wireless Mouse")]
    [InlineData("?sort=created&order=desc", "Webcam")]
    [InlineData("?sort=updated&order=asc", "Wireless Mouse")]
    [InlineData("?sort=updated&order=desc", "Webcam")]
    public async Task Sorting_orders_the_whole_result_not_just_the_page(string query, string expectedFirst)
    {
        var page = await ListAsync(query);

        Assert.Equal(expectedFirst, page.Items[0].Name);
    }

    [Fact]
    public async Task Stock_filter_splits_the_catalog()
    {
        var available = await ListAsync("?inStock=true");
        var soldOut = await ListAsync("?inStock=false");

        Assert.Equal(5, available.TotalCount);
        Assert.All(available.Items, p => Assert.True(p.Stock > 0));
        Assert.Equal(2, soldOut.TotalCount);
        Assert.All(soldOut.Items, p => Assert.Equal(0, p.Stock));
    }

    [Fact]
    public async Task Price_range_is_inclusive_and_exact_to_the_cent()
    {
        var page = await ListAsync("?minPrice=19.99&maxPrice=34.00&sort=price");

        Assert.Equal(["Desk Lamp", "Wireless Mouse", "Laptop Stand"], page.Items.Select(p => p.Name));
    }

    [Theory]
    [InlineData("wireless", "Wireless Mouse")]
    [InlineData("GAMING", "Gaming Monitor")]
    [InlineData("aluminium", "Laptop Stand")]
    public async Task Search_matches_name_or_description_ignoring_case(string q, string expected)
    {
        var page = await ListAsync($"?q={q}");

        Assert.Equal([expected], page.Items.Select(p => p.Name));
    }

    [Fact]
    public async Task Search_treats_percent_as_a_literal_not_a_wildcard()
    {
        var page = await ListAsync("?q=100%25&sort=name");

        Assert.Equal(["Mechanical Keyboard", "USB-C Cable"], page.Items.Select(p => p.Name));
    }

    [Fact]
    public async Task Search_treats_underscore_as_a_literal_not_a_wildcard()
    {
        var page = await ListAsync("?q=W_reless");

        Assert.Empty(page.Items);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?sort=bogus")]
    [InlineData("?order=sideways")]
    [InlineData("?minPrice=-1")]
    [InlineData("?minPrice=50&maxPrice=10")]
    [InlineData("?inStock=maybe")]
    public async Task Invalid_listing_parameters_return_a_validation_problem(string query)
    {
        var response = await Client.GetAsync($"/api/v1/products{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Stats_summarise_the_catalog_for_admins()
    {
        using var admin = await fixture.Factory.AdminClientAsync();

        var stats = await (await admin.GetAsync("/api/v1/products/stats")).ReadAsync<ProductStatsDto>();
        var tight = await (await admin.GetAsync("/api/v1/products/stats?lowStockThreshold=3")).ReadAsync<ProductStatsDto>();

        Assert.Equal(7, stats.TotalProducts);
        Assert.Equal(2, stats.OutOfStock);
        Assert.Equal(4, stats.LowStock);
        Assert.Equal(122, stats.TotalUnits);
        Assert.Equal(2356.90m, stats.InventoryValue);
        Assert.Equal(2, tight.LowStock);
        Assert.Equal(3, tight.LowStockThreshold);
    }

    [Fact]
    public async Task Legacy_product_paths_are_aliases_of_the_versioned_routes()
    {
        var response = await Client.GetAsync("/api/product?pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.ReadAsync<PagedResult<ProductDto>>()).Items.Count);
        Assert.Equal("true", response.Headers.GetValues("Deprecation").Single());
        Assert.Contains("</api/v1/products>", response.Headers.GetValues("Link").Single());

        var one = await Client.GetAsync($"/api/product/{fixture.Products["Webcam"].Id}");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
    }

    [Fact]
    public async Task Unversioned_plural_path_does_not_exist()
    {
        var response = await Client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
