using System.Net.Sockets;

namespace vncs.Net;

public class SocketHelper
{
    public static Socket Socket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay             = true;
        socket.ExclusiveAddressUse = false;
        socket.LingerState         = new LingerOption(true, 0);
        return socket;
    }
}