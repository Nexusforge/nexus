using System;

namespace Nexus.Buffers
{
    public interface IExtendedBuffer : IBuffer
    {
        Span<byte> StatusBuffer { get; }
    }
}
