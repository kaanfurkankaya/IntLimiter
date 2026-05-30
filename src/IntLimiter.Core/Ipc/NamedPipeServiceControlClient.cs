using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntLimiter.Core.Contracts;
using IntLimiter.Core.Models;

namespace IntLimiter.Core.Ipc;

public sealed class NamedPipeServiceControlClient : IServiceControlClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;

    public NamedPipeServiceControlClient(string pipeName = PipeNames.ServicePipe, TimeSpan? connectTimeout = null)
    {
        _pipeName = pipeName;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(2);
    }

    public Task<ServiceStateDto> GetStateAsync(CancellationToken cancellationToken) =>
        SendAsync<ServiceStateDto>("GetState", null, cancellationToken);

    public Task<ServiceDiagnosticsDto> GetDiagnosticsAsync(CancellationToken cancellationToken) =>
        SendAsync<ServiceDiagnosticsDto>("GetDiagnostics", null, cancellationToken);

    public Task<IReadOnlyList<ProcessIdentity>> GetProcessesAsync(CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<ProcessIdentity>>("GetProcesses", null, cancellationToken);

    public Task<IReadOnlyList<BandwidthRule>> GetRulesAsync(CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<BandwidthRule>>("GetRules", null, cancellationToken);

    public Task ApplyRulesAsync(IReadOnlyList<BandwidthRule> rules, CancellationToken cancellationToken) =>
        SendNoDataAsync("ApplyRules", rules, cancellationToken);

    public Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken) =>
        SendNoDataAsync("DeleteRule", new DeleteRuleRequest { RuleId = ruleId }, cancellationToken);

    public Task StopAllAsync(CancellationToken cancellationToken) =>
        SendNoDataAsync<object>("StopAll", null, cancellationToken);

    public Task<IReadOnlyList<LogEntry>> GetLogsAsync(int take, CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<LogEntry>>("GetLogs", new GetLogsRequest { Take = take }, cancellationToken);

    public Task<IReadOnlyList<LogEntry>> GetRecentLogsAsync(int take, CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<LogEntry>>("GetRecentLogs", new GetLogsRequest { Take = take }, cancellationToken);

    private async Task SendNoDataAsync<TPayload>(string command, TPayload? payload, CancellationToken cancellationToken)
    {
        await SendAsync<JsonElement?>(command, payload, cancellationToken);
    }

    private async Task<TResult> SendAsync<TResult, TPayload>(string command, TPayload? payload, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(_connectTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        await pipe.ConnectAsync(linkedCts.Token);

        var request = new IpcRequest
        {
            Command = command,
            Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, JsonOptions)
        };

        var line = JsonSerializer.Serialize(request, JsonOptions) + "\n";
        var requestBytes = Encoding.UTF8.GetBytes(line);
        await pipe.WriteAsync(requestBytes, cancellationToken);
        await pipe.FlushAsync(cancellationToken);

        using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
        var responseLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            throw new IOException("IntLimiter service returned an empty response.");
        }

        var response = JsonSerializer.Deserialize<IpcResponse>(responseLine, JsonOptions)
            ?? throw new IOException("IntLimiter service returned an invalid response.");
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "IntLimiter service request failed.");
        }

        if (typeof(TResult) == typeof(JsonElement?) || response.Data is null)
        {
            return default!;
        }

        return response.Data.Value.Deserialize<TResult>(JsonOptions)
            ?? throw new IOException("IntLimiter service response payload could not be parsed.");
    }

    private Task<TResult> SendAsync<TResult>(string command, object? payload, CancellationToken cancellationToken) =>
        SendAsync<TResult, object>(command, payload, cancellationToken);
}
