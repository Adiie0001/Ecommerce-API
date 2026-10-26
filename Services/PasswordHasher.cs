using EcommerceAPI.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace EcommerceAPI.Services;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Checks a password. When <paramref name="hash"/> is null or empty (unknown account) a dummy hash is verified
    /// instead, so the call takes about as long either way and callers cannot be timed to enumerate accounts.
    /// </summary>
    bool Verify(string password, string? hash);
}

/// <summary>BCrypt with the "enhanced" SHA-384 pre-hash, which removes bcrypt's 72-byte password limit.</summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private readonly int _workFactor;
    private readonly Lazy<string> _dummyHash;

    public BCryptPasswordHasher(IOptions<AuthOptions> options)
    {
        _workFactor = options.Value.BcryptWorkFactor;
        _dummyHash = new Lazy<string>(() => Hash(Guid.NewGuid().ToString("N")));
    }

    public string Hash(string password) => BCrypt.Net.BCrypt.EnhancedHashPassword(password, _workFactor);

    public bool Verify(string password, string? hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            BCrypt.Net.BCrypt.EnhancedVerify(password, _dummyHash.Value);
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(password, hash);
        }
        catch (Exception ex) when (ex is BCrypt.Net.SaltParseException or ArgumentException or FormatException)
        {
            // A malformed stored hash must never authenticate anyone.
            return false;
        }
    }
}
