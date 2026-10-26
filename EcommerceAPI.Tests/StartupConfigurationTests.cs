using System.Net;
using System.Net.Http.Json;
using EcommerceAPI.Contracts;
using EcommerceAPI.Data;
using EcommerceAPI.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EcommerceAPI.Tests;

/// <summary>The application must refuse to start with unsafe or contradictory configuration, and must say why.</summary>
public sealed class StartupConfigurationTests
{
    private static async Task<string> StartupFailureAsync(Dictionary<string, string?> overrides, string environment = "Production")
    {
        using var factory = ApiFactory.With(overrides, environment);
        var failure = await Record.ExceptionAsync(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/health/live");
        });

        Assert.NotNull(failure);
        var messages = new List<string>();
        for (var current = failure; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    [Fact]
    public async Task A_missing_signing_key_stops_startup_outside_development()
    {
        var message = await StartupFailureAsync(new() { ["Jwt:SigningKey"] = null });

        Assert.Contains("Jwt:SigningKey", message);
    }

    [Fact]
    public async Task A_short_signing_key_stops_startup()
    {
        var message = await StartupFailureAsync(new() { ["Jwt:SigningKey"] = "too-short" });

        Assert.Contains("at least 32 bytes", message);
    }

    [Fact]
    public async Task The_old_placeholder_key_is_rejected_even_though_it_is_long_enough()
    {
        var message = await StartupFailureAsync(new() { ["Jwt:SigningKey"] = "YourSecretKeyHereMustBeVeryLongForSecurity" });

        Assert.Contains("placeholder", message);
    }

    [Fact]
    public async Task Demo_data_is_refused_outside_development()
    {
        var message = await StartupFailureAsync(new() { ["Seed:DemoData"] = "true" });

        Assert.Contains("Seed:DemoData", message);
    }

    [Fact]
    public async Task A_published_demo_password_cannot_be_used_for_the_bootstrap_admin_in_production()
    {
        var message = await StartupFailureAsync(new()
        {
            ["Seed:AdminEmail"] = "owner@shop.test",
            ["Seed:AdminPassword"] = DemoCredentials.AdminPassword
        });

        Assert.Contains("demo password", message);
    }

    [Fact]
    public async Task A_weak_bootstrap_admin_password_is_rejected()
    {
        var message = await StartupFailureAsync(new() { ["Seed:AdminPassword"] = "weak" });

        Assert.Contains("Seed:AdminPassword", message);
    }

    [Fact]
    public async Task The_bootstrap_admin_email_and_password_must_come_together()
    {
        var message = await StartupFailureAsync(new() { ["Seed:AdminPassword"] = null });

        Assert.Contains("provided together", message);
    }

    [Fact]
    public async Task Enabling_gemini_without_a_key_stops_startup()
    {
        var message = await StartupFailureAsync(new() { ["Recommendations:Gemini:Enabled"] = "true" });

        Assert.Contains("ApiKey", message);
    }

    [Fact]
    public async Task A_model_name_that_could_alter_the_request_path_is_rejected()
    {
        var settings = ApiFactory.WithGemini();
        settings["Recommendations:Gemini:Model"] = "gemini/../../admin";

        var message = await StartupFailureAsync(settings);

        Assert.Contains("Model", message);
    }

    [Fact]
    public async Task Out_of_range_numbers_are_rejected()
    {
        var message = await StartupFailureAsync(new() { ["Auth:BcryptWorkFactor"] = "3" });

        Assert.Contains("BcryptWorkFactor", message);
    }

    [Fact]
    public async Task Development_needs_no_secrets_seeds_demo_data_and_enables_swagger()
    {
        using var factory = ApiFactory.With(new()
        {
            ["Jwt:SigningKey"] = null,
            ["Seed:AdminEmail"] = null,
            ["Seed:AdminPassword"] = null
        }, "Development");
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = DemoCredentials.AdminEmail, password = DemoCredentials.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var admin = await login.ReadAsync<AuthResponse>();
        Assert.Equal("Admin", admin.User.Role);

        var page = await (await client.GetAsync("/api/v1/products")).ReadAsync<PagedResult<ProductDto>>();
        Assert.Equal(12, page.TotalCount);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);

        var customerLogin = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = DemoCredentials.CustomerEmail, password = DemoCredentials.CustomerPassword });
        Assert.Equal(HttpStatusCode.OK, customerLogin.StatusCode);
    }

    [Fact]
    public async Task Production_with_real_secrets_starts_empty_with_only_the_bootstrap_admin()
    {
        using var factory = ApiFactory.With(new() { ["Swagger:Enabled"] = "false" }, "Production");
        var client = factory.CreateClient();

        var page = await (await client.GetAsync("/api/v1/products")).ReadAsync<PagedResult<ProductDto>>();
        Assert.Equal(0, page.TotalCount);

        var demoLogin = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = DemoCredentials.AdminEmail, password = DemoCredentials.AdminPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, demoLogin.StatusCode);

        var admin = await factory.LoginAdminAsync();
        Assert.Equal("Admin", admin.User.Role);
    }

    [Fact]
    public async Task The_bootstrap_admin_is_created_once_and_never_duplicated()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        for (var run = 0; run < 2; run++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        }

        await using var check = factory.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Role == EcommerceAPI.Domain.UserRole.Admin));
    }
}
