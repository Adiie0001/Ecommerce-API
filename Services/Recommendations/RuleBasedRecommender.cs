using System.Text.RegularExpressions;
using EcommerceAPI.Contracts;
using EcommerceAPI.Domain;
using EcommerceAPI.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace EcommerceAPI.Services.Recommendations;

/// <summary>
/// Deterministic keyword ranking: stemmed token matching weighted by inverse document frequency, name matches
/// weighted above description matches, plus simple price-intent words ("cheap", "premium"). Needs no network and
/// gives the same answer for the same catalog and query, which is why it is also the fallback for the AI engine.
/// </summary>
public sealed partial class RuleBasedRecommender(IOptions<RecommendationOptions> options) : IProductRecommender
{
    private const double NameWeight = 3.0;
    private const double DescriptionWeight = 1.0;
    private const double PrefixFactor = 0.7;
    private const double PhraseBonus = 3.0;
    private const double IntentBonus = 1.5;
    private const double OutOfStockFactor = 0.5;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "any", "are", "as", "at", "be", "best", "by", "for", "from", "good", "i", "in", "is", "it",
        "me", "my", "need", "of", "on", "or", "show", "some", "something", "the", "to", "want", "with"
    };

    private static readonly HashSet<string> BudgetWords = new(StringComparer.Ordinal)
    {
        "cheap", "cheaper", "budget", "affordable", "inexpensive", "lowcost", "value"
    };

    private static readonly HashSet<string> PremiumWords = new(StringComparer.Ordinal)
    {
        "premium", "pro", "flagship", "highend", "luxury", "professional"
    };

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex Separators();

    public Task<RecommendationResponse> RecommendAsync(string query, IReadOnlyList<Product> catalog, CancellationToken ct) =>
        Task.FromResult(new RecommendationResponse(
            query,
            RecommendationSources.Rules,
            Rank(query, catalog).Take(options.Value.MaxResults).Select(ToItem).ToList()));

    /// <summary>Every product with a positive score, best first. Also used to pre-select candidates for the AI engine.</summary>
    internal static IReadOnlyList<ScoredProduct> Rank(string query, IReadOnlyList<Product> catalog)
    {
        var tokens = Tokenize(query);
        var wantsBudget = tokens.Any(BudgetWords.Contains);
        var wantsPremium = tokens.Any(PremiumWords.Contains);
        var terms = tokens.Where(t => !BudgetWords.Contains(t) && !PremiumWords.Contains(t)).Distinct().ToList();

        if (catalog.Count == 0 || (terms.Count == 0 && !wantsBudget && !wantsPremium))
        {
            return [];
        }

        var documents = catalog.Select(p => new Document(p, Tokenize(p.Name).ToHashSet(), Tokenize(p.Description).ToHashSet())).ToList();
        var idf = terms.ToDictionary(t => t, t => Idf(t, documents));

        var prices = catalog.Select(p => p.PriceCents).Order().ToArray();
        var median = prices[prices.Length / 2];
        var upperQuartile = prices[(int)Math.Min(prices.Length - 1, Math.Ceiling(prices.Length * 0.75) - 1)];

        var phrase = string.Join(' ', Tokenize(query));
        var maxRaw = terms.Sum(t => NameWeight * idf[t]) + (terms.Count > 1 ? PhraseBonus : 0) + (wantsBudget || wantsPremium ? IntentBonus : 0);

        var scored = new List<ScoredProduct>();
        foreach (var doc in documents)
        {
            var raw = 0.0;
            var notes = new List<string>();

            foreach (var term in terms)
            {
                if (doc.Name.Contains(term))
                {
                    raw += NameWeight * idf[term];
                    notes.Add($"name matches \"{term}\"");
                }
                else if (doc.Description.Contains(term))
                {
                    raw += DescriptionWeight * idf[term];
                    notes.Add($"description mentions \"{term}\"");
                }
                else if (term.Length >= 4 && doc.Name.Any(n => n.StartsWith(term, StringComparison.Ordinal)))
                {
                    raw += NameWeight * PrefixFactor * idf[term];
                    notes.Add($"name contains \"{term}\"");
                }
                else if (term.Length >= 4 && doc.Description.Any(d => d.StartsWith(term, StringComparison.Ordinal)))
                {
                    raw += DescriptionWeight * PrefixFactor * idf[term];
                    notes.Add($"description contains \"{term}\"");
                }
            }

            if (terms.Count > 1 && phrase.Length > 0 && string.Join(' ', Tokenize(doc.Product.Name)).Contains(phrase, StringComparison.Ordinal))
            {
                raw += PhraseBonus;
                notes.Add("name contains the whole phrase");
            }

            var hasTermMatch = raw > 0;
            if (wantsBudget && doc.Product.PriceCents <= median && (hasTermMatch || terms.Count == 0))
            {
                raw += IntentBonus;
                notes.Add("budget-friendly price");
            }

            if (wantsPremium && doc.Product.PriceCents >= upperQuartile && (hasTermMatch || terms.Count == 0))
            {
                raw += IntentBonus;
                notes.Add("premium price tier");
            }

            if (raw <= 0)
            {
                continue;
            }

            if (doc.Product.Stock == 0)
            {
                raw *= OutOfStockFactor;
                notes.Add("currently out of stock");
            }

            var score = (int)Math.Clamp(Math.Round(100 * raw / Math.Max(maxRaw, 0.0001)), 1, 99);
            scored.Add(new ScoredProduct(doc.Product, score, raw, string.Join("; ", notes) + "."));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.Raw)
            .ThenBy(s => s.Product.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Product.Id)
            .ToList();
    }

    private static RecommendationItem ToItem(ScoredProduct scored) =>
        new(scored.Product.ToDto(), scored.Score, scored.Reason);

    private static double Idf(string term, List<Document> documents)
    {
        var containing = documents.Count(d => d.Name.Contains(term) || d.Description.Contains(term));
        return Math.Log(1 + documents.Count / (1.0 + containing));
    }

    internal static List<string> Tokenize(string text) =>
        Separators()
            .Split(text.ToLowerInvariant())
            .Where(t => t.Length > 1 && !StopWords.Contains(t))
            .Select(Stem)
            .ToList();

    /// <summary>Tiny plural stemmer: "keyboards" and "keyboard", "batteries" and "battery" meet in the middle.</summary>
    private static string Stem(string token)
    {
        if (token.Length > 4 && token.EndsWith("ies", StringComparison.Ordinal))
        {
            return token[..^3] + "y";
        }

        if (token.Length > 3 && token.EndsWith('s') && !token.EndsWith("ss", StringComparison.Ordinal))
        {
            return token[..^1];
        }

        return token;
    }

    private sealed record Document(Product Product, HashSet<string> Name, HashSet<string> Description);

    internal sealed record ScoredProduct(Product Product, int Score, double Raw, string Reason);
}
