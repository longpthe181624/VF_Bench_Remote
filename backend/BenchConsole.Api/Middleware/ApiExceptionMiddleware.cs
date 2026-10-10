using System.Text.Json;
using BenchConsole.Api.Services.Common;
using BenchConsole.Api.Services.Testing;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace BenchConsole.Api.Middleware;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context, ProblemDetailsFactory problems)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client đã ngắt kết nối; không gửi thêm response.
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var cacheControl = context.Response.Headers.CacheControl;
            context.Response.Clear();
            if (cacheControl.Count > 0)
                context.Response.Headers.CacheControl = cacheControl;
            var expected = ex switch
            {
                ApiException api => (api.StatusCode, api.Error),
                JobFlowException job => (job.Status, (string?)job.Message),
                _ => ((int StatusCode, string? Error)?)null,
            };
            if (expected is { } failure)
            {
                context.Response.StatusCode = failure.StatusCode;
                if (failure.Error is not null)
                    await context.Response.WriteAsJsonAsync(new { error = failure.Error }, context.RequestAborted);
                else if (failure.StatusCode is 400 or 404)
                    await context.Response.WriteAsJsonAsync(problems.CreateProblemDetails(context, failure.StatusCode),
                        options: (JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
                return;
            }

            log.LogError(ex, "Unhandled API error {TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Đã xảy ra lỗi hệ thống. Vui lòng thử lại.",
                traceId = context.TraceIdentifier,
            }, context.RequestAborted);
        }
    }
}
