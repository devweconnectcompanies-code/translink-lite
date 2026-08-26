using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TransLink.Lite.Application.Common.Exceptions;

namespace TransLink.Lite.API.ErrorHandling;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        var (status, title, detail, type) = MapException(exception);

        if (status == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled request exception. TraceId: {TraceId}, RequestPath: {RequestPath}, Category: {ExceptionCategory}",
                traceId,
                httpContext.Request.Path,
                "Unexpected");
        }
        else
        {
            _logger.LogWarning(
                "Handled request exception. TraceId: {TraceId}, RequestPath: {RequestPath}, Category: {ExceptionCategory}",
                traceId,
                httpContext.Request.Path,
                exception.GetType().Name);
        }

        httpContext.Response.StatusCode = status;

        var problemDetails = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = status,
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
        problemDetails.Extensions["traceId"] = traceId;

        await _problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
        });

        return true;
    }

    private static (int Status, string Title, string Detail, string Type) MapException(
        Exception exception) => exception switch
    {
        ConflictException conflict => (
            StatusCodes.Status409Conflict,
            "Conflict",
            conflict.Message,
            "https://tools.ietf.org/html/rfc9110#section-15.5.10"),
        NotFoundException notFound => (
            StatusCodes.Status404NotFound,
            "Not Found",
            notFound.Message,
            "https://tools.ietf.org/html/rfc9110#section-15.5.5"),
        _ => (
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred.",
            "The server encountered an unexpected condition.",
            "https://tools.ietf.org/html/rfc9110#section-15.6.1"),
    };
}
