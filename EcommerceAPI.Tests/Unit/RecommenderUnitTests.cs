using EcommerceAPI.Domain;
using EcommerceAPI.Infrastructure.Options;
using EcommerceAPI.Services.Recommendations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EcommerceAPI.Tests.Unit;

public sealed class RuleBasedRecommenderTests
{
    private static Product P(int id, string name, string description, decimal price = 20m, int stock = 5) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        Price = price,
        Stock = stock,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch
    };

    private static IReadOnlyList<string> Names(string query, params Product[] catalog) =>
        RuleBasedRecommender.Rank(query, catalog).Select(s => s.Product.Name).ToList();

    [Fact]
    public void A_name_match_outranks_a_description_match()
    {
        var ranked = Names("lamp",
            P(1, "Desk Accessory", "includes a small lamp"),
            P(2, "Reading Lamp", "bright light"));

        Assert.Equal(["Reading Lamp", "Desk Accessory"], ranked);
    }

    [Fact]
    public void A_rare_term_outweighs_a_common_one()
    {
        var catalog = new[]
        {
            P(1, "Basic Cable", "plain cable"),
            P(2, "Long Cable", "long cable"),
            P(3, "Short Cable", "short cable"),
            P(4, "Thunderbolt Cable", "fast cable")
        };

        Assert.Equal("Thunderbolt Cable", Names("thunderbolt cable", catalog)[0]);
    }

    [Fact]
    public void Sold_out_items_rank_below_otherwise_identical_available_ones()
    {
        var ranked = RuleBasedRecommender.Rank("mouse",
        [
            P(1, "Mouse A", "wireless", stock: 0),
            P(2, "Mouse B", "wireless", stock: 4)
        ]);

        Assert.Equal(["Mouse B", "Mouse A"], ranked.Select(s => s.Product.Name));
        Assert.True(ranked[0].Score > ranked[1].Score);
        Assert.Contains("out of stock", ranked[1].Reason);
    }

    [Fact]
    public void A_word_stem_matches_the_longer_word_but_scores_lower_than_an_exact_match()
    {
        var ranked = RuleBasedRecommender.Rank("head",
        [
            P(1, "Headphones", "over-ear"),
            P(2, "Head Strap", "elastic")
        ]);

        Assert.Equal(["Head Strap", "Headphones"], ranked.Select(s => s.Product.Name));
    }

    [Fact]
    public void Plurals_and_punctuation_do_not_matter()
    {
        Assert.Equal(["Battery Pack"], Names("batteries!!!", P(1, "Battery Pack", "aa"), P(2, "Mouse", "usb")));
        Assert.Equal(["USB-C Hub"], Names("usb c, hubs", P(1, "USB-C Hub", "ports"), P(2, "Lamp", "led")).Take(1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("the and of")]
    [InlineData("!!! ???")]
    public void Queries_without_content_words_match_nothing(string query)
    {
        Assert.Empty(RuleBasedRecommender.Rank(query, [P(1, "Mouse", "wireless")]));
    }

    [Fact]
    public void An_empty_catalog_matches_nothing()
    {
        Assert.Empty(RuleBasedRecommender.Rank("mouse", []));
    }

    [Fact]
    public void Scores_are_between_1_and_99_and_never_claim_certainty()
    {
        var ranked = RuleBasedRecommender.Rank("wireless mouse", [P(1, "Wireless Mouse", "wireless mouse")]);

        Assert.InRange(ranked[0].Score, 1, 99);
    }

    [Fact]
    public void Ties_are_broken_by_name_then_id_so_the_order_is_stable()
    {
        var ranked = RuleBasedRecommender.Rank("gadget",
        [
            P(3, "Gadget", "x"),
            P(2, "Gadget", "x"),
            P(1, "Another Gadget", "x")
        ]);

        Assert.Equal([1, 2, 3], ranked.Select(s => s.Product.Id));
    }

    [Fact]
    public void Budget_intent_alone_returns_the_cheaper_half()
    {
        var catalog = new[]
        {
            P(1, "A", "x", price: 5m), P(2, "B", "x", price: 10m), P(3, "C", "x", price: 50m),
            P(4, "D", "x", price: 100m), P(5, "E", "x", price: 500m)
        };

        var names = Names("cheap", catalog);

        Assert.Equal(["A", "B", "C"], names.Order());
    }

    [Fact]
    public async Task The_public_entry_point_caps_results_and_labels_the_source()
    {
        var recommender = new RuleBasedRecommender(Microsoft.Extensions.Options.Options.Create(new RecommendationOptions { MaxResults = 2 }));
        var catalog = Enumerable.Range(1, 6).Select(i => P(i, $"Widget {i}", "widget")).ToList();

        var result = await recommender.RecommendAsync("widget", catalog, CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("rules", result.Source);
    }
}

public sealed class GeminiParsingTests
{
    private static string Envelope(string text) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text } } } } }
        });

    [Fact]
    public void Well_formed_answers_are_parsed()
    {
        var picks = GeminiClient.ParsePicks(Envelope("[{\"productId\":3,\"score\":88,\"reason\":\"fits\"}]"));

        var pick = Assert.Single(picks);
        Assert.Equal(new GeminiPick(3, 88, "fits"), pick);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"candidates\":[]}")]
    [InlineData("{\"candidates\":[{\"content\":{}}]}")]
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"null\"}]}}]}")]
    public void Anything_else_is_reported_as_unavailable_not_as_a_crash(string payload)
    {
        Assert.Throws<RecommendationUnavailableException>(() => GeminiClient.ParsePicks(payload));
    }
}

public sealed class GeminiRecommenderTests
{
    private static Product P(int id, string name, int stock = 5) => new()
    {
        Id = id,
        Name = name,
        Description = "d",
        Price = 10m,
        Stock = stock,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch
    };

    [Fact]
    public void Small_catalogs_are_sent_whole()
    {
        var catalog = new[] { P(1, "A"), P(2, "B") };

        Assert.Equal(catalog, GeminiRecommender.SelectCandidates("anything", catalog, max: 5));
    }

    [Fact]
    public void Large_catalogs_keep_the_lexical_matches_and_top_up_with_in_stock_items()
    {
        var catalog = new[]
        {
            P(1, "Alpha", stock: 0), P(2, "Beta"), P(3, "Gamma"), P(4, "Lamp Deluxe"), P(5, "Epsilon")
        };

        var selected = GeminiRecommender.SelectCandidates("lamp", catalog, max: 3);

        Assert.Equal(3, selected.Count);
        Assert.Equal("Lamp Deluxe", selected[0].Name);
        Assert.DoesNotContain(selected, p => p.Id == 1);
    }

    [Fact]
    public async Task An_empty_catalog_never_calls_the_model()
    {
        var recommender = new GeminiRecommender(new ThrowingClient(), new MemoryCache(new MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(new RecommendationOptions()));

        var result = await recommender.RecommendAsync("lamp", [], CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal("gemini", result.Source);
    }

    private sealed class ThrowingClient : IGeminiClient
    {
        public Task<IReadOnlyList<GeminiPick>> RankAsync(
            string query, IReadOnlyList<GeminiCandidate> candidates, int maxResults, CancellationToken ct) =>
            throw new InvalidOperationException("must not be called");
    }
}
