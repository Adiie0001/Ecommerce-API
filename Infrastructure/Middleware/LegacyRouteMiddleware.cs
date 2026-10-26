namespace EcommerceAPI.Infrastructure.Middleware;

/// <summary>
/// The first release exposed <c>/api/auth</c> and <c>/api/product</c>. Those paths keep working as aliases of the
/// versioned routes (same behaviour, same response shapes) and announce their replacement in response headers.
/// Must run before routing so the rewritten path is the one that gets matched.
/// </summary>
public sealed class LegacyRouteMiddleware(RequestDelegate next)
{
    private static readonly (PathString Legacy, PathString Current)[] Aliases =
    [
        (new PathString("/api/auth"), new PathString("/api/v1/auth")),
        (new PathString("/api/product"), new PathString("/api/v1/products"))
    ];

    public Task InvokeAsync(HttpContext context)
    {
        foreach (var (legacy, current) in Aliases)
        {
            if (context.Request.Path.StartsWithSegments(legacy, StringComparison.OrdinalIgnoreCase, out var remainder))
            {
                var successor = current.Add(remainder);
                context.Request.Path = successor;

                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["Deprecation"] = "true";
                    context.Response.Headers.Append("Link", $"<{successor}>; rel=\"successor-version\"");
                    return Task.CompletedTask;
                });

                break;
            }
        }

        return next(context);
    }
}
