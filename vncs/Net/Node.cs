using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace vncs.Net;

public class Node : IDisposable
{
    private const int DefaultPort       = 6974;
    private const int MaxAttempt        = 256;
    private const int BranchingCriteria = 2;

    private readonly object _syncRoot = new();

    private Thread? _thread;
    private bool    _token = true;

    private EndPoint? _parent;

    private Socket              Listener { get; } = SocketHelper.Socket();
    private List<SocketContext> Peers    { get; } = [];

    public Node()
    {
    }

    public Node(EndPoint parent)
    {
        _parent = parent;
    }

    public bool Initialize()
    {
        try
        {
            return InitializeInternal();
        }
        catch (Exception e)
        {
            Logger.Fail(e.ToString());
            return false;
        }
    }

    private bool InitializeInternal()
    {
        IPEndPoint? remote = null, local = null;

        // Available node traversing
        if (_parent is not null)
        {
            var socket = SocketHelper.Socket();
            try
            {
                while (true)
                {
                    Logger.Info($"Connect to {_parent}...");
                    socket.Connect(_parent);
                    Logger.Info("Connection established");

                    remote = (IPEndPoint)socket.RemoteEndPoint!;
                    local  = (IPEndPoint)socket.LocalEndPoint!;

                    if (!socket.Poll(1000000, SelectMode.SelectRead))
                    {
                        Logger.Fail($"A parent node {_parent} doesn't respond; connection reset");
                        return false;
                    }

                    using var buffer = MemoryPool<byte>.Shared.Rent(256);

                    if (socket.Receive(buffer.Memory[..1].Span) <= 0)
                    {
                        Logger.Fail("Connection reset by peer");
                        return false;
                    }

                    if (buffer.Memory.Span[0] != 0)
                        break;

                    Logger.Info("Redirection requested");

                    if (socket.Receive(buffer.Memory[..1].Span) <= 0)
                    {
                        Logger.Fail("Connection reset by peer");
                        return false;
                    }

                    var size = buffer.Memory.Span[0];
                    for (var i = 0; i < size; )
                    {
                        var rcv = socket.Receive(buffer.Memory.Slice(i, size - i).Span);
                        if (rcv <= 0)
                        {
                            Logger.Fail("Connection reset by peer");
                            return false;
                        }

                        i += rcv;
                    }

                    var address = new IPAddress(buffer.Memory[..4].Span);
                    
                    for (var i = 0; i < 2; )
                    {
                        var rcv = socket.Receive(buffer.Memory[..(2 - i)].Span);
                        if (rcv <= 0)
                        {
                            Logger.Fail("Connection reset by peer");
                            return false;
                        }

                        i += rcv;
                    }

                    var port = BinaryPrimitives.ReadUInt16BigEndian(buffer.Memory.Span);
                    
                    _parent = new IPEndPoint(address, port);

                    Logger.Info($"Redirect to {_parent}");

                    socket.Dispose();
                    socket = SocketHelper.Socket();
                }
            }
            finally
            {
                socket.Dispose();
            }
        }

        // Listening on...
        Listener.Bind(_parent is null ? new IPEndPoint(IPAddress.Any, DefaultPort) : local!);
        Listener.Listen();

        Logger.Info($"Listen on {Listener.LocalEndPoint}");

        // Accept parent node
        if (_parent is not null)
        {
            var parent = Listener.Accept();
            if (!((IPEndPoint)parent.RemoteEndPoint!).Equals(remote))
            {
                Logger.Fail($"{parent.RemoteEndPoint} acts as parent ({remote} expected)");

                parent.Close();
                return false;
            }

            Logger.Info($"{parent.RemoteEndPoint} connected");

            Peers.Add(new SocketContext(parent, true));
        }

        return true;
    }

    public void BeginExecution()
    {
        lock (_syncRoot)
        {
            if (_thread is not null)
                throw new ThreadStateException("Execution already started");
            _thread = new Thread(() =>
            {
                try
                {
                    Run();
                }
                catch (Exception e)
                {
                    Logger.Fail(e.ToString());
                    Thread.Sleep(500);
                }
            });
            _thread.Start();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => EndExecution();
        }
    }

    public void EndExecution()
    {
        lock (_syncRoot)
        {
            if (_thread is null)
                return;
            Volatile.Write(ref _token, false);
            _thread.Join();
        }
    }

    private void Run()
    {
        var listenerCtx = new SocketContext(Listener, false);

        for (var i = 0; Volatile.Read(ref _token); i = (i + 1) % (Peers.Count + 1))
        {
            var ctx = i == 0 ? listenerCtx : Peers[i - 1];
            if (!ctx.Socket.Poll(0, SelectMode.SelectRead))
                continue;

            if (ctx.Socket == Listener)
                PassListener();
            else
            {
                try
                {
                    PassChild(Peers[i - 1]);
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.ConnectionRefused)
                        Logger.Fail($"Connection refused by {ctx.Socket.RemoteEndPoint}");
                    throw;
                }
            }
        }
    }

    private IPEndPoint GetRedirection()
    {
        return (IPEndPoint)Peers[Random.Shared.Next(0, Peers.Count)].Socket.RemoteEndPoint!;
    }

    private void PassListener()
    {
        IPEndPoint remote, local;
        using (var client = Listener.Accept())
        {
            remote = (IPEndPoint)client.RemoteEndPoint!;
            local  = (IPEndPoint)client.LocalEndPoint!;
            
            Logger.Info($"{remote} knocking...");

            if (Peers.Count >= BranchingCriteria)
            {
                var redirection = GetRedirection();
                
                var address = redirection.Address.GetAddressBytes();
                
                client.Send([0, (byte)address.Length]);
                client.Send(address);
                Span<byte> port = stackalloc byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(port, (ushort)redirection.Port);
                client.Send(port);

                Logger.Info($"{remote} redirected to {redirection}");

                return;
            }

            client.Send([1]);
        }

        int    i;
        Socket peer = null!;
        for (i = 0; i < MaxAttempt; ++i)
        {
            peer = SocketHelper.Socket();
            peer.Bind(local);

            try
            {
                peer.Connect(remote);
                break;
            }
            catch (SocketException e)
            {
                peer.Close();
                if (e.SocketErrorCode != SocketError.ConnectionRefused)
                    throw;
            }
        }

        if (i >= MaxAttempt)
        {
            Logger.Fail($"Couldn't reverse connection for {remote}; Peer may be using symmetric NAT?");
            peer.Close();
            return;
        }

        Logger.Info($"{remote} connected");
        Peers.Add(new SocketContext(peer, false));
    }

    private void PassChild(SocketContext socket)
    {
        Console.WriteLine("hello");

        using var buffer = MemoryPool<byte>.Shared.Rent(256);
        socket.Socket.Send("Hello, World"u8);
        var recv = socket.Socket.Receive(buffer.Memory.Span);

        Logger.Info(Encoding.UTF8.GetString(buffer.Memory[..recv].Span));
    }

    public virtual void Dispose()
    {
        foreach (var peer in Peers)
            peer.Dispose();
        Listener.Dispose();
        GC.SuppressFinalize(this);
    }
}