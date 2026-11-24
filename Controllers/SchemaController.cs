using Asp.Versioning;
using EcommerceAPI.Contracts;
using EcommerceAPI.Services.Schema;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceAPI.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/schema")]
[Produces("application/json")]
public sealed class SchemaController(ISchemaDiscoveryService schemaService) : ControllerBase
{
    /// <summary>
    /// Performs zero-model dynamic schema discovery across the database catalog.
    /// Returns reflected tables, columns, data types, primary keys, indexes, and relationships.
    /// </summary>
    [HttpGet("discovery")]
    [ProducesResponseType(typeof(SchemaDiscoveryResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SchemaDiscoveryResponseDto>> DiscoverSchema(CancellationToken ct)
    {
        var discovery = await schemaService.DiscoverSchemaAsync(ct);
        return Ok(discovery);
    }

    /// <summary>
    /// Returns dynamic metadata, column definitions, and live row count for a specific table.
    /// </summary>
    [HttpGet("tables/{tableName}")]
    [ProducesResponseType(typeof(DiscoveredTableDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiscoveredTableDto>> GetTable(string tableName, CancellationToken ct)
    {
        var table = await schemaService.GetTableMetadataAsync(tableName, ct);
        if (table == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Table Not Found",
                Detail = $"Relational table '{tableName}' was not found in database catalog."
            });
        }

        return Ok(table);
    }
}
