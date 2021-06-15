using System;
using System.Buffers;

namespace Nexus.Extensibility
{
    public struct ReadResult : IDisposable
    {
        #region Fields

        private IMemoryOwner<byte> _dataOwner;
        private IMemoryOwner<byte> _statusOwner;

        #endregion

        #region Constuctors

        internal ReadResult(int elementCount, int elementSize)
        {
            _dataOwner = MemoryPool<byte>.Shared.Rent(elementCount * elementSize);
            this.Data = _dataOwner.Memory.Slice(0, elementCount * elementSize);
            this.Data.Span.Clear();

            _statusOwner = MemoryPool<byte>.Shared.Rent(elementCount);
            this.Status = _statusOwner.Memory.Slice(0, elementCount);
            this.Status.Span.Clear();
        }

        #endregion

        #region Properties

        public Memory<byte> Data { get; }

        public Memory<byte> Status { get; }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _dataOwner?.Dispose();
            _statusOwner?.Dispose();
        }

        #endregion
    }
}
