using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OddOddities.Application.Ports;

namespace OddOddities.Infrastructure.Middleware;

/// <summary>
/// Global exception middleware that captures any unhandled exception in the HTTP pipeline.
/// Implements RF-11: converts exceptions to structured logs, keeps stack trace local only,
/// and returns a sanitized 500 response without exposing internal details.
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly ILogCorrelationPort _logCorrelation;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        ILogCorrelationPort logCorrelation)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logCorrelation = logCorrelation ?? throw new ArgumentNullException(nameof(logCorrelation));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var executionId = Guid.NewGuid().ToString("N");
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

            using (_logCorrelation.PushCorrelation(executionId, "UnhandledException", "Failed", 0))
            {
                _logger.LogError(ex,
                    "Unhandled exception: {ExceptionType} | TraceId={TraceId}",
                    ex.GetType().Name,
                    traceId);
            }

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";

                var response = new
                {
                    error = "An internal error occurred",
                    traceId
                };

                var json = JsonSerializer.Serialize(response, JsonOptions);
                await context.Response.WriteAsync(json);
            }
            else
            {
                _logger.LogWarning(
                    "Response already started, cannot write error body. TraceId={TraceId}",
                    traceId);
            }
        }
    }
}
