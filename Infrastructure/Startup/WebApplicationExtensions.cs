using EcommerceAPI.Data;
using EcommerceAPI.Infrastructure.Middleware;
using EcommerceAPI.Infrastructure.Options;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EcommerceAPI.Infrastructure.Startup;

public static class WebApplicationExtensions
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        // Outermost first: ids and headers exist even when something below throws.
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        // TLS normally ends at a reverse proxy or container platform; opt in when Kestrel terminates it.
        if (IsEnabled(app.Configuration, "Security:HttpsRedirection"))
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        // Rewrite legacy paths before routing picks an endpoint.
        app.UseMiddleware<LegacyRouteMiddleware>();
        app.UseRouting();

        app.UseCors();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        if (IsEnabled(app.Configuration, "Swagger:Enabled"))
        {
            app.UseSwagger();
            app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Ecommerce API v1"));
        }

        app.MapControllers();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        return app;
    }

    /// <summary>A feature flag that is on only for an explicit "true"; empty or unparsable values mean off.</summary>
    private static bool IsEnabled(IConfiguration configuration, string key) =>
        bool.TryParse(configuration[key], out var enabled) && enabled;

    /// <summary>
    /// Validates configuration first, so a bad setup fails before any database work, then runs the opt-in
    /// startup data tasks (migrations, demo data, bootstrap admin).
    /// </summary>
    public static async Task InitializeAsync(this WebApplication app)
    {
        app.Services.GetRequiredService<IStartupValidator>().Validate();

        var jwt = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        if (jwt.SigningKey == DevelopmentSigningKey.Value)
        {
            app.Logger.LogWarning("Using a random development signing key; tokens stop working when the process restarts");
        }

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);
    }
}
