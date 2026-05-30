using System.Diagnostics;
using IntLimiter.Core.Contracts;
using IntLimiter.Core.Infrastructure;
using IntLimiter.Core.Models;
using IntLimiter.Core.Monitoring;
using IntLimiter.Core.RateLimiting;
using IntLimiter.DriverBridge.Packet;
using SharpDivert;
using SharpWinDivert = SharpDivert.WinDivert;

namespace IntLimiter.DriverBridge.WinDivert;

public sealed class WinDivertTrafficLimiter : ITrafficLimiter
{
    private const string CaptureFilter = "(ip or ipv6) and (tcp or udp) and !loopback";
    private const int MaxQueuedPackets = 10_000;
    private const long MaxQueuedBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private readonly IProcessNetworkMonitor _processNetworkMonitor;
    private readonly IAppLog _log;
    private readonly TrafficCounter _trafficCounter = new();
    private readonly RuleTokenBucketSet _buckets = new();
    private readonly object _stateSync = new();
    private readonly object _queueSync = new();
    private readonly object _sendSync = new();
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly PriorityQueue<ScheduledPacket, long> _queue = new();

    private IReadOnlyList<BandwidthRule> _rules = [];
    private SharpWinDivert? _handle;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _sendTask;
    private CaptureMode _captureMode = CaptureMode.None;
    private LimiterRuntimeStatus _status = new()
    {
        Mode = LimiterMode.Stopped,
        IsAdmin = Privilege.IsAdministrator(),
        Message = "Stopped"
    };
    private long _queuedBytes;
    private long _droppedPackets;
    private long _capturedPackets;
    private long _delayedPackets;
    private long _reinjectedPackets;
    private long _processMappingSuccess;
    private long _processMappingFailed;

    public WinDivertTrafficLimiter(IProcessNetworkMonitor processNetworkMonitor, IAppLog log)
    {
        _processNetworkMonitor = processNetworkMonitor;
        _log = log;
    }

