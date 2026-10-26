using EcommerceAPI.Contracts;

namespace EcommerceAPI.Tests.TestSupport;

/// <summary>A factory whose catalog is created once, through the public admin API, with values the tests can reason about.</summary>
public class SeededCatalogFixture : IAsyncLifetime
{
    public static readonly (string Name, string Description, decimal Price, int Stock)[] Catalog =
    [
        ("Wireless Mouse", "Ergonomic wireless mouse with USB receiver", 25.00m, 10),
        ("Mechanical Keyboard", "Tactile keyboard 100% layout", 80.50m, 0),
        ("Gaming Monitor", "27 inch 144Hz gaming monitor", 299.99m, 3),
        ("USB-C Cable", "1m braided cable, 100% copper", 9.99m, 100),
        ("Desk Lamp", "LED lamp with dimmer", 19.99m, 7),
        ("Laptop Stand", "Aluminium stand, foldable", 34.00m, 2),
        ("Webcam", "1080p webcam with microphone", 45.00m, 0)
    ];

    public ApiFactory Factory { get; private set; } = new();

    public Dictionary<string, ProductDto> Products { get; } = new();

    protected virtual Dictionary<string, string?>? Settings => null;

    public async Task InitializeAsync()
    {
        Factory = Settings is null ? new ApiFactory() : ApiFactory.With(Settings);

        using var admin = await Factory.AdminClientAsync();
        foreach (var (name, description, price, stock) in Catalog)
        {
            Products[name] = await admin.CreateProductAsync(name, description, price, stock);

            // Distinct timestamps make created/updated ordering deterministic in tests.
            Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        }
    }

    public Task DisposeAsync()
    {
        Factory.Dispose();
        return Task.CompletedTask;
    }
}

public sealed class GeminiCatalogFixture : SeededCatalogFixture
{
    protected override Dictionary<string, string?>? Settings => ApiFactory.WithGemini();
}

public sealed class GeminiLimitedCatalogFixture : SeededCatalogFixture
{
    protected override Dictionary<string, string?>? Settings
    {
        get
        {
            var settings = ApiFactory.WithGemini();
            settings["Recommendations:Gemini:MaxCandidates"] = "3";
            return settings;
        }
    }
}

public sealed class GeminiBreakerCatalogFixture : SeededCatalogFixture
{
    protected override Dictionary<string, string?>? Settings
    {
        get
        {
            var settings = ApiFactory.WithGemini();
            settings["Recommendations:Gemini:CircuitBreakerMinimumRequests"] = "3";
            settings["Recommendations:Gemini:CircuitBreakerSeconds"] = "60";
            return settings;
        }
    }
}
