using System;

namespace Nexus.Buffers
{
    public interface ISimpleBuffer : IBuffer
    {
        Span<double> Buffer { get; }
    }
}
