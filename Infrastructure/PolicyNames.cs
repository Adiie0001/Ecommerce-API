namespace EcommerceAPI.Infrastructure;

public static class Policies
{
    /// <summary>Requires a valid access token whose role claim is Admin.</summary>
    public const string AdminOnly = "AdminOnly";
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Recommend = "recommend";
}
