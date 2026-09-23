using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace SmartCraftTask.Infrastructure;

/// <summary>
/// Translates the two database failures that are not server faults into the status codes that say
/// so. Anything else is left alone and becomes a 500, because an unexpected database error is a
/// bug until proven otherwise.
/// </summary>
public sealed class DatabaseExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DatabaseExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Cannot insert duplicate key row in object ... with unique index ...</summary>
    private const int DuplicateKeyInUniqueIndex = 2601;

    /// <summary>Violation of UNIQUE KEY constraint ...</summary>
    private const int UniqueConstraintViolation = 2627;

    private const int RetryAfterSeconds = 5;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // A write wraps its failure in DbUpdateException; a read throws SqlException directly.
        var sqlException = exception switch
        {
            SqlException direct => direct,
            DbUpdateException { InnerException: SqlException inner } => inner,
            _ => null
        };

        if (sqlException is null)
        {
            return false;
        }

        if (sqlException.Number is DuplicateKeyInUniqueIndex or UniqueConstraintViolation)
        {
            return await WriteAsync(
                httpContext,
                exception,
                StatusCodes.Status409Conflict,
                "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                "Duplicate value",
                // The controllers check for a duplicate before inserting, so reaching here means
                // another request won the race between that check and the insert. Same conflict,
                // same answer — a 500 would tell the caller to report a bug instead of retrying.
                "Another request created the same value first. Re-read and retry if you still need to.");
        }

        if (sqlException.IsTransient)
        {
            // 503, not 500: the request was fine and may well succeed on a retry. Matching the
            // readiness probe, which is reporting the same outage from the other direction.
            httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString();

            return await WriteAsync(
                httpContext,
                exception,
                StatusCodes.Status503ServiceUnavailable,
                "https://tools.ietf.org/html/rfc9110#section-15.6.4",
                "Database unavailable",
                "The database could not be reached. This is temporary; retry shortly.");
        }

        return false;
    }

    private async ValueTask<bool> WriteAsync(
        HttpContext httpContext,
        Exception exception,
        int statusCode,
        string type,
        string title,
        string detail)
    {
        // Logged in full here, because the response deliberately carries none of it: a SQL error
        // message names tables, indexes and servers, and this response is going to a client.
        logger.LogError(exception, "Database failure answered with {StatusCode}.", statusCode);

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Type = type,
                Title = title,
                Detail = detail,
                Status = statusCode
            }
        });
    }
}
