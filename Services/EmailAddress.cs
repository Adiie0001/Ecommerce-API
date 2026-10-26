using System.ComponentModel.DataAnnotations;

namespace EcommerceAPI.Services;

public static class EmailAddress
{
    private static readonly EmailAddressAttribute Validator = new();

    /// <summary>Canonical stored form: trimmed and lower-cased.</summary>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public static bool IsValid(string email)
    {
        var trimmed = email.Trim();
        return trimmed.Length is > 0 and <= 254 && Validator.IsValid(trimmed);
    }
}
