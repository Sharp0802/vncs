using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace vncs.Net;

public static class SocketHelper
{
    public static Socket Socket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay             = true;
        socket.ExclusiveAddressUse = false;
        socket.LingerState         = new LingerOption(true, 0);
        return socket;
    }

    public static int Serialize(IPEndPoint endpoint, Memory<byte> buffer)
    {
        var address = endpoint.Address.GetAddressBytes();
        if (buffer.Length < address.Length + 2)
            return -1;
        address.CopyTo(buffer);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[address.Length..].Span, (ushort)endpoint.Port);
        return address.Length + 2;
    }
}