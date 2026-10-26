using EcommerceAPI.Contracts;
using EcommerceAPI.Data;
using EcommerceAPI.Domain;
using EcommerceAPI.Infrastructure.Options;
using EcommerceAPI.Services.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EcommerceAPI.Services;

public sealed class ProductService(
    AppDbContext db,
    IProductRecommender recommender,
    IOptions<RecommendationOptions> recommendationOptions,
    TimeProvider clock)
{
    public async Task<PagedResult<ProductDto>> ListAsync(ProductQuery query, CancellationToken ct)
    {
        var products = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = $"%{EscapeLike(query.Q.Trim())}%";
            products = products.Where(p =>
                EF.Functions.Like(p.Name, pattern, "\\") || EF.Functions.Like(p.Description, pattern, "\\"));
        }

        if (query.MinPrice is { } min)
        {
            var cents = Money.ToCents(min);
            products = products.Where(p => p.PriceCents >= cents);
        }

        if (query.MaxPrice is { } max)
        {
            var cents = Money.ToCents(max);
            products = products.Where(p => p.PriceCents <= cents);
        }

        if (query.InStock is { } inStock)
        {
            products = inStock ? products.Where(p => p.Stock > 0) : products.Where(p => p.Stock == 0);
        }

        var total = await products.CountAsync(ct);
        var page = await Order(products, query.Sort, query.Order == SortOrder.Desc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<ProductDto>(page.Select(p => p.ToDto()).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<ProductDto> GetAsync(int id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return (product ?? throw NotFound(id)).ToDto();
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var product = new Product
        {
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            Price = request.Price,
            Stock = request.Stock,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return product.ToDto();
    }

    /// <summary>
    /// Optimistic concurrency: the caller must send the <c>version</c> it last read. If the row changed since, the
    /// UPDATE matches nothing and the caller gets a 409 instead of silently overwriting someone else's edit.
    /// </summary>
    public async Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(request.Version, out var expectedStamp))
        {
            throw new ValidationFailedException("version", "Version is not valid; send the version returned by the last read.");
        }

        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound(id);

        db.Entry(product).Property(p => p.ConcurrencyStamp).OriginalValue = expectedStamp;
        product.Name = request.Name.Trim();
        product.Description = request.Description.Trim();
        product.Price = request.Price;
        product.Stock = request.Stock;
        product.UpdatedAt = clock.GetUtcNow();
        product.ConcurrencyStamp = Guid.NewGuid();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The product was changed by someone else. Reload it and apply your edit again.");
        }

        return product.ToDto();
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var deleted = await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0)
        {
            throw NotFound(id);
        }
    }

    public async Task<ProductStatsDto> StatsAsync(int lowStockThreshold, CancellationToken ct)
    {
        var products = db.Products.AsNoTracking();

        return new ProductStatsDto(
            TotalProducts: await products.CountAsync(ct),
            OutOfStock: await products.CountAsync(p => p.Stock == 0, ct),
            LowStock: await products.CountAsync(p => p.Stock > 0 && p.Stock <= lowStockThreshold, ct),
            LowStockThreshold: lowStockThreshold,
            TotalUnits: await products.SumAsync(p => (long)p.Stock, ct),
            InventoryValue: await products.SumAsync(p => p.PriceCents * p.Stock, ct) / 100m);
    }

    public async Task<RecommendationResponse> RecommendAsync(string query, CancellationToken ct)
    {
        var catalog = await db.Products.AsNoTracking()
            .OrderBy(p => p.Id)
            .Take(recommendationOptions.Value.MaxCatalogItems)
            .ToListAsync(ct);

        return await recommender.RecommendAsync(query.Trim(), catalog, ct);
    }

    private static IQueryable<Product> Order(IQueryable<Product> products, ProductSort sort, bool descending)
    {
        IOrderedQueryable<Product> ordered = (sort, descending) switch
        {
            (ProductSort.Price, false) => products.OrderBy(p => p.PriceCents),
            (ProductSort.Price, true) => products.OrderByDescending(p => p.PriceCents),
            (ProductSort.Stock, false) => products.OrderBy(p => p.Stock),
            (ProductSort.Stock, true) => products.OrderByDescending(p => p.Stock),
            (ProductSort.Created, false) => products.OrderBy(p => p.CreatedAt),
            (ProductSort.Created, true) => products.OrderByDescending(p => p.CreatedAt),
            (ProductSort.Updated, false) => products.OrderBy(p => p.UpdatedAt),
            (ProductSort.Updated, true) => products.OrderByDescending(p => p.UpdatedAt),
            (_, true) => products.OrderByDescending(p => p.Name),
            _ => products.OrderBy(p => p.Name)
        };

        // The id tiebreaker keeps paging stable when many rows share the sort value.
        return ordered.ThenBy(p => p.Id);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static NotFoundException NotFound(int id) => new($"Product {id} was not found.");
}
