namespace BenchConsole.Api.Services;

public sealed class ApiException(int statusCode, string? message = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? Error { get; } = message;

    public static ApiException BadRequest(string? message = null) => new(400, message);
    public static ApiException NotFound(string? message = null) => new(404, message);
    public static ApiException Conflict(string? message = null) => new(409, message);
    public static ApiException Forbidden() => new(403);
    public static ApiException Unavailable(string message) => new(503, message);
}
