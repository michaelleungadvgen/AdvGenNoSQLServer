// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Tests for the per-connection frame payload limit (pre-auth memory-exhaustion guard).
/// </summary>
public class ConnectionHandlerLimitTests
{
    [Fact]
    public async Task ReadMessages_OversizedPreAuthFrame_RejectedFromDeclaredLength()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        using var serverSide = await listener.AcceptTcpClientAsync();
        await connectTask;

        var config = new ServerConfiguration { PreAuthMaxMessageBytes = 1024 };
        using var handler = new ConnectionHandler("test-conn", serverSide, new MessageProtocol(), config);

        // Craft a valid header declaring a 10 MB payload (over the 1 KB pre-auth limit).
        // The payload itself is never sent — the limit must fire on the declared length,
        // before any buffer of that size is allocated.
        var header = new byte[12];
        header[0] = (byte)'N'; header[1] = (byte)'O'; header[2] = (byte)'S'; header[3] = (byte)'Q';
        header[4] = 0; header[5] = 1;                       // protocol version 1
        header[6] = (byte)MessageType.Command;
        header[7] = 0;                                      // flags
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 10 * 1024 * 1024);

        await client.GetStream().WriteAsync(header);

        var ex = await Assert.ThrowsAsync<ProtocolException>(async () =>
        {
            await foreach (var _ in handler.ReadMessagesAsync())
            {
                // no messages expected
            }
        });
        Assert.Contains("exceeds the per-connection limit", ex.Message);
    }

    [Fact]
    public async Task ReadMessages_FrameWithinLimit_StillWorks()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        using var serverSide = await listener.AcceptTcpClientAsync();
        await connectTask;

        var config = new ServerConfiguration { PreAuthMaxMessageBytes = 1024 };
        using var handler = new ConnectionHandler("test-conn", serverSide, new MessageProtocol(), config);
        var protocol = new MessageProtocol();

        // Send a small ping frame through the real serializer
        var ping = new NoSqlMessage
        {
            MessageType = MessageType.Ping,
            Flags = 0,
            Payload = Array.Empty<byte>(),
            PayloadLength = 0
        };
        var data = protocol.Serialize(ping);
        var length = MessageHeader.HeaderSize + ping.PayloadLength + 4;
        await client.GetStream().WriteAsync(data.AsMemory(0, length));

        NoSqlMessage? received = null;
        await foreach (var msg in handler.ReadMessagesAsync())
        {
            received = msg;
            break;
        }

        Assert.NotNull(received);
        Assert.Equal(MessageType.Ping, received!.MessageType);
    }
}
