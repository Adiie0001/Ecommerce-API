namespace EcommerceAPI.Domain;

/// <summary>
/// A server-side record of an issued refresh token. Only a SHA-256 hash of the opaque token is stored.
/// Tokens rotate on every use and belong to a family; presenting an already-rotated token revokes the family.
/// </summary>
public sealed class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public required string TokenHash { get; set; }
    public Guid FamilyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
}
