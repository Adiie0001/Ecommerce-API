namespace EcommerceAPI.Domain;

/// <summary>A catalog item. Money is stored as integer minor units (cents) so it sorts, filters and sums exactly.</summary>
public sealed class Product
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;

    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }

    /// <summary>Price in minor units (cents). Always greater than zero.</summary>
    public long PriceCents { get; set; }

    public int Stock { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Optimistic-concurrency token. Replaced on every update; clients echo it back as <c>version</c>.</summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    /// <summary>Price in major units, derived from <see cref="PriceCents"/> (not persisted).</summary>
    public decimal Price
    {
        get => PriceCents / 100m;
        set => PriceCents = Money.ToCents(value);
    }
}

public static class Money
{
    /// <summary>Converts a major-unit amount to cents, rounding half away from zero.</summary>
    public static long ToCents(decimal amount) =>
        checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    /// <summary>True when the amount has no more than two decimal places.</summary>
    public static bool HasAtMostTwoDecimals(decimal amount) => decimal.Round(amount, 2) == amount;
}
