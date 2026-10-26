using System.Security.Cryptography;
using System.Text;
using EcommerceAPI.Contracts;
using EcommerceAPI.Domain;
using EcommerceAPI.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EcommerceAPI.Services.Recommendations;

/// <summary>
/// Asks a language model to rank a bounded set of candidate products. The model's answer is untrusted input: ids must
/// exist in the candidate set, scores are clamped, and reasons are reduced to short plain text before they are returned.
/// Any failure surfaces as <see cref="RecommendationUnavailableException"/> so the caller can fall back.
/// </summary>
public sealed class GeminiRecommender(
    IGeminiClient client,
    IMemoryCache cache,
    IOptions<RecommendationOptions> options) : IProductRecommender
{
    private const int MaxReasonLength = 200;

    public async Task<RecommendationResponse> RecommendAsync(string query, IReadOnlyList<Product> catalog, CancellationToken ct)
    {
        var settings = options.Value;
        if (catalog.Count == 0)
        {
            return new RecommendationResponse(query, RecommendationSources.Gemini, []);
        }

        var cacheKey = CacheKey(query, catalog);
        if (cache.TryGetValue(cacheKey, out RecommendationResponse? cached) && cached is not null)
        {
            return cached;
        }

        var candidates = SelectCandidates(query, catalog, settings.Gemini.MaxCandidates);
        var picks = await client.RankAsync(
            query,
            candidates.Select(p => new GeminiCandidate(p.Id, p.Name, Truncate(p.Description, 160), p.Price, p.Stock > 0)).ToList(),
            settings.MaxResults,
            ct);

        var byId = candidates.ToDictionary(p => p.Id);
        var items = picks
            .Where(pick => byId.ContainsKey(pick.ProductId))
            .DistinctBy(pick => pick.ProductId)
            .OrderByDescending(pick => pick.Score)
            .Take(settings.MaxResults)
            .Select(pick => new RecommendationItem(byId[pick.ProductId].ToDto(), Math.Clamp(pick.Score, 0, 100), CleanReason(pick.Reason)))
            .ToList();

        if (picks.Count > 0 && items.Count == 0)
        {
            throw new RecommendationUnavailableException("Gemini referenced only products that are not in the catalog.");
        }

        var response = new RecommendationResponse(query, RecommendationSources.Gemini, items);
        if (settings.Gemini.CacheMinutes > 0)
        {
            cache.Set(cacheKey, response, TimeSpan.FromMinutes(settings.Gemini.CacheMinutes));
        }

        return response;
    }

    /// <summary>
    /// Small catalogs go to the model whole. Larger ones are cut down to the best lexical candidates, topped up with
    /// in-stock items so the model can still find synonyms the keyword pass missed.
    /// </summary>
    internal static IReadOnlyList<Product> SelectCandidates(string query, IReadOnlyList<Product> catalog, int max)
    {
        if (catalog.Count <= max)
        {
            return catalog;
        }

        var selected = RuleBasedRecommender.Rank(query, catalog).Select(s => s.Product).Take(max).ToList();
        var chosen = selected.Select(p => p.Id).ToHashSet();

        foreach (var product in catalog.Where(p => p.Stock > 0).OrderBy(p => p.Id))
        {
            if (selected.Count >= max)
            {
                break;
            }

            if (chosen.Add(product.Id))
            {
                selected.Add(product);
            }
        }

        return selected;
    }

    private static string CacheKey(string query, IReadOnlyList<Product> catalog)
    {
        // The catalog fingerprint makes cached answers expire the moment a product changes.
        var newest = catalog.Max(p => p.UpdatedAt).ToUnixTimeMilliseconds();
        var normalized = string.Join(' ', query.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return $"gemini:{digest}:{catalog.Count}:{newest}";
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private static string CleanReason(string? reason)
    {
        var cleaned = new string((reason ?? string.Empty).Where(c => !char.IsControl(c)).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length <= MaxReasonLength ? cleaned : cleaned[..MaxReasonLength].TrimEnd() + "…";
    }
}
