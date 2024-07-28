using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace vncs;

public enum OpCode : byte
{
    Okay,
    
    ESessionSecretChallenge,
}

public class ServiceNode
{
    private const int SessionSecretSize = 16;
    private const int DefaultPort       = 9812;
    
    private Socket _socket;
    
    private bool        _isRoot;
    private IPEndPoint? _local;
    private IPEndPoint? _remote;

    private Action<string> _log;
    
    public ServiceNode(Action<string> log, IPEndPoint? local, IPEndPoint? remote)
    {
        _log = log;
        
        if (local is null == remote is null)
            throw new InvalidOperationException();
        
        _isRoot = local is not null;
        _local  = local;
        _remote = remote;

        _socket = NewSocket();
    }

    private static Socket NewSocket()
    {
        var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            ExclusiveAddressUse = false,
            NoDelay             = true
        };
        return sock;
    }

    private byte[] GetVersion()
    {
        var version = typeof(ServiceNode).Assembly.GetName().Version;
        if (version is null)
            throw new VersionNotFoundException();

        var buffer = new byte[4];
        buffer[0] = (byte)version.Major;
        buffer[1] = (byte)version.Minor;
        buffer[2] = (byte)version.Build;
        buffer[3] = (byte)version.Revision;

        return buffer;
    }

    private static Memory<byte> Receive(Socket socket, byte[] buffer, int size, CancellationToken token)
    {
        if (buffer.Length < size)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = ArrayPool<byte>.Shared.Rent(Math.Max(size, buffer.Length * 2));
        }
        
        for (var rcv = 0; rcv < size && !token.IsCancellationRequested; )
            rcv += socket.Receive(buffer, rcv, size - rcv, SocketFlags.None);
        if (token.IsCancellationRequested)
            throw new TaskCanceledException();
        
        return new Memory<byte>(buffer, 0, size);
    }

    public void InitializeAsRoot()
    {
        _socket = NewSocket();
        _socket.Bind(new IPEndPoint(IPAddress.Any, DefaultPort));
        _socket.Listen();
    }
    
    public void InitializeAsNonRoot(out Socket parent, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);

        while (!token.IsCancellationRequested)
        {
            _socket.Connect(_remote!);
            
            _log($"connection established to '{_remote}'");

            var version = Receive(_socket, buffer, 4, token);
            if (!version.Span.SequenceEqual(GetVersion()))
            {
                _log($"version mismatched with '{_remote}'");
                
                _socket.Shutdown(SocketShutdown.Both);
                throw new BadImageFormatException("Protocol version mismatched");
            }

            var op = Receive(_socket, buffer, 1, token);
            if (op.Span[0] != (byte)OpCode.Okay)
            {
                _log("redirect requested; receiving peer info...");
                
                var redirect = Receive(_socket, buffer, 6, token);
                var addr     = new IPAddress(redirect[..4].Span);
                var port     = BinaryPrimitives.ReadInt16BigEndian(redirect[4..6].Span);

                _remote = new IPEndPoint(addr, port);
                
                _log($"change remote endpoint to '{_remote}'");
                
                _socket.Disconnect(true);
            }
            else
            {
                var secret = Receive(_socket, buffer, SessionSecretSize, token);
                
                var port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
                
                _socket.Disconnect(true);
                _socket.Bind(new IPEndPoint(IPAddress.Any, port));
                _socket.Listen();

                var challenger = _socket.Accept();
                var challenge = Receive(_socket, buffer, SessionSecretSize, token);
                if (!secret.Span.SequenceEqual(challenge.Span))
                {
                    _log("session secret incorrect (from peer)");
                    
                    buffer[0] = (byte)OpCode.ESessionSecretChallenge;
                    challenger.Send(buffer, 1, SocketFlags.None);
                    challenger.Shutdown(SocketShutdown.Both);
                    challenger.Close();

                    throw new AuthenticationException("Session secret challenge failed");
                }

                _log("initialized");
                parent = challenger;
                return;
            }
        }
        
        throw new TaskCanceledException();
    }
}