using System.Buffers;
using System.Runtime.InteropServices;

namespace Nexus.Utilities
{
#warning Validate against this: https://github.com/windows-toolkit/WindowsCommunityToolkit/pull/3520/files 

    internal class CastMemoryManager<TFrom, TTo> : MemoryManager<TTo>
        where TFrom : struct
        where TTo : struct
    {
        private readonly Memory<TFrom> _from;

        public CastMemoryManager(Memory<TFrom> from) => _from = from;

        public override Span<TTo> GetSpan() => MemoryMarshal.Cast<TFrom, TTo>(_from.Span);

        protected override void Dispose(bool disposing)
        {
            //
        }

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        public override void Unpin() => throw new NotSupportedException();
    }

    internal class ReadonlyCastMemoryManager<TFrom, TTo> : MemoryManager<TTo>
        where TFrom : struct
        where TTo : struct
    {
        private readonly ReadOnlyMemory<TFrom> _from;

        public ReadonlyCastMemoryManager(ReadOnlyMemory<TFrom> from) => _from = from;

        public override Span<TTo> GetSpan() => MemoryMarshal.Cast<TFrom, TTo>(MemoryMarshal.AsMemory(_from).Span);

        protected override void Dispose(bool disposing)
        {
            //
        }

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        public override void Unpin() => throw new NotSupportedException();
    }
}
