using System.ComponentModel.DataAnnotations;

namespace EcommerceAPI.Contracts;

/// <summary>Public sign-up. Always creates a customer; roles cannot be chosen by the caller.</summary>
public sealed class RegisterRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>10 to 128 characters with at least one lower-case letter, one upper-case letter and one digit.</summary>
    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;
}

public sealed class RefreshRequest
{
    [Required, StringLength(200)]
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed record UserDto(int Id, string Email, string DisplayName, string Role);

/// <param name="AccessToken">Short-lived JWT for the Authorization header.</param>
/// <param name="TokenType">Always "Bearer".</param>
/// <param name="ExpiresIn">Access token lifetime in seconds.</param>
/// <param name="RefreshToken">Single-use opaque token; exchange it at the refresh endpoint for a new pair.</param>
/// <param name="User">The authenticated user.</param>
public sealed record AuthResponse(string AccessToken, string TokenType, int ExpiresIn, string RefreshToken, UserDto User);
