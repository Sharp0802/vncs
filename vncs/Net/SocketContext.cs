using System;
using System.Net.Sockets;

namespace vncs.Net;

public sealed record SocketContext(
    Socket Socket, 
    bool IsParent) : IDisposable
{
    public void Dispose()
    {
        Socket.Dispose();
    }
}