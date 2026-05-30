using System.Buffers.Binary;
using System.Net;
using IntLimiter.Core.Models;

namespace IntLimiter.DriverBridge.Packet;

internal static class IpPacketParser
{
    public static ParsedPacket? TryParse(ReadOnlySpan<byte> packet, bool outbound)
    {
        if (packet.Length < 1)
        {
            return null;
        }

        return (packet[0] >> 4) switch
        {
            4 => TryParseIpv4(packet, outbound),
            6 => TryParseIpv6(packet, outbound),
            _ => null
        };
    }

    private static ParsedPacket? TryParseIpv4(ReadOnlySpan<byte> packet, bool outbound)
    {
        if (packet.Length < 20)
        {
            return null;
        }

        var ipHeaderLength = (packet[0] & 0x0F) * 4;
        if (ipHeaderLength < 20 || packet.Length < ipHeaderLength + 4)
        {
            return null;
        }

        if (!TryGetSupportedProtocol(packet[9], out var protocol))
        {
            return null;
        }

        var sourceAddress = new IPAddress(packet.Slice(12, 4)).ToString();
        var destinationAddress = new IPAddress(packet.Slice(16, 4)).ToString();
        return BuildParsedPacket(packet[ipHeaderLength..], packet.Length, outbound, protocol, sourceAddress, destinationAddress);
    }

    private static ParsedPacket? TryParseIpv6(ReadOnlySpan<byte> packet, bool outbound)
    {
        if (packet.Length < 44)
        {
            return null;
        }

        if (!TryGetSupportedProtocol(packet[6], out var protocol))
        {
            return null;
        }

        var sourceAddress = new IPAddress(packet.Slice(8, 16)).ToString();
        var destinationAddress = new IPAddress(packet.Slice(24, 16)).ToString();
        return BuildParsedPacket(packet[40..], packet.Length, outbound, protocol, sourceAddress, destinationAddress);
    }

    private static ParsedPacket? BuildParsedPacket(
        ReadOnlySpan<byte> transport,
        int packetLength,
        bool outbound,
        IpProtocol protocol,
        string sourceAddress,
        string destinationAddress)
    {
        if (transport.Length < 4)
        {
            return null;
        }

        var sourcePort = BinaryPrimitives.ReadUInt16BigEndian(transport[..2]);
        var destinationPort = BinaryPrimitives.ReadUInt16BigEndian(transport.Slice(2, 2));
        var direction = outbound ? TrafficDirection.Upload : TrafficDirection.Download;
        var key = outbound
            ? new PacketFlowKey(protocol, sourceAddress, sourcePort, destinationAddress, destinationPort)
            : new PacketFlowKey(protocol, destinationAddress, destinationPort, sourceAddress, sourcePort);

        return new ParsedPacket(key, direction, packetLength);
    }

    private static bool TryGetSupportedProtocol(byte value, out IpProtocol protocol)
    {
        if (value == (byte)IpProtocol.Tcp)
        {
            protocol = IpProtocol.Tcp;
            return true;
        }

        if (value == (byte)IpProtocol.Udp)
        {
            protocol = IpProtocol.Udp;
            return true;
        }

        protocol = default;
        return false;
    }
}

internal sealed record ParsedPacket(PacketFlowKey FlowKey, TrafficDirection Direction, int Length);
