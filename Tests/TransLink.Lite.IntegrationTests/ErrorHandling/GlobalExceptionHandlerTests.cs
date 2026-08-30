using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TransLink.Lite.API.ErrorHandling;
using TransLink.Lite.IntegrationTests.Infrastructure;

namespace TransLink.Lite.IntegrationTests.ErrorHandling;

[Collection(IntegrationTestCollection.Name)]
public sealed class GlobalExceptionHandlerTests
{
    private readonly PostgreSqlApiFixture _fixture;

    public GlobalExceptionHandlerTests(PostgreSqlApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UnexpectedException_ReturnsSafeInternalServerProblemDetails()
    {
        var problemDetailsService =
            _fixture.Factory.Services.GetRequiredService<IProblemDetailsService>();
        var handler = new GlobalExceptionHandler(
            problemDetailsService,
            NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext
        {
            RequestServices = _fixture.Factory.Services,
        };
        context.Request.Path = "/test-only-handler-verification";
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("Internal SQL implementation detail"),
            CancellationToken.None);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        var root = json.RootElement;
        var body = root.ToString();

        Assert.True(handled);
        Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.True(root.TryGetProperty("traceId", out _));
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Internal SQL", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }
}