    public async Task StartMonitoringAsync(CancellationToken cancellationToken)
    {
        lock (_stateSync)
        {
            if (_captureMode == CaptureMode.Monitoring || _captureMode == CaptureMode.Shaping)
            {
                return;
            }
        }

        try
        {
            await SwitchCaptureModeAsync(CaptureMode.Monitoring, cancellationToken);
            UpdateStatus("WinDivert passive monitoring active", LimiterMode.Monitoring, true);
            _log.Event("Information", "WinDivertMonitoringActive", nameof(WinDivertTrafficLimiter), "WinDivert passive monitoring active.",
                new Dictionary<string, object?> { ["filter"] = CaptureFilter });
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message, LimiterMode.Error, false);
            _log.Event("Warning", "WinDivertMonitoringFailed", nameof(WinDivertTrafficLimiter), $"WinDivert passive monitoring could not be started: {ex.Message}",
                new Dictionary<string, object?> { ["error"] = ex.Message });
        }
    }

    public async Task ApplyRulesAsync(IReadOnlyList<BandwidthRule> rules, CancellationToken cancellationToken)
    {
        var activeRules = rules.Where(rule => rule.Enabled && rule.IsValid).ToArray();
        if (activeRules.Length == 0)
        {
            lock (_stateSync)
            {
                _rules = [];
                _buckets.Configure([]);
            }

            await StartMonitoringAsync(cancellationToken);
            return;
        }

        lock (_stateSync)
        {
            _rules = activeRules;
            _buckets.Configure(activeRules);
        }

        await SwitchCaptureModeAsync(CaptureMode.Shaping, cancellationToken);
        UpdateStatus("WinDivert limiter active", LimiterMode.WinDivert, true);
        _log.Event("Information", "WinDivertModeActive", nameof(WinDivertTrafficLimiter), $"WinDivert mode active with {activeRules.Length} rule(s).",
            new Dictionary<string, object?> { ["ruleCount"] = activeRules.Length });
        _log.Event("Information", "RuleApplied", nameof(WinDivertTrafficLimiter), $"Applied {activeRules.Length} active WinDivert rule(s).",
            new Dictionary<string, object?> { ["ruleCount"] = activeRules.Length });
    }

    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        lock (_stateSync)
        {
            _rules = [];
            _buckets.Configure([]);
        }

        ClearQueue();
        try
        {
            await SwitchCaptureModeAsync(CaptureMode.Monitoring, cancellationToken);
            UpdateStatus("All limits stopped; passive monitoring active", LimiterMode.Monitoring, true);
        }
        catch (Exception ex)
        {
            UpdateStatus("All limits stopped; monitoring unavailable: " + ex.Message, LimiterMode.Error, false);
            _log.Event("Warning", "WinDivertMonitoringFailed", nameof(WinDivertTrafficLimiter), $"Limits were stopped, but passive monitoring could not be restarted: {ex.Message}",
                new Dictionary<string, object?> { ["error"] = ex.Message });
        }

        _log.Event("Information", "StopAllLimitsExecuted", nameof(WinDivertTrafficLimiter), "All WinDivert limits stopped; passive monitoring remains enabled when available.");
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        lock (_stateSync)
        {
            _rules = [];
            _buckets.Configure([]);
        }

        await StopCaptureAsync(cancellationToken);
        ClearQueue();
        UpdateStatus("Stopped", LimiterMode.Stopped, false);
        _log.Event("Information", "WinDivertStopped", nameof(WinDivertTrafficLimiter), "WinDivert capture stopped.");
    }

    public LimiterRuntimeStatus GetStatus()
    {
        lock (_stateSync)
        {
            return _status with
            {
                ActiveRuleCount = _rules.Count,
                QueuedPacketCount = GetQueueCount(),
                QueuedBytes = Interlocked.Read(ref _queuedBytes),
                CapturedPackets = Interlocked.Read(ref _capturedPackets),
                DelayedPackets = Interlocked.Read(ref _delayedPackets),
                ReinjectedPackets = Interlocked.Read(ref _reinjectedPackets),
                DroppedPackets = Interlocked.Read(ref _droppedPackets),
                ProcessMappingSuccess = Interlocked.Read(ref _processMappingSuccess),
                ProcessMappingFailed = Interlocked.Read(ref _processMappingFailed),
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public IReadOnlyList<ProcessIdentity> GetTrafficSnapshots() => _trafficCounter.Snapshot();

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync(CancellationToken.None);
        _queueSignal.Dispose();
    }

    private async Task SwitchCaptureModeAsync(CaptureMode mode, CancellationToken cancellationToken)
    {
        lock (_stateSync)
        {
            if (_captureMode == mode && _handle is not null)
            {
                return;
            }
        }

        await StopCaptureAsync(cancellationToken);
        StartCapture(mode);
    }

    private async Task StopCaptureAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts;
        Task? receiveTask;
        Task? sendTask;
        SharpWinDivert? handle;

        lock (_stateSync)
        {
            cts = _cts;
            receiveTask = _receiveTask;
            sendTask = _sendTask;
            handle = _handle;
            _cts = null;
            _receiveTask = null;
            _sendTask = null;
            _handle = null;
            _captureMode = CaptureMode.None;
        }

        if (cts is not null)
        {
            await cts.CancelAsync();
        }

        try
        {
            handle?.Shutdown();
        }
        catch
        {
            // Shutdown is best-effort; Dispose below is still required.
        }

        _queueSignal.Release();
        var tasks = new[] { receiveTask, sendTask }.Where(task => task is not null).Cast<Task>().ToArray();
        if (tasks.Length > 0)
        {
            await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(StopTimeout, cancellationToken));
        }

        handle?.Dispose();
        cts?.Dispose();
    }

    private void StartCapture(CaptureMode mode)
    {
        if (!Privilege.IsAdministrator())
        {
            throw new InvalidOperationException("WinDivert requires an elevated Administrator process.");
        }

        try
        {
            EnsureWinDivertFilesPresent();
            var flags = mode == CaptureMode.Monitoring ? SharpWinDivert.Flag.Sniff : (SharpWinDivert.Flag)0;
            var handle = new SharpWinDivert(CaptureFilter, SharpWinDivert.Layer.Network, 0, flags);
            _log.Event("Information", "WinDivertOpened", nameof(WinDivertTrafficLimiter), "WinDivert opened.",
                new Dictionary<string, object?>
                {
                    ["filter"] = CaptureFilter,
                    ["mode"] = mode.ToString()
                });
            var cts = new CancellationTokenSource();
            lock (_stateSync)
            {
                _handle = handle;
                _cts = cts;
                _captureMode = mode;
                _receiveTask = Task.Run(() => ReceiveLoopAsync(handle, mode, cts.Token));
                _sendTask = mode == CaptureMode.Shaping
                    ? Task.Run(() => SendLoopAsync(handle, cts.Token))
                    : null;
            }
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message, LimiterMode.Error, false);
            _log.Event("Error", "WinDivertOpenFailed", nameof(WinDivertTrafficLimiter), $"WinDivert could not be started: {ex.Message}",
                new Dictionary<string, object?> { ["error"] = ex.Message, ["mode"] = mode.ToString() });
            throw;
        }
    }

    private async Task ReceiveLoopAsync(SharpWinDivert handle, CaptureMode mode, CancellationToken cancellationToken)
    {
        var packetBuffer = new Memory<byte>(new byte[SharpWinDivert.MTUMax]);
        var addressBuffer = new Memory<WinDivertAddress>(new WinDivertAddress[1]);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var (receivedLength, addressLength) = handle.RecvEx(packetBuffer.Span, addressBuffer.Span);
                if (receivedLength <= 0 || addressLength <= 0)
                {
                    continue;
                }

                var packet = packetBuffer.Span[..(int)receivedLength].ToArray();
                var address = addressBuffer.Span[0];
                await HandlePacketAsync(handle, packet, address, mode, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested)
            {
                _log.Warning(nameof(WinDivertTrafficLimiter), $"Receive loop stopped: {ex.Message}");
                break;
            }
            catch (Exception ex)
            {
                UpdateStatus(ex.Message, LimiterMode.Error, false);
                _log.Error(nameof(WinDivertTrafficLimiter), $"Receive loop error: {ex.Message}");
                break;
            }
        }
    }

    private async Task HandlePacketAsync(
        SharpWinDivert handle,
        byte[] packet,
        WinDivertAddress address,
        CaptureMode mode,
        CancellationToken cancellationToken)
    {
        if (address.Loopback)
        {
            ForwardIfShaping(handle, packet, address, mode);
            return;
        }

        var parsed = IpPacketParser.TryParse(packet, address.Outbound);
        if (parsed is null)
        {
            ForwardIfShaping(handle, packet, address, mode);
            return;
        }

        var captured = Interlocked.Increment(ref _capturedPackets);
        if (ShouldLogPacketEvent(captured))
        {
            _log.Event("Information", "PacketCaptured", nameof(WinDivertTrafficLimiter), "Packet captured.",
                new Dictionary<string, object?>
                {
                    ["bytes"] = parsed.Length,
                    ["direction"] = parsed.Direction.ToString(),
                    ["mode"] = mode.ToString(),
                    ["count"] = captured
                });
        }

        var flow = parsed.FlowKey.Protocol == IpProtocol.Tcp
            ? await _processNetworkMonitor.FindTcpFlowAsync(parsed.FlowKey, cancellationToken)
            : await _processNetworkMonitor.FindUdpFlowAsync(parsed.FlowKey, cancellationToken);
        var process = flow is null
            ? new ProcessIdentity { ProcessId = 0, ProcessName = "unknown" }
            : new ProcessIdentity
            {
                ProcessId = flow.ProcessId,
                ProcessName = flow.ProcessName,
                ExecutablePath = flow.ProcessPath
            };

        if (flow is null)
        {
            var failed = Interlocked.Increment(ref _processMappingFailed);
            if (mode == CaptureMode.Shaping && failed % 5000 == 0)
            {
                _log.Event("Warning", "ProcessMappingFailed", nameof(WinDivertTrafficLimiter), "Process mapping failed.",
                    new Dictionary<string, object?>
                    {
                        ["localAddress"] = parsed.FlowKey.LocalAddress,
                        ["localPort"] = parsed.FlowKey.LocalPort,
                        ["remoteAddress"] = parsed.FlowKey.RemoteAddress,
                        ["remotePort"] = parsed.FlowKey.RemotePort,
                        ["count"] = failed
                    });
            }
        }
        else
        {
            var succeeded = Interlocked.Increment(ref _processMappingSuccess);
            if (ShouldLogPacketEvent(succeeded))
            {
                _log.Event("Information", "ProcessMappingSuccess", nameof(WinDivertTrafficLimiter), "Process mapping success.",
                    new Dictionary<string, object?>
                    {
                        ["pid"] = flow.ProcessId,
                        ["processName"] = flow.ProcessName,
                        ["count"] = succeeded
                    });
            }
        }

        _trafficCounter.Add(process, parsed.Direction, parsed.Length);
        if (mode == CaptureMode.Monitoring)
        {
            return;
        }

        var matchingRules = GetMatchingRules(process, parsed.Direction);
        if (matchingRules.Length == 0)
        {
            SendPacket(handle, packet, address);
            return;
        }

        var delay = _buckets.Reserve(matchingRules, parsed.Length);
        if (delay <= TimeSpan.Zero)
        {
            SendPacket(handle, packet, address);
            return;
        }

        var delayed = Interlocked.Increment(ref _delayedPackets);
        if (ShouldLogPacketEvent(delayed))
        {
            _log.Event("Information", "PacketDelayed", nameof(WinDivertTrafficLimiter), "Packet delayed.",
                new Dictionary<string, object?>
                {
                    ["bytes"] = parsed.Length,
                    ["delayMs"] = delay.TotalMilliseconds,
                    ["direction"] = parsed.Direction.ToString(),
                    ["processId"] = process.ProcessId,
                    ["processName"] = process.ProcessName,
                    ["count"] = delayed
                });
        }

        EnqueuePacket(new ScheduledPacket(packet, address, Stopwatch.GetTimestamp() + ToStopwatchTicks(delay)));
    }

    private void ForwardIfShaping(SharpWinDivert handle, byte[] packet, WinDivertAddress address, CaptureMode mode)
    {
        if (mode == CaptureMode.Shaping)
        {
            SendPacket(handle, packet, address);
        }
    }

    private async Task SendLoopAsync(SharpWinDivert handle, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var packet = await DequeueDuePacketAsync(cancellationToken);
            if (packet is not null)
            {
                SendPacket(handle, packet.Packet, packet.Address);
            }
        }
    }

    private async Task<ScheduledPacket?> DequeueDuePacketAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan wait;
            lock (_queueSync)
            {
                if (_queue.Count == 0)
                {
                    wait = Timeout.InfiniteTimeSpan;
                }
                else
                {
                    var next = _queue.Peek();
                    var ticksUntilDue = next.DueTimestamp - Stopwatch.GetTimestamp();
                    if (ticksUntilDue <= 0)
                    {
                        var packet = _queue.Dequeue();
                        Interlocked.Add(ref _queuedBytes, -packet.Packet.Length);
                        return packet;
                    }

                    wait = TimeSpan.FromSeconds(ticksUntilDue / (double)Stopwatch.Frequency);
                }
            }

            if (wait == Timeout.InfiniteTimeSpan)
            {
                await _queueSignal.WaitAsync(cancellationToken);
            }
            else
            {
                await _queueSignal.WaitAsync(wait, cancellationToken);
            }
        }

        return null;
    }

    private void EnqueuePacket(ScheduledPacket packet)
    {
        lock (_queueSync)
        {
            if (_queue.Count >= MaxQueuedPackets || Interlocked.Read(ref _queuedBytes) + packet.Packet.Length > MaxQueuedBytes)
            {
                var dropped = Interlocked.Increment(ref _droppedPackets);
                _log.Event("Warning", "PacketDropped", nameof(WinDivertTrafficLimiter), "Packet queue overflow; packet dropped to avoid unbounded memory growth.",
                    new Dictionary<string, object?> { ["bytes"] = packet.Packet.Length, ["count"] = dropped });
                return;
            }

            _queue.Enqueue(packet, packet.DueTimestamp);
            Interlocked.Add(ref _queuedBytes, packet.Packet.Length);
        }

        _queueSignal.Release();
    }

    private void SendPacket(SharpWinDivert handle, byte[] packet, WinDivertAddress address)
    {
        try
        {
            var addresses = new[] { address };
            lock (_sendSync)
            {
                _ = handle.SendEx(packet, addresses);
            }

            var reinjected = Interlocked.Increment(ref _reinjectedPackets);
            if (ShouldLogPacketEvent(reinjected))
            {
                _log.Event("Information", "PacketReinjected", nameof(WinDivertTrafficLimiter), "Packet reinjected.",
                    new Dictionary<string, object?> { ["bytes"] = packet.Length, ["count"] = reinjected });
            }
        }
        catch (Exception ex)
        {
            _log.Event("Warning", "PacketReinjectFailed", nameof(WinDivertTrafficLimiter), $"Packet reinjection failed: {ex.Message}",
                new Dictionary<string, object?> { ["error"] = ex.Message });
        }
    }

    private BandwidthRule[] GetMatchingRules(ProcessIdentity process, TrafficDirection direction)
    {
        lock (_stateSync)
        {
            return _rules.Where(rule => rule.Matches(process, direction)).ToArray();
        }
    }

    private void ClearQueue()
    {
        lock (_queueSync)
        {
            _queue.Clear();
            Interlocked.Exchange(ref _queuedBytes, 0);
        }
    }

    private int GetQueueCount()
    {
        lock (_queueSync)
        {
            return _queue.Count;
        }
    }

    private void UpdateStatus(string message, LimiterMode mode, bool running)
    {
        lock (_stateSync)
        {
            _status = _status with
            {
                Mode = mode,
                IsRunning = running,
                IsAdmin = Privilege.IsAdministrator(),
                WinDivertReady = running && (mode == LimiterMode.WinDivert || mode == LimiterMode.Monitoring),
                Message = message,
                LastError = mode == LimiterMode.Error ? message : _status.LastError,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    private void EnsureWinDivertFilesPresent()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var dllPath = Path.Combine(baseDirectory, "WinDivert.dll");
        var driverPath = Path.Combine(baseDirectory, "WinDivert64.sys");
        if (File.Exists(dllPath) && File.Exists(driverPath))
        {
            return;
        }

        var missing = new List<string>();
        if (!File.Exists(dllPath))
        {
            missing.Add("WinDivert.dll");
        }

        if (!File.Exists(driverPath))
        {
            missing.Add("WinDivert64.sys");
        }

        var message = "WinDivert files missing: " + string.Join(", ", missing);
        _log.Event("Error", "WinDivertFilesMissing", nameof(WinDivertTrafficLimiter), message,
            new Dictionary<string, object?>
            {
                ["baseDirectory"] = baseDirectory,
                ["missing"] = string.Join(",", missing)
            });
        throw new FileNotFoundException(message);
    }

    private static bool ShouldLogPacketEvent(long count) =>
        count <= 10 || count % 1000 == 0;

    private static long ToStopwatchTicks(TimeSpan timeSpan) =>
        (long)(timeSpan.TotalSeconds * Stopwatch.Frequency);

    private enum CaptureMode
    {
        None,
        Monitoring,
        Shaping
    }

    private sealed record ScheduledPacket(byte[] Packet, WinDivertAddress Address, long DueTimestamp);
}
