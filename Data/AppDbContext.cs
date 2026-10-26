using EcommerceAPI.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EcommerceAPI.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no native DateTimeOffset ordering or comparison; Unix milliseconds sort and compare correctly.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UnixMillisecondsConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("Products", table =>
            {
                table.HasCheckConstraint("CK_Products_PriceCents_Positive", "\"PriceCents\" > 0");
                table.HasCheckConstraint("CK_Products_Stock_NonNegative", "\"Stock\" >= 0");
            });

            product.HasKey(p => p.Id);
            product.Property(p => p.Name).HasMaxLength(Product.MaxNameLength).IsRequired();
            product.Property(p => p.Description).HasMaxLength(Product.MaxDescriptionLength).IsRequired();
            product.Ignore(p => p.Price);
            product.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
            product.HasIndex(p => p.Name);
        });

        modelBuilder.Entity<AppUser>(user =>
        {
            user.ToTable("Users", table =>
                table.HasCheckConstraint("CK_Users_FailedLoginCount_NonNegative", "\"FailedLoginCount\" >= 0"));

            user.HasKey(u => u.Id);
            user.Property(u => u.Email).HasMaxLength(254).IsRequired();
            user.Property(u => u.DisplayName).HasMaxLength(80).IsRequired();
            user.Property(u => u.PasswordHash).HasMaxLength(100).IsRequired();
            user.Property(u => u.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
            user.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(token =>
        {
            token.ToTable("RefreshTokens");
            token.HasKey(t => t.Id);
            token.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            token.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasIndex(t => new { t.UserId, t.FamilyId });
            token.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

internal sealed class UnixMillisecondsConverter() : ValueConverter<DateTimeOffset, long>(
    value => value.ToUnixTimeMilliseconds(),
    value => DateTimeOffset.FromUnixTimeMilliseconds(value));
