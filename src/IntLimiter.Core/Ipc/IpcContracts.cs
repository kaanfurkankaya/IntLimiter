using System.Text.Json;

namespace IntLimiter.Core.Ipc;

public static class PipeNames
{
    public const string ServicePipe = "IntLimiter.Service";
}

public sealed record IpcRequest
{
    public string Command { get; init; } = "";
    public JsonElement? Payload { get; init; }
}

public sealed record IpcResponse
{
    public bool Success { get; init; }
    public JsonElement? Data { get; init; }
    public string? Error { get; init; }

    public static IpcResponse Ok<T>(T data, JsonSerializerOptions options) => new()
    {
        Success = true,
        Data = JsonSerializer.SerializeToElement(data, options)
    };

    public static IpcResponse EmptyOk() => new()
    {
        Success = true
    };

    public static IpcResponse Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}

public sealed record DeleteRuleRequest
{
    public Guid RuleId { get; init; }
}

public sealed record GetLogsRequest
{
    public int Take { get; init; } = 100;
}
