using System;

namespace Nexus.Buffers
{
    public interface IExtendedBuffer : IBuffer
    {
        Memory<byte> StatusBuffer { get; }
    }
}
