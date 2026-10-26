namespace EcommerceAPI.Domain;

public enum UserRole
{
    Customer = 0,
    Admin = 1
}

public sealed class AppUser
{
    public int Id { get; set; }

    /// <summary>Stored trimmed and lower-cased; unique.</summary>
    public required string Email { get; set; }

    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Customer;

    /// <summary>Consecutive failed sign-ins since the last success or lockout.</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>While in the future, sign-in is refused regardless of the password.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
