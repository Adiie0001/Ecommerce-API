using EcommerceAPI.Contracts;
using EcommerceAPI.Domain;

namespace EcommerceAPI.Services.Recommendations;

/// <summary>
/// Ranks catalog items for a free-text shopper query. This is the seam between the shop and any AI provider:
/// implementations differ in how they rank, never in what they return.
/// </summary>
public interface IProductRecommender
{
    Task<RecommendationResponse> RecommendAsync(string query, IReadOnlyList<Product> catalog, CancellationToken ct);
}
