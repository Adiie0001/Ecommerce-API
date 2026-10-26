using EcommerceAPI.Contracts;
using EcommerceAPI.Data;
using EcommerceAPI.Domain;
using EcommerceAPI.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EcommerceAPI.Services;

public sealed class AuthService(
    AppDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    TimeProvider clock,
    IOptions<JwtOptions> jwtOptions,
    IOptions<AuthOptions> authOptions,
    ILogger<AuthService> logger)
{
    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly AuthOptions _auth = authOptions.Value;

    /// <summary>Creates a customer account. Roles are never taken from the request.</summary>
    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = EmailAddress.Normalize(request.Email);
        var displayName = request.DisplayName.Trim();
        var errors = new Dictionary<string, string[]>();

        if (!EmailAddress.IsValid(email))
        {
            errors["email"] = ["Email address is not valid."];
        }

        if (displayName.Length == 0)
        {
            errors["displayName"] = ["Display name is required."];
        }

        var passwordErrors = PasswordPolicy.Validate(request.Password, email);
        if (passwordErrors.Count > 0)
        {
            errors["password"] = passwordErrors.Select(e => $"Password {e}.").ToArray();
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new ConflictException("An account with this email address already exists.");
        }

        var user = new AppUser
        {
            Email = email,
            DisplayName = displayName,
            PasswordHash = hasher.Hash(request.Password),
            Role = UserRole.Customer,
            CreatedAt = clock.GetUtcNow()
        };

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two registrations racing on the unique email index: the loser gets the same answer as the pre-check.
            db.ChangeTracker.Clear();
            if (await db.Users.AnyAsync(u => u.Email == email, ct))
            {
                throw new ConflictException("An account with this email address already exists.");
            }

            throw;
        }

        return user.ToDto();
    }

    /// <summary>
    /// Verifies credentials. Returns null for every kind of failure (unknown account, wrong password, locked out) so
    /// the response never reveals which one it was.
    /// </summary>
    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = EmailAddress.Normalize(request.Email);
        var now = clock.GetUtcNow();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);

        // Always verify, even for unknown accounts, so timing does not reveal whether the email exists.
        var passwordMatches = hasher.Verify(request.Password, user?.PasswordHash);

        if (user is null)
        {
            logger.LogInformation("Sign-in rejected: no such account");
            return null;
        }

        if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
        {
            logger.LogWarning("Sign-in rejected: user {UserId} is locked out until {LockoutEnd:O}", user.Id, lockoutEnd);
            return null;
        }

        if (!passwordMatches)
        {
            await RegisterFailureAsync(user.Id, now, ct);
            return null;
        }

        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FailedLoginCount, 0)
                .SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null), ct);

        // Housekeeping: expired tokens of this user are no longer useful.
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.ExpiresAt < now).ExecuteDeleteAsync(ct);

        logger.LogInformation("User {UserId} signed in", user.Id);
        return await IssueTokensAsync(user, Guid.NewGuid(), now, tokens.CreateRefreshToken(), ct);
    }

    /// <summary>
    /// Exchanges a refresh token for a new pair. Each token works once; presenting one that was already used revokes
    /// the whole family, because it means a copy of the token leaked.
    /// </summary>
    public async Task<AuthResponse?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = tokens.HashRefreshToken(refreshToken);
        var stored = await db.RefreshTokens.AsNoTracking()
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored?.User is null)
        {
            return null;
        }

        if (stored.RevokedAt is not null)
        {
            logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking family {FamilyId}", stored.UserId, stored.FamilyId);
            await RevokeFamilyAsync(stored.FamilyId, now, ct);
            return null;
        }

        if (stored.ExpiresAt <= now)
        {
            return null;
        }

        if (stored.User.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
        {
            return null;
        }

        var replacement = tokens.CreateRefreshToken();
        var replacementHash = tokens.HashRefreshToken(replacement);

        // Compare-and-set: only one concurrent caller can flip RevokedAt from null.
        var revoked = await db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, (DateTimeOffset?)now)
                .SetProperty(t => t.ReplacedByTokenHash, replacementHash), ct);

        if (revoked == 0)
        {
            logger.LogWarning("Concurrent use of refresh token detected for user {UserId}; revoking family {FamilyId}", stored.UserId, stored.FamilyId);
            await RevokeFamilyAsync(stored.FamilyId, now, ct);
            return null;
        }

        return await IssueTokensAsync(stored.User, stored.FamilyId, now, replacement, ct);
    }

    /// <summary>Revokes the token's whole family. Unknown tokens are ignored so logout is always safe to call.</summary>
    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var hash = tokens.HashRefreshToken(refreshToken);
        var familyId = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(ct);

        if (familyId is { } id)
        {
            await RevokeFamilyAsync(id, clock.GetUtcNow(), ct);
        }
    }

    public async Task<UserDto?> GetUserAsync(int id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
        return user?.ToDto();
    }

    private async Task RegisterFailureAsync(int userId, DateTimeOffset now, CancellationToken ct)
    {
        // Atomic increment so parallel guesses cannot slip under the counter.
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1), ct);

        var failures = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FailedLoginCount)
            .SingleAsync(ct);

        if (failures >= _auth.MaxFailedAttempts)
        {
            var until = now.AddMinutes(_auth.LockoutMinutes);
            await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.LockoutEnd, (DateTimeOffset?)until)
                    .SetProperty(u => u.FailedLoginCount, 0), ct);

            logger.LogWarning("User {UserId} locked out until {LockoutEnd:O} after repeated failures", userId, until);
        }
    }

    private Task<int> RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, (DateTimeOffset?)now), ct);

    private async Task<AuthResponse> IssueTokensAsync(
        AppUser user, Guid familyId, DateTimeOffset now, string refreshToken, CancellationToken ct)
    {
        var access = tokens.CreateAccessToken(user);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokens.HashRefreshToken(refreshToken),
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_jwt.RefreshTokenDays)
        });
        await db.SaveChangesAsync(ct);

        var expiresIn = (int)Math.Max(1, (access.ExpiresAt - now).TotalSeconds);
        return new AuthResponse(access.Value, "Bearer", expiresIn, refreshToken, user.ToDto());
    }
}
