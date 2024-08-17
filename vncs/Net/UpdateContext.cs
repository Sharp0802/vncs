using System;

namespace vncs.Net;

public struct UpdateContext(byte op, uint length)
{
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public byte     OpCode    { get; } = op;
    public uint     Length    { get; } = length;
}
