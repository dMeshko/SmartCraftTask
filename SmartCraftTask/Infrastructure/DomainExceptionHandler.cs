using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SmartCraftTask.Models;

namespace SmartCraftTask.Infrastructure;

/// <summary>
/// Turns a broken business rule into a 409 ProblemDetails, so the aggregate can enforce its
/// invariants without every controller repeating the translation.
/// </summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = domainException,
            ProblemDetails = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                Title = domainException.Title,
                Detail = domainException.Message,
                Status = StatusCodes.Status409Conflict
            }
        });
    }
}
