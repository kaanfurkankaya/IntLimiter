using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using IntLimiter.Core.Contracts;
using IntLimiter.Core.Models;

namespace IntLimiter.Core.Monitoring;

public sealed class ProcessNetworkMonitor : IProcessNetworkMonitor
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private readonly ConcurrentDictionary<int, ProcessIdentity> _processCache = new();
    private readonly ConcurrentDictionary<int, byte> _pathLoadStarted = new();
    private volatile FlowCache _flowCache = new([], DateTimeOffset.MinValue);

    public Task<IReadOnlyList<ProcessIdentity>> GetProcessesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processes = Process.GetProcesses()
            .Select(CreateProcessIdentity)
            .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.ProcessId)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ProcessIdentity>>(processes);
    }

    public Task<IReadOnlyList<NetworkFlow>> GetTcpFlowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var flows = ReadTcpFlows();
        _flowCache = new FlowCache(flows, DateTimeOffset.UtcNow);
        return Task.FromResult<IReadOnlyList<NetworkFlow>>(flows);
    }

    public Task<IReadOnlyList<NetworkFlow>> GetUdpFlowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var flows = ReadUdpFlows();
        _flowCache = new FlowCache(_flowCache.Flows.Where(flow => flow.Protocol == IpProtocol.Tcp).Concat(flows).ToArray(), DateTimeOffset.UtcNow);
        return Task.FromResult<IReadOnlyList<NetworkFlow>>(flows);
    }

    public async Task<NetworkFlow?> FindTcpFlowAsync(PacketFlowKey key, CancellationToken cancellationToken)
    {
        var cache = _flowCache;
        if (DateTimeOffset.UtcNow - cache.RefreshedAt > TimeSpan.FromMilliseconds(500))
        {
            await RefreshFlowsAsync(cancellationToken);
            cache = _flowCache;
        }

        return cache.Flows.FirstOrDefault(flow =>
            flow.Protocol == key.Protocol
            && flow.LocalPort == key.LocalPort
            && flow.RemotePort == key.RemotePort
            && AddressMatches(flow.LocalAddress, key.LocalAddress)
            && AddressMatches(flow.RemoteAddress, key.RemoteAddress));
    }

    public async Task<NetworkFlow?> FindUdpFlowAsync(PacketFlowKey key, CancellationToken cancellationToken)
    {
        var cache = _flowCache;
        if (DateTimeOffset.UtcNow - cache.RefreshedAt > TimeSpan.FromMilliseconds(500))
        {
            await RefreshFlowsAsync(cancellationToken);
            cache = _flowCache;
        }

        var udpFlows = cache.Flows.Where(flow => flow.Protocol == key.Protocol && flow.LocalPort == key.LocalPort).ToArray();
        return udpFlows.FirstOrDefault(flow => AddressMatches(flow.LocalAddress, key.LocalAddress))
            ?? udpFlows.FirstOrDefault();
    }

    private Task RefreshFlowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _flowCache = new FlowCache(ReadTcpFlows().Concat(ReadUdpFlows()).ToArray(), DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    private NetworkFlow[] ReadTcpFlows()
    {
        return ReadTcp4Flows().Concat(ReadTcp6Flows()).ToArray();
    }

    private NetworkFlow[] ReadUdpFlows()
    {
        return ReadUdp4Flows().Concat(ReadUdp6Flows()).ToArray();
    }

    private NetworkFlow[] ReadTcp4Flows()
    {
        var bufferSize = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet, TcpTableClass.OwnerPidAll, 0);
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var result = GetExtendedTcpTable(buffer, ref bufferSize, true, AfInet, TcpTableClass.OwnerPidAll, 0);
            if (result != 0)
            {
                return [];
            }

            var entries = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, sizeof(int));
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            var flows = new List<NetworkFlow>(entries);

            for (var index = 0; index < entries; index++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(IntPtr.Add(rowPtr, index * rowSize));
                if (row.LocalPort == 0 || row.RemotePort == 0)
                {
                    continue;
                }

                var process = GetCachedProcess((int)row.OwningPid);
                flows.Add(new NetworkFlow
                {
                    Protocol = IpProtocol.Tcp,
                    LocalAddress = new IPAddress(row.LocalAddr).ToString(),
                    LocalPort = ConvertPort(row.LocalPort),
                    RemoteAddress = new IPAddress(row.RemoteAddr).ToString(),
                    RemotePort = ConvertPort(row.RemotePort),
                    ProcessId = (int)row.OwningPid,
                    ProcessName = process.ProcessName,
                    ProcessPath = process.ExecutablePath
                });
            }

            return flows.ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private NetworkFlow[] ReadTcp6Flows()
    {
        var bufferSize = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet6, TcpTableClass.OwnerPidAll, 0);
        if (bufferSize <= 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var result = GetExtendedTcpTable(buffer, ref bufferSize, true, AfInet6, TcpTableClass.OwnerPidAll, 0);
            if (result != 0)
            {
                return [];
            }

            var entries = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, sizeof(int));
            var rowSize = Marshal.SizeOf<MibTcp6RowOwnerPid>();
            var flows = new List<NetworkFlow>(entries);

            for (var index = 0; index < entries; index++)
            {
                var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(IntPtr.Add(rowPtr, index * rowSize));
                if (row.LocalPort == 0 || row.RemotePort == 0)
                {
                    continue;
                }

                var process = GetCachedProcess((int)row.OwningPid);
                flows.Add(new NetworkFlow
                {
                    Protocol = IpProtocol.Tcp,
                    LocalAddress = FormatIPv6(row.LocalAddr, row.LocalScopeId),
                    LocalPort = ConvertPort(row.LocalPort),
                    RemoteAddress = FormatIPv6(row.RemoteAddr, row.RemoteScopeId),
                    RemotePort = ConvertPort(row.RemotePort),
                    ProcessId = (int)row.OwningPid,
                    ProcessName = process.ProcessName,
                    ProcessPath = process.ExecutablePath
                });
            }

            return flows.ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private NetworkFlow[] ReadUdp4Flows()
    {
        var bufferSize = 0;
        _ = GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, AfInet, UdpTableClass.OwnerPid, 0);
        if (bufferSize <= 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var result = GetExtendedUdpTable(buffer, ref bufferSize, true, AfInet, UdpTableClass.OwnerPid, 0);
            if (result != 0)
            {
                return [];
            }

            var entries = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, sizeof(int));
            var rowSize = Marshal.SizeOf<MibUdpRowOwnerPid>();
            var flows = new List<NetworkFlow>(entries);

            for (var index = 0; index < entries; index++)
            {
                var row = Marshal.PtrToStructure<MibUdpRowOwnerPid>(IntPtr.Add(rowPtr, index * rowSize));
                if (row.LocalPort == 0)
                {
                    continue;
                }

                var process = GetCachedProcess((int)row.OwningPid);
                flows.Add(new NetworkFlow
                {
                    Protocol = IpProtocol.Udp,
                    LocalAddress = new IPAddress(row.LocalAddr).ToString(),
                    LocalPort = ConvertPort(row.LocalPort),
                    RemoteAddress = "",
                    RemotePort = 0,
                    ProcessId = (int)row.OwningPid,
                    ProcessName = process.ProcessName,
                    ProcessPath = process.ExecutablePath
                });
            }

            return flows.ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private NetworkFlow[] ReadUdp6Flows()
    {
        var bufferSize = 0;
        _ = GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, AfInet6, UdpTableClass.OwnerPid, 0);
        if (bufferSize <= 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var result = GetExtendedUdpTable(buffer, ref bufferSize, true, AfInet6, UdpTableClass.OwnerPid, 0);
            if (result != 0)
            {
                return [];
            }

            var entries = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, sizeof(int));
            var rowSize = Marshal.SizeOf<MibUdp6RowOwnerPid>();
            var flows = new List<NetworkFlow>(entries);

            for (var index = 0; index < entries; index++)
            {
                var row = Marshal.PtrToStructure<MibUdp6RowOwnerPid>(IntPtr.Add(rowPtr, index * rowSize));
                if (row.LocalPort == 0)
                {
                    continue;
                }

                var process = GetCachedProcess((int)row.OwningPid);
                flows.Add(new NetworkFlow
                {
                    Protocol = IpProtocol.Udp,
                    LocalAddress = FormatIPv6(row.LocalAddr, row.LocalScopeId),
                    LocalPort = ConvertPort(row.LocalPort),
                    RemoteAddress = "",
                    RemotePort = 0,
                    ProcessId = (int)row.OwningPid,
                    ProcessName = process.ProcessName,
                    ProcessPath = process.ExecutablePath
                });
            }

            return flows.ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private ProcessIdentity CreateProcessIdentity(Process process)
    {
        try
        {
            if (_processCache.TryGetValue(process.Id, out var cached))
            {
                QueuePathLoad(cached);
                return cached;
            }

            var identity = _processCache.GetOrAdd(process.Id, _ => new ProcessIdentity
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName
            });
            QueuePathLoad(identity);
            return identity;
        }
        finally
        {
            process.Dispose();
        }
    }

    private ProcessIdentity GetCachedProcess(int processId)
    {
        if (_processCache.TryGetValue(processId, out var cached) && !string.IsNullOrWhiteSpace(cached.ExecutablePath))
        {
            return cached;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (_processCache.TryGetValue(processId, out cached) && !string.IsNullOrWhiteSpace(cached.ExecutablePath))
            {
                return cached;
            }

            return _processCache.AddOrUpdate(processId, _ => FromProcess(process), (_, _) => FromProcess(process));
        }
        catch
        {
            return new ProcessIdentity { ProcessId = processId, ProcessName = $"pid:{processId}" };
        }
    }

    private void QueuePathLoad(ProcessIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.ExecutablePath) || !_pathLoadStarted.TryAdd(identity.ProcessId, 0))
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                using var process = Process.GetProcessById(identity.ProcessId);
                var enriched = FromProcess(process);
                _processCache.AddOrUpdate(identity.ProcessId, _ => enriched, (_, current) =>
                    string.Equals(current.ProcessName, enriched.ProcessName, StringComparison.OrdinalIgnoreCase)
                        ? enriched
                        : current);
            }
            catch
            {
                // Some processes exit or deny path access; PID/name is still enough for display.
            }
        });
    }

    private static ProcessIdentity FromProcess(Process process)
    {
        string? path = null;
        try
        {
            path = process.MainModule?.FileName;
        }
        catch
        {
            // Some system processes deny module path access; PID/name are still useful.
        }

        return new ProcessIdentity
        {
            ProcessId = process.Id,
            ProcessName = process.ProcessName,
            ExecutablePath = path
        };
    }

    private static int ConvertPort(uint port) =>
        (ushort)IPAddress.NetworkToHostOrder((short)(port & 0xFFFF));

    private static string FormatIPv6(byte[] address, uint scopeId)
    {
        var ipAddress = new IPAddress(address, scopeId);
        return ipAddress.ToString();
    }

    private static bool AddressMatches(string flowAddress, string packetAddress)
    {
        var normalizedFlowAddress = NormalizeAddress(flowAddress);
        var normalizedPacketAddress = NormalizeAddress(packetAddress);
        if (string.Equals(normalizedFlowAddress, normalizedPacketAddress, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedFlowAddress is "0.0.0.0" or "::" or "::0";
    }

    private static string NormalizeAddress(string address)
    {
        var scopeIndex = address.IndexOf('%', StringComparison.Ordinal);
        return scopeIndex >= 0 ? address[..scopeIndex] : address;
    }

    private sealed record FlowCache(IReadOnlyList<NetworkFlow> Flows, DateTimeOffset RefreshedAt);

    private enum TcpTableClass
    {
        OwnerPidAll = 5
    }

    private enum UdpTableClass
    {
        OwnerPid = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint LocalAddr;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int tcpTableLength,
        bool sort,
        int ipVersion,
        TcpTableClass tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr udpTable,
        ref int udpTableLength,
        bool sort,
        int ipVersion,
        UdpTableClass tableClass,
        uint reserved);
}
