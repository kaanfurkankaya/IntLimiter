using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntLimiter.Core.Ipc;
using IntLimiter.Core.Models;
using IntLimiter.Core.Contracts;

namespace IntLimiter.Service;

public sealed class NamedPipeIpcServer : BackgroundService
{
    private static readonly HashSet<string> PollingCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "GetState",
        "GetDiagnostics",
        "GetProcesses",
        "GetLogs",
        "GetRecentLogs"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly LimiterCoordinator _coordinator;
    private readonly IAppLog _appLog;
    private readonly ILogger<NamedPipeIpcServer> _logger;

    public NamedPipeIpcServer(LimiterCoordinator coordinator, IAppLog appLog, ILogger<NamedPipeIpcServer> logger)
    {
        _coordinator = coordinator;
        _appLog = appLog;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _coordinator.InitializeAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var pipe = NamedPipeServerStreamAcl.Create(
                PipeNames.ServicePipe,
                PipeDirection.InOut,
                10,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                64 * 1024,
                64 * 1024,
                CreatePipeSecurity());

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
                _ = Task.Run(() => HandleClientAsync(pipe, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Named pipe accept failed.");
                await pipe.DisposeAsync();
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _coordinator.ShutdownAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true)
                {
                    AutoFlush = true
                };

                var requestLine = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    await WriteResponseAsync(writer, IpcResponse.Fail("Empty request."), cancellationToken);
                    return;
                }

                var request = JsonSerializer.Deserialize<IpcRequest>(requestLine, JsonOptions);
                if (request is null)
                {
                    await WriteResponseAsync(writer, IpcResponse.Fail("Invalid request."), cancellationToken);
                    return;
                }

                if (!PollingCommands.Contains(request.Command))
                {
                    _appLog.Event("Information", "ClientConnected", nameof(NamedPipeIpcServer), $"Client command received: {request.Command}",
                        new Dictionary<string, object?> { ["command"] = request.Command });
                }

                var response = await DispatchAsync(request, cancellationToken);
                await WriteResponseAsync(writer, response, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Named pipe client request failed.");
            }
        }
    }

    private async Task<IpcResponse> DispatchAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        try
        {
            switch (request.Command)
            {
                case "GetState":
                    return IpcResponse.Ok(_coordinator.GetState(), JsonOptions);

                case "GetDiagnostics":
                    return IpcResponse.Ok(_coordinator.GetDiagnostics(), JsonOptions);

                case "GetProcesses":
                    return IpcResponse.Ok(await _coordinator.GetProcessesAsync(cancellationToken), JsonOptions);

                case "GetRules":
                    return IpcResponse.Ok(await _coordinator.GetRulesAsync(), JsonOptions);

                case "ApplyRules":
                    var rules = request.Payload?.Deserialize<List<BandwidthRule>>(JsonOptions) ?? [];
                    await _coordinator.ApplyRulesAsync(rules, cancellationToken);
                    return IpcResponse.EmptyOk();

                case "DeleteRule":
                    var deleteRule = request.Payload?.Deserialize<DeleteRuleRequest>(JsonOptions)
                        ?? throw new InvalidOperationException("DeleteRule payload is missing.");
                    await _coordinator.DeleteRuleAsync(deleteRule.RuleId, cancellationToken);
                    return IpcResponse.EmptyOk();

                case "StopAll":
                    await _coordinator.StopAllAsync(cancellationToken);
                    return IpcResponse.EmptyOk();

                case "GetLogs":
                    var logRequest = request.Payload?.Deserialize<GetLogsRequest>(JsonOptions) ?? new GetLogsRequest();
                    return IpcResponse.Ok(_coordinator.GetState().Logs.TakeLast(logRequest.Take).ToArray(), JsonOptions);

                case "GetRecentLogs":
                    var recentLogRequest = request.Payload?.Deserialize<GetLogsRequest>(JsonOptions) ?? new GetLogsRequest();
                    return IpcResponse.Ok(_coordinator.GetState().Logs.TakeLast(recentLogRequest.Take).ToArray(), JsonOptions);

                default:
                    return IpcResponse.Fail($"Unknown command '{request.Command}'.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IPC command {Command} failed.", request.Command);
            return IpcResponse.Fail(ex.Message);
        }
    }

    private static Task WriteResponseAsync(StreamWriter writer, IpcResponse response, CancellationToken cancellationToken) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions).AsMemory(), cancellationToken);

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return security;
    }
}
