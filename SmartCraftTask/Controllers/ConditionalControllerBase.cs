using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace SmartCraftTask.Controllers;

/// <summary>
/// Shared handling of conditional requests. Mutating actions require an <c>If-Match</c> carrying
/// the ETag from a prior read, so a client cannot overwrite a version it never saw.
/// </summary>
public abstract class ConditionalControllerBase : ControllerBase
{
    /// <summary>Publishes the resource's current version so the caller can send it back later.</summary>
    protected void SetETag(byte[] rowVersion) =>
        Response.Headers.ETag = FormatETag(rowVersion);

    protected static string FormatETag(byte[] rowVersion) =>
        $"\"{Convert.ToBase64String(rowVersion)}\"";

    /// <summary>
    /// Reads the version the caller expects. Returns false and fills <paramref name="failure"/>
    /// when the header is missing (428) or unreadable (400).
    /// </summary>
    protected bool TryGetExpectedVersion(out byte[] rowVersion, out ActionResult? failure)
    {
        rowVersion = [];
        failure = null;

        var header = Request.Headers[HeaderNames.IfMatch].ToString();

        if (string.IsNullOrWhiteSpace(header))
        {
            failure = Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "If-Match required",
                detail: "Read the resource first and send its ETag back as If-Match, so a concurrent "
                    + "change cannot be overwritten unnoticed.");

            return false;
        }

        // A wildcard means "whatever version is current", which is exactly the blind overwrite
        // this endpoint exists to prevent.
        if (header == "*")
        {
            failure = Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "If-Match must name a version",
                detail: "A wildcard If-Match would overwrite whatever is current. Send the ETag from a read.");

            return false;
        }

        var candidate = header.Trim().Trim('"');

        if (!TryDecode(candidate, out rowVersion))
        {
            failure = Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Malformed If-Match",
                detail: $"'{header}' is not an ETag this service issued.");

            return false;
        }

        return true;
    }

    /// <summary>The write lost a race, or was built from a version that is no longer current.</summary>
    protected ActionResult PreconditionFailed(string detail) =>
        Problem(
            statusCode: StatusCodes.Status412PreconditionFailed,
            title: "Version mismatch",
            detail: detail);

    private static bool TryDecode(string candidate, out byte[] rowVersion)
    {
        rowVersion = [];

        if (candidate.Length == 0)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[16];

        if (!Convert.TryFromBase64String(candidate, buffer, out var written) || written == 0)
        {
            return false;
        }

        rowVersion = buffer[..written].ToArray();

        return true;
    }
}
