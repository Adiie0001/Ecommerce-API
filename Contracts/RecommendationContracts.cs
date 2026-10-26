namespace EcommerceAPI.Contracts;

/// <param name="Product">The recommended product.</param>
/// <param name="Score">Relevance from 0 to 100. A ranking signal, not a calibrated probability.</param>
/// <param name="Reason">Short human-readable explanation of the match.</param>
public sealed record RecommendationItem(ProductDto Product, int Score, string Reason);

/// <param name="Query">The query that was ranked.</param>
/// <param name="Items">Matches, best first. Empty when nothing is relevant.</param>
/// <param name="Source">
/// Which engine produced the answer: <c>gemini</c>, <c>rules</c> (deterministic engine, AI not configured)
/// or <c>rules-fallback</c> (AI was configured but unavailable, so the deterministic engine answered).
/// </param>
public sealed record RecommendationResponse(string Query, string Source, IReadOnlyList<RecommendationItem> Items);

public static class RecommendationSources
{
    public const string Gemini = "gemini";
    public const string Rules = "rules";
    public const string RulesFallback = "rules-fallback";
}
