using EcommerceAPI.Domain;
using EcommerceAPI.Infrastructure.Options;
using EcommerceAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EcommerceAPI.Data;

/// <summary>
/// Startup data tasks, each opt-in through configuration: apply migrations, insert demo data (Development only,
/// enforced by <see cref="SeedOptionsValidator"/>) and create a bootstrap administrator.
/// </summary>
public sealed class DatabaseInitializer(
    AppDbContext db,
    IPasswordHasher hasher,
    TimeProvider clock,
    IOptions<DatabaseOptions> database,
    IOptions<SeedOptions> seed,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (database.Value.ApplyMigrationsOnStartup)
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync(ct);

            if (db.Database.IsSqlite())
            {
                // Write-ahead logging lets readers run while the single writer commits; the mode is stored in the file.
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
            }
        }

        var options = seed.Value;
        if (options.DemoData)
        {
            await SeedDemoDataAsync(ct);
        }

        if (!string.IsNullOrWhiteSpace(options.AdminEmail))
        {
            await EnsureBootstrapAdminAsync(options, ct);
        }
    }

    private async Task SeedDemoDataAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        if (!await db.Users.AnyAsync(ct))
        {
            db.Users.AddRange(
                NewUser(DemoCredentials.AdminEmail, "Demo Admin", DemoCredentials.AdminPassword, UserRole.Admin, now),
                NewUser(DemoCredentials.CustomerEmail, "Demo Customer", DemoCredentials.CustomerPassword, UserRole.Customer, now));
        }

        if (!await db.Products.AnyAsync(ct))
        {
            db.Products.AddRange(DemoCatalog.Items.Select(item => new Product
            {
                Name = item.Name,
                Description = item.Description,
                Price = item.Price,
                Stock = item.Stock,
                CreatedAt = now,
                UpdatedAt = now
            }));
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Inserted demo data (Development only)");
        }
    }

    private async Task EnsureBootstrapAdminAsync(SeedOptions options, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Role == UserRole.Admin, ct))
        {
            return;
        }

        var email = EmailAddress.Normalize(options.AdminEmail!);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            logger.LogWarning("Bootstrap admin email is already registered as a customer; leaving it unchanged");
            return;
        }

        db.Users.Add(NewUser(email, options.AdminDisplayName, options.AdminPassword!, UserRole.Admin, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created the bootstrap administrator");
    }

    private AppUser NewUser(string email, string displayName, string password, UserRole role, DateTimeOffset now) => new()
    {
        Email = EmailAddress.Normalize(email),
        DisplayName = displayName,
        PasswordHash = hasher.Hash(password),
        Role = role,
        CreatedAt = now
    };
}
