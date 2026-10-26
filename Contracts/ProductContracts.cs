using System.ComponentModel.DataAnnotations;
using EcommerceAPI.Domain;

namespace EcommerceAPI.Contracts;

public abstract class ProductWriteRequest : IValidatableObject
{
    [Required, StringLength(Product.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(Product.MaxDescriptionLength)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Price in major units with at most two decimal places.</summary>
    [Range(typeof(decimal), "0.01", "1000000")]
    public decimal Price { get; set; }

    [Range(0, 1_000_000)]
    public int Stock { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Money.HasAtMostTwoDecimals(Price))
        {
            yield return new ValidationResult("Price can have at most two decimal places.", [nameof(Price)]);
        }
    }
}

public sealed class CreateProductRequest : ProductWriteRequest;

public sealed class UpdateProductRequest : ProductWriteRequest
{
    /// <summary>The <c>version</c> returned by the last read. A stale value results in 409 Conflict.</summary>
    [Required, StringLength(36)]
    public string Version { get; set; } = string.Empty;
}

/// <param name="Id">Product id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Short description.</param>
/// <param name="Price">Price in major units.</param>
/// <param name="Stock">Units available.</param>
/// <param name="Version">Opaque concurrency token to send back when updating.</param>
/// <param name="CreatedAt">When the product was created (UTC).</param>
/// <param name="UpdatedAt">When the product last changed (UTC).</param>
public sealed record ProductDto(
    int Id,
    string Name,
    string Description,
    decimal Price,
    int Stock,
    string Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum ProductSort
{
    Name,
    Price,
    Stock,
    Created,
    Updated
}

public enum SortOrder
{
    Asc,
    Desc
}

public sealed class ProductQuery : IValidatableObject
{
    [Range(1, 1_000_000)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    /// <summary>Case-insensitive text search over name and description.</summary>
    [StringLength(100)]
    public string? Q { get; set; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal? MinPrice { get; set; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal? MaxPrice { get; set; }

    /// <summary>True: only items with stock. False: only sold-out items. Omitted: all.</summary>
    public bool? InStock { get; set; }

    public ProductSort Sort { get; set; } = ProductSort.Name;

    public SortOrder Order { get; set; } = SortOrder.Asc;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice is { } min && MaxPrice is { } max && min > max)
        {
            yield return new ValidationResult("MinPrice cannot be greater than MaxPrice.", [nameof(MinPrice), nameof(MaxPrice)]);
        }
    }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <param name="TotalProducts">Number of products in the catalog.</param>
/// <param name="OutOfStock">Products with zero stock.</param>
/// <param name="LowStock">Items with stock above zero and at or below <paramref name="LowStockThreshold"/>.</param>
/// <param name="LowStockThreshold">The threshold used for <paramref name="LowStock"/>.</param>
/// <param name="TotalUnits">Total units in stock across the catalog.</param>
/// <param name="InventoryValue">Sum of price times stock over the whole catalog, in major units.</param>
public sealed record ProductStatsDto(
    int TotalProducts,
    int OutOfStock,
    int LowStock,
    int LowStockThreshold,
    long TotalUnits,
    decimal InventoryValue);
