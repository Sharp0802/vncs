using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using vncs.Reflection;

namespace vncs.Net;

public static class Op
{
    public const byte Disconnect = 1;
    public const byte Code       = 2;
    public const byte Invoke     = 3;

    public const byte ErrorEntryPointInvalidSignature = 250;
    public const byte ErrorNoEntryPoint               = 251;
    public const byte ErrorNoImage                    = 252;
    public const byte ErrorInvalidFlow                = 253;
    public const byte ErrorBadImage                   = 254;
    public const byte Okay                            = 255;
}

public sealed record SocketContext(
    Socket Socket,
    bool   IsParent) : IDisposable
{
    private const double UpdateTimeout = 3;

    private CollectibleAssemblyLoadContext? _domain;

    private class RefData(Memory<byte> data)
    {
        public Memory<byte> Data { get; set; } = data;
    }

    public IPEndPoint RemoteEndPoint { get; } = (IPEndPoint)Socket.RemoteEndPoint!;

    private readonly Queue<RefData> _messageQueue = new();

    private UpdateContext? _updateContext;

    private Thread? _invoker;
    
    public void Queue(byte op, Memory<byte> data)
    {
        var buffer = new byte[5];
        buffer[0] = op;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan()[1..], (uint)data.Length);
        _messageQueue.Enqueue(new RefData(buffer));
        if (data.Length > 0)
            _messageQueue.Enqueue(new RefData(data));
    }

    private void BlockingSend(Span<byte> memory)
    {
        if (memory.Length <= 0)
            return;

        for (var i = 0; i < memory.Length;)
        {
            try
            {
                i += Socket.Send(memory[i..]);
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode != SocketError.WouldBlock)
                    throw;
            }
        }
    }

    private bool Flush(RefData data)
    {
        if (data.Data.Length <= 0)
            return true;

        try
        {
            var w = Socket.Send(data.Data.Span);
            if (w <= 0)
                return false;

            if (w < data.Data.Length)
            {
                data.Data = data.Data[w..];
                return false;
            }
        }
        catch (SocketException e)
        {
            if (e.SocketErrorCode == SocketError.WouldBlock)
                return false;
            throw;
        }

        return true;
    }

    private int SafeReceive(Span<byte> span)
    {
        try
        {
            return Socket.Receive(span);
        }
        catch (SocketException e)
        {
            if (e.SocketErrorCode != SocketError.WouldBlock)
                throw;

            return -1;
        }
    }

    private void BlockingReceive(Span<byte> span)
    {
        for (var i = 0; i < span.Length;)
        {
            var t = SafeReceive(span[i..]);
            if (t < 0)
                continue;
            i += t;
        }
    }

    public bool Update(IEnumerable<SocketContext> contexts)
    {
        if (_updateContext is not null)
        {
            var delta = DateTime.UtcNow - _updateContext.Value.Timestamp;
            if (delta.TotalSeconds > UpdateTimeout)
            {
                Logger.Fail($"Update timeout from {RemoteEndPoint}");
                return false;
            }
        }

        // Flush
        while (_messageQueue.TryDequeue(out var data) && Flush(data))
        {
        }

        // Polling input
        if (!Socket.Poll(0, SelectMode.SelectRead))
            return true;

        // Check if the socket is alive
        if (Socket.Available == 0)
        {
            Logger.Fail($"Unexpected connection reset by {RemoteEndPoint}");
            return false;
        }

        byte op     = 0;
        uint length = 0;
        if (_updateContext is not null)
        {
            op     = _updateContext.Value.OpCode;
            length = _updateContext.Value.Length;
        }
        else
        {
            if (Socket.Available < 5)
            {
                // Wait for required octet-stream for UpdateContext
                return true;
            }

            BlockingReceive(new Span<byte>(ref op));
            unsafe
            {
                BlockingReceive(new Span<byte>(Unsafe.AsPointer(ref length), 4));
            }

            if (BitConverter.IsLittleEndian)
            {
                uint buffer = 0;
                BinaryPrimitives.ReverseEndianness(new Span<uint>(ref length), new Span<uint>(ref buffer));
                length = buffer;
            }

            _updateContext = new UpdateContext(op, length);
        }

        var required = op switch
        {
            Op.Okay => 0,
            
            Op.Code       => 0,
            Op.Invoke     => 0,
            Op.Disconnect => IsParent ? 6 : 0,
            _             => int.MaxValue
        };

        if (required == int.MaxValue)
        {
            Logger.Fail($"Unknown opcode(0x{op:X2}) from {RemoteEndPoint}");
            return false;
        }

        if (length < required)
        {
            Logger.Fail($"{RemoteEndPoint}: Message size({length}B) cannot be less than required size({required}B)");
            return false;
        }

        if (Socket.Available < length)
        {
            // Wait for required data
            return true;
        }

        switch (op)
        {
            case Op.Code:
            {
                var buffer = new byte[(int)length];
                BlockingReceive(buffer);
                
                if (IsParent)
                {
                    Logger.Info($"Loading COFF image by {RemoteEndPoint}...");
                    foreach (var context in contexts)
                    {
                        if (context.IsParent || ReferenceEquals(this, context))
                            continue;

                        context.Queue(Op.Code, buffer);
                    }

                    _domain?.Unload();
                    _domain = new CollectibleAssemblyLoadContext();

                    try
                    {
                        _domain.LoadFromStream(new MemoryStream(buffer));
                    }
                    catch (BadImageFormatException e)
                    {
                        Queue(Op.ErrorBadImage, Encoding.UTF8.GetBytes(e.FusionLog ?? e.ToString()));
                        Logger.Fail("Bad COFF image! cannot be loaded");
                    }
                    
                    var asm = _domain.Assemblies.FirstOrDefault(asm => asm.EntryPoint is not null);
                    if (asm is null)
                    {
                        Logger.Fail("There is no entrypoint in loaded assembly");
                        Queue(Op.ErrorNoEntryPoint, Array.Empty<byte>());
                        break;
                    }
                        
                    // TODO : implement function parameters

                    if (asm.EntryPoint!.GetParameters().Length > 0 ||
                        asm.EntryPoint!.ReturnType != typeof(void))
                    {
                        Logger.Fail("An entrypoint of assembly has invalid signature");
                        Queue(Op.ErrorEntryPointInvalidSignature, Array.Empty<byte>());
                        break;
                    }

                    _invoker = new Thread(asm.EntryPoint!.CreateDelegate<ThreadStart>());
                    
                    var assemblies = string.Join(", ", _domain.Assemblies.Select(
                        a => $"'{a.GetName().Name ?? a.FullName}'"));
                    Queue(Op.Okay, Array.Empty<byte>());
                    Logger.Info($"{assemblies} loaded");
                }
                else
                {
                    Logger.Fail($"A child node {RemoteEndPoint} requests loading COFF image");
                    Queue(Op.ErrorInvalidFlow, Array.Empty<byte>());
                }

                break;
            }

            case Op.Invoke:
            {
                if (!IsParent)
                {
                    Logger.Fail($"A child node {RemoteEndPoint} requests invoking loaded assembly");
                    Queue(Op.ErrorInvalidFlow, Array.Empty<byte>());
                    break;
                }
                
                if (_domain is null)
                {
                    Logger.Fail($"{RemoteEndPoint} requests invoking assembly; but there is no loaded assembly");
                    Queue(Op.ErrorNoImage, Array.Empty<byte>());
                    break;
                }
                
                // TODO : implement function parameters

                _invoker!.Start();
                
                break;
            }

            case Op.Disconnect:
            {
                if (IsParent)
                {
                    var buffer = new byte[(int)length];
                    BlockingReceive(buffer);

                    var endpoint = new IPEndPoint(
                        new IPAddress(buffer.AsSpan()[..(int)(length - 2)]),
                        BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan()[(int)(length - 2)..]));

                    Logger.Warn($"Parent node {RemoteEndPoint} requests redirection to {endpoint}");

                    // TODO
                }
                else
                {
                    Logger.Warn($"{RemoteEndPoint} requests disconnecting...");

                    Span<byte> buffer = stackalloc byte[1];
                    BlockingSend(buffer);
                }

                return false;
            }
        }

        _updateContext = null;
        return true;
    }

    public void Disconnect()
    {
        Logger.Info($"Disconnection request to {RemoteEndPoint}");

        Span<byte> buffer = stackalloc byte[] { Op.Disconnect, 0, 0, 0, 0 };
        BlockingSend(buffer);

        Logger.Info($"Waiting for response by {RemoteEndPoint}");

        BlockingReceive(buffer[..1]);
    }

    public void Disconnect(SocketContext parent)
    {
        if (IsParent)
            throw new InvalidOperationException();

        Logger.Info($"Disconnection request to {RemoteEndPoint}");

        using var buffer = MemoryPool<byte>.Shared.Rent(256);
        buffer.Memory.Span[0] = Op.Disconnect;
        var written = SocketHelper.Serialize(parent.RemoteEndPoint, buffer.Memory[1..]);
        BlockingSend(buffer.Memory[..(written + 1)].Span);
        BlockingReceive(buffer.Memory[..1].Span);
    }

    public void Dispose()
    {
        Socket.Dispose();
    }
}