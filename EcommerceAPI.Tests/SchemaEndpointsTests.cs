using System.Net;
using System.Net.Http.Json;
using EcommerceAPI.Contracts;
using EcommerceAPI.Tests.TestSupport;

namespace EcommerceAPI.Tests;

public sealed class SchemaEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task DiscoverSchema_returns_200_with_all_database_tables_and_columns()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/schema/discovery");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var schema = await response.ReadAsync<SchemaDiscoveryResponseDto>();

        Assert.Equal("SQLite", schema.Engine);
        Assert.True(schema.TotalTables >= 3, $"Expected at least 3 tables, found {schema.TotalTables}");
        Assert.NotEmpty(schema.Tables);

        var tableNames = schema.Tables.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Users", tableNames);
        Assert.Contains("Products", tableNames);
        Assert.Contains("RefreshTokens", tableNames);

        var productsTable = schema.Tables.First(t => string.Equals(t.Name, "Products", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("TABLE", productsTable.Type);
        Assert.NotEmpty(productsTable.Columns);

        var idCol = productsTable.Columns.FirstOrDefault(c => string.Equals(c.Name, "Id", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(idCol);
        Assert.True(idCol.IsPrimaryKey);

        var nameCol = productsTable.Columns.FirstOrDefault(c => string.Equals(c.Name, "Name", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(nameCol);
        Assert.False(nameCol.IsNullable);
    }

    [Fact]
    public async Task GetTable_returns_200_for_existing_table_with_metadata()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/schema/tables/Products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var table = await response.ReadAsync<DiscoveredTableDto>();

        Assert.Equal("Products", table.Name);
        Assert.Equal("TABLE", table.Type);
        Assert.NotEmpty(table.Columns);
        Assert.True(table.RowCount >= 0);
    }

    [Fact]
    public async Task GetTable_returns_404_for_non_existent_table()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/schema/tables/NonExistentTableXYZ");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await response.ReadJsonAsync();
        Assert.Equal(404, json.GetProperty("status").GetInt32());
        Assert.Equal("Table Not Found", json.GetProperty("title").GetString());
    }
}
