namespace BenchConsole.Api.Services.Common;

public sealed class ApiException(int statusCode, string? message = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? Error { get; } = message;

    public static ApiException BadRequest(string? message = null) => new(400, message);
    public static ApiException NotFound(string? message = null) => new(404, message);
    public static ApiException Conflict(string? message = null) => new(409, message);
    public static ApiException Forbidden(string? message = null) => new(403, message);
    public static ApiException Unauthorized(string? message = null) => new(401, message);
    public static ApiException Unavailable(string message) => new(503, message);
}
