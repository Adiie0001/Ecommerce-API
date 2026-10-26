using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EcommerceAPI.Domain;
using EcommerceAPI.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EcommerceAPI.Services;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(AppUser user);

    /// <summary>A new opaque, URL-safe refresh token with 256 bits of entropy.</summary>
    string CreateRefreshToken();

    /// <summary>Hex SHA-256 of a refresh token; only this hash is persisted.</summary>
    string HashRefreshToken(string token);
}

public sealed class JwtTokenService : ITokenService
{
    public const string RoleClaim = "role";

    private readonly JwtOptions _options;
    private readonly TimeProvider _clock;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public AccessToken CreateAccessToken(AppUser user)
    {
        var now = _clock.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = _credentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(CultureInfo.InvariantCulture),
                [JwtRegisteredClaimNames.Email] = user.Email,
                [JwtRegisteredClaimNames.Name] = user.DisplayName,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
                [RoleClaim] = user.Role.ToString()
            }
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }

    public string CreateRefreshToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
