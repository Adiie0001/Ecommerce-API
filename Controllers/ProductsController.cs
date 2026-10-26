using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using EcommerceAPI.Contracts;
using EcommerceAPI.Infrastructure;
using EcommerceAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EcommerceAPI.Controllers;

/// <summary>The product catalog. Reads are public; writes need the Admin role.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products")]
public sealed class ProductsController(ProductService products) : ControllerBase
{
    /// <summary>List products with search, filters, sorting and paging.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ProductDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ProductDto>>> List([FromQuery] ProductQuery query, CancellationToken ct) =>
        Ok(await products.ListAsync(query, ct));

    /// <summary>Get one product.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> Get(int id, CancellationToken ct) =>
        Ok(await products.GetAsync(id, ct));

    /// <summary>
    /// Natural-language product search. The <c>source</c> field of the answer says which engine ranked the results:
    /// <c>gemini</c>, <c>rules</c> or <c>rules-fallback</c>.
    /// </summary>
    [HttpGet("recommend")]
    [EnableRateLimiting(RateLimitPolicies.Recommend)]
    [ProducesResponseType<RecommendationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RecommendationResponse>> Recommend([FromQuery] RecommendQuery request, CancellationToken ct) =>
        Ok(await products.RecommendAsync(request.Query, ct));

    /// <summary>Inventory totals for the admin dashboard.</summary>
    [HttpGet("stats")]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType<ProductStatsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProductStatsDto>> Stats([FromQuery, Range(0, 1_000_000)] int lowStockThreshold = 10, CancellationToken ct = default) =>
        Ok(await products.StatsAsync(lowStockThreshold, ct));

    /// <summary>Create a product (Admin).</summary>
    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProductDto>> Create([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        var created = await products.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1" }, created);
    }

    /// <summary>Replace a product (Admin). Send the <c>version</c> from the last read; a stale one returns 409.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> Update(int id, [FromBody] UpdateProductRequest request, CancellationToken ct) =>
        Ok(await products.UpdateAsync(id, request, ct));

    /// <summary>Delete a product (Admin).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }
}

public sealed class RecommendQuery
{
    /// <summary>What the shopper is looking for, in plain words.</summary>
    [Required, StringLength(200, MinimumLength = 2)]
    public string Query { get; set; } = string.Empty;
}
