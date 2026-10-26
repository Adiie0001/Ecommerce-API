using EcommerceAPI.Contracts;
using EcommerceAPI.Domain;

namespace EcommerceAPI.Services.Recommendations;

/// <summary>
/// Chooses the engine. With no AI configured only the deterministic engine runs and the answer says <c>rules</c>.
/// With AI configured, any failure (timeout, open circuit, HTTP error, unreadable or invalid answer) falls back to
/// the deterministic engine and the answer says <c>rules-fallback</c>, so callers can always tell what they got.
/// </summary>
public sealed class ResilientRecommender(
    RuleBasedRecommender rules,
    GeminiRecommender? gemini,
    ILogger<ResilientRecommender> logger) : IProductRecommender
{
    public async Task<RecommendationResponse> RecommendAsync(string query, IReadOnlyList<Product> catalog, CancellationToken ct)
    {
        if (gemini is null)
        {
            return await rules.RecommendAsync(query, catalog, ct);
        }

        try
        {
            return await gemini.RecommendAsync(query, catalog, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AI recommendation unavailable ({Reason}); using the rule-based engine", ex.GetType().Name);
            var fallback = await rules.RecommendAsync(query, catalog, ct);
            return fallback with { Source = RecommendationSources.RulesFallback };
        }
    }
}
