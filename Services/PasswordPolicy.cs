namespace EcommerceAPI.Services;

/// <summary>
/// Length and composition rules applied when an account is created. Each message completes the sentence
/// "Password ..." so callers can prefix their own subject.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password123", "password1234", "password12345", "passw0rd123", "1234567890", "12345678910",
        "qwertyuiop", "qwerty12345", "letmein1234", "welcome1234", "administrator", "iloveyou123"
    };

    public static IReadOnlyList<string> Validate(string password, string? email = null)
    {
        var errors = new List<string>();

        if (password.Length < MinLength)
        {
            errors.Add($"must be at least {MinLength} characters long");
        }

        if (password.Length > MaxLength)
        {
            errors.Add($"must be at most {MaxLength} characters long");
        }

        if (!password.Any(char.IsLower))
        {
            errors.Add("must contain a lower-case letter");
        }

        if (!password.Any(char.IsUpper))
        {
            errors.Add("must contain an upper-case letter");
        }

        if (!password.Any(char.IsDigit))
        {
            errors.Add("must contain a digit");
        }

        if (Common.Contains(password))
        {
            errors.Add("is too common");
        }

        var localPart = email?.Split('@')[0];
        if (localPart is { Length: >= 4 } &&
            password.Contains(localPart, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("must not contain the name part of your email address");
        }

        return errors;
    }
}
