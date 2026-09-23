using System.Net.Mime;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartCraftTask.Infrastructure;

/// <summary>
/// Renders a health report as JSON. The built-in writer answers with the single word "Healthy",
/// which gives an operator the verdict but not which check produced it or how long it took.
/// </summary>
public static class HealthReportWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = MediaTypeNames.Application.Json;

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1)
            })
        };

        // Deliberately no exception message or stack trace: this endpoint is unauthenticated, and a
        // failing connection likes to name the server it failed to reach. The reason belongs in the
        // logs, which is why the database check lets its exception escape to be logged there.
        return context.Response.WriteAsJsonAsync(payload, SerializerOptions);
    }
}
