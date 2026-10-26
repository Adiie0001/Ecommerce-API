using EcommerceAPI.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceAPI.Infrastructure;

/// <summary>
/// Turns exceptions into RFC 9457 problem details. Known domain failures keep their message and status; everything
/// else becomes a generic 500 so internals (stack traces, SQL, file paths) never reach a client in production.
/// </summary>
public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer.
            return true;
        }

        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            ValidationFailedException => (StatusCodes.Status400BadRequest, "Validation failed"),
            BadHttpRequestException bad => (bad.StatusCode, "Bad request"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error")
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception while processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request rejected with {Status}: {Message}", status, exception.Message);
        }

        ProblemDetails problem = exception is ValidationFailedException validation
            ? new ValidationProblemDetails(validation.Errors) { Status = status, Title = title }
            : new ProblemDetails { Status = status, Title = title };

        problem.Detail = status >= 500
            ? (environment.IsDevelopment() ? exception.Message : "An unexpected error occurred.")
            : exception.Message;

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
