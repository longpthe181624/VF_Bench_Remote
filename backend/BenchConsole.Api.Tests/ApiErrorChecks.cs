using System.Text.Json;
using BenchConsole.Api.Middleware;
using BenchConsole.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BenchConsole.Api.Tests;

internal static class ApiErrorChecks
{
    public static async Task Run(IServiceProvider services, Action<bool, string> check)
    {
        using var scope = services.CreateScope();
        var problems = scope.ServiceProvider.GetRequiredService<ProblemDetailsFactory>();
        async Task<(int Status, string Body, string? ContentType)> Invoke(Exception error, bool aborted = false)
        {
            var context = new DefaultHttpContext { TraceIdentifier = "test-error-trace", RequestServices = scope.ServiceProvider };
            context.Response.Body = new MemoryStream();
            if (aborted) context.RequestAborted = new CancellationToken(true);
            var middleware = new ApiExceptionMiddleware(_ => Task.FromException(error), NullLogger<ApiExceptionMiddleware>.Instance);
            await middleware.InvokeAsync(context, problems);
            context.Response.Body.Position = 0;
            return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync(), context.Response.ContentType);
        }

        var validation = await Invoke(ApiException.BadRequest("Tên file không hợp lệ."));
        check(validation.Status == 400 && JsonDocument.Parse(validation.Body).RootElement.GetProperty("error").GetString() == "Tên file không hợp lệ.",
            "API error: lỗi nghiệp vụ giữ status và thông báo cho FE/client");

        var notFound = await Invoke(ApiException.NotFound());
        check(notFound.Status == 404 && notFound.ContentType?.StartsWith("application/problem+json") == true
            && JsonDocument.Parse(notFound.Body).RootElement.GetProperty("status").GetInt32() == 404,
            "API error: NotFound không thông báo giữ ProblemDetails của MVC");

        var forbidden = await Invoke(ApiException.Forbidden());
        check(forbidden.Status == 403 && forbidden.Body.Length == 0, "API error: Forbidden giữ response 403 không có body");

        var conflict = await Invoke(new JobFlowException(409, "Lease đã hết hạn."));
        check(conflict.Status == 409 && JsonDocument.Parse(conflict.Body).RootElement.GetProperty("error").GetString() == "Lease đã hết hạn.",
            "API error: workflow job giữ hợp đồng lỗi sau khi bỏ exception filter riêng");

        var unexpected = await Invoke(new IOException("private-storage-path-and-secret"));
        check(unexpected.Status == 500 && JsonDocument.Parse(unexpected.Body).RootElement.GetProperty("traceId").GetString() == "test-error-trace",
            "API error: lỗi hệ thống trả JSON với traceId để tra log");
        check(!unexpected.Body.Contains("private-storage-path-and-secret") && !unexpected.Body.Contains("IOException"),
            "API error: không trả chi tiết exception nội bộ cho client");

        var cancelled = await Invoke(new OperationCanceledException(), aborted: true);
        check(cancelled.Status != 500 && cancelled.Body.Length == 0, "API error: client ngắt kết nối không sinh response lỗi hệ thống");
    }
}
