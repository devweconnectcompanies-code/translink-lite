using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TransLink.Lite.API.HealthChecks;

public static class HealthCheckResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            new { status = report.Status.ToString() },
            cancellationToken: context.RequestAborted);
    }
}
