using EcommerceAPI.Data;
using EcommerceAPI.Domain;
using EcommerceAPI.Infrastructure.Options;
using EcommerceAPI.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EcommerceAPI.Tests;

/// <summary>Runs against the real migrations on SQLite, so these tests describe what production data can and cannot hold.</summary>
public sealed class DatabaseTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<AppDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        await using var db = new AppDbContext(_options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private AppDbContext NewContext() => new(_options);

    private static Product NewProduct(string name = "Item", decimal price = 10m, int stock = 1) => new()
    {
        Name = name,
        Description = "d",
        Price = price,
        Stock = stock,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static AppUser NewUser(string email, UserRole role = UserRole.Customer) => new()
    {
        Email = email,
        DisplayName = "Test",
        PasswordHash = "hash",
        Role = role,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    [Fact]
    public async Task The_migrations_describe_exactly_the_current_model()
    {
        await using var db = NewContext();

        Assert.False(db.Database.HasPendingModelChanges(),
            "The model changed without a migration. Run: dotnet ef migrations add <Name> -o Data/Migrations");
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Applying_migrations_again_is_a_no_op()
    {
        await using var db = NewContext();

        await db.Database.MigrateAsync();

        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Prices_are_stored_as_whole_cents()
    {
        await using (var db = NewContext())
        {
            db.Products.Add(NewProduct(price: 12.34m));
            await db.SaveChangesAsync();
        }

        Assert.Equal(1234L, await ScalarAsync<long>("SELECT PriceCents FROM Products"));
        await using var read = NewContext();
        Assert.Equal(12.34m, (await read.Products.SingleAsync()).Price);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task The_database_itself_refuses_non_positive_prices(long cents)
    {
        await using var db = NewContext();
        db.Products.Add(NewProduct());
        await db.SaveChangesAsync();

        var violation = await Record.ExceptionAsync(() =>
            db.Database.ExecuteSqlAsync($"UPDATE Products SET PriceCents = {cents}"));

        Assert.IsType<SqliteException>(violation);
        Assert.Contains("CHECK", violation!.Message);
    }

    [Fact]
    public async Task The_database_itself_refuses_negative_stock()
    {
        await using var db = NewContext();
        db.Products.Add(NewProduct(stock: -1));

        var violation = await Record.ExceptionAsync(() => db.SaveChangesAsync());

        Assert.IsType<DbUpdateException>(violation);
        Assert.Contains("CHECK", violation!.InnerException!.Message);
    }

    [Fact]
    public async Task Email_addresses_are_unique()
    {
        await using var db = NewContext();
        db.Users.Add(NewUser("same@shop.test"));
        await db.SaveChangesAsync();

        await using var second = NewContext();
        second.Users.Add(NewUser("same@shop.test"));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Roles_are_stored_as_readable_names()
    {
        await using (var db = NewContext())
        {
            db.Users.Add(NewUser("admin@shop.test", UserRole.Admin));
            await db.SaveChangesAsync();
        }

        Assert.Equal("Admin", await ScalarAsync<string>("SELECT Role FROM Users"));
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_refresh_tokens()
    {
        await using var db = NewContext();
        var user = NewUser("owner@shop.test");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = new string('a', 64),
            FamilyId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        db.Users.Remove(user);
        await db.SaveChangesAsync();

        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM RefreshTokens"));
    }

    [Fact]
    public async Task Refresh_token_hashes_are_unique()
    {
        await using var db = NewContext();
        var user = NewUser("dup@shop.test");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        RefreshToken Token() => new()
        {
            UserId = user.Id,
            TokenHash = new string('b', 64),
            FamilyId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };

        db.RefreshTokens.Add(Token());
        await db.SaveChangesAsync();
        db.RefreshTokens.Add(Token());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_concurrent_edit_is_detected_through_the_concurrency_stamp()
    {
        int id;
        await using (var seed = NewContext())
        {
            var product = NewProduct();
            seed.Products.Add(product);
            await seed.SaveChangesAsync();
            id = product.Id;
        }

        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.Products.SingleAsync(p => p.Id == id);
        var b = await second.Products.SingleAsync(p => p.Id == id);

        a.Name = "first";
        a.ConcurrencyStamp = Guid.NewGuid();
        await first.SaveChangesAsync();

        b.Name = "second";
        b.ConcurrencyStamp = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Timestamps_round_trip_with_millisecond_precision_and_sort_correctly()
    {
        var early = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var late = early.AddMinutes(5);
        await using (var db = NewContext())
        {
            var newer = NewProduct("newer");
            newer.CreatedAt = late;
            var older = NewProduct("older");
            older.CreatedAt = early;
            db.Products.AddRange(newer, older);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var ordered = await read.Products.OrderBy(p => p.CreatedAt).ToListAsync();

        Assert.Equal(["older", "newer"], ordered.Select(p => p.Name));
        Assert.Equal(early, ordered[0].CreatedAt);
    }

    [Fact]
    public async Task Demo_data_is_inserted_once_even_if_initialisation_runs_twice()
    {
        var initializer = NewInitializer(new SeedOptions { DemoData = true });

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        await using var db = NewContext();
        Assert.Equal(12, await db.Products.CountAsync());
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(1, await db.Users.CountAsync(u => u.Role == UserRole.Admin));
    }

    [Fact]
    public async Task A_bootstrap_admin_is_not_created_when_an_admin_already_exists()
    {
        await using (var db = NewContext())
        {
            db.Users.Add(NewUser("first-admin@shop.test", UserRole.Admin));
            await db.SaveChangesAsync();
        }

        await NewInitializer(new SeedOptions { AdminEmail = "second@shop.test", AdminPassword = "Sup3r-Secret-Pass" }).InitializeAsync();

        await using var read = NewContext();
        Assert.Equal(1, await read.Users.CountAsync(u => u.Role == UserRole.Admin));
        Assert.False(await read.Users.AnyAsync(u => u.Email == "second@shop.test"));
    }

    [Fact]
    public async Task A_bootstrap_admin_never_takes_over_an_existing_customer_account()
    {
        await using (var db = NewContext())
        {
            db.Users.Add(NewUser("customer@shop.test"));
            await db.SaveChangesAsync();
        }

        await NewInitializer(new SeedOptions { AdminEmail = "Customer@Shop.test", AdminPassword = "Sup3r-Secret-Pass" }).InitializeAsync();

        await using var read = NewContext();
        Assert.Equal(UserRole.Customer, (await read.Users.SingleAsync()).Role);
    }

    private DatabaseInitializer NewInitializer(SeedOptions seed) => new(
        NewContext(),
        new BCryptPasswordHasher(Microsoft.Extensions.Options.Options.Create(new AuthOptions { BcryptWorkFactor = 4 })),
        TimeProvider.System,
        Microsoft.Extensions.Options.Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }),
        Microsoft.Extensions.Options.Options.Create(seed),
        NullLogger<DatabaseInitializer>.Instance);
}
