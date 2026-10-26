namespace EcommerceAPI.Domain;

/// <summary>Base type for failures that map to a specific HTTP status instead of a generic 500.</summary>
public abstract class AppException(string message) : Exception(message);

public sealed class NotFoundException(string message) : AppException(message);

public sealed class ConflictException(string message) : AppException(message);

public sealed class ValidationFailedException : AppException
{
    public ValidationFailedException(string field, params string[] messages)
        : this(new Dictionary<string, string[]> { [field] = messages })
    {
    }

    public ValidationFailedException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public IDictionary<string, string[]> Errors { get; }
}

/// <summary>The AI recommender could not produce a usable answer; callers fall back to the rule-based engine.</summary>
public sealed class RecommendationUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
