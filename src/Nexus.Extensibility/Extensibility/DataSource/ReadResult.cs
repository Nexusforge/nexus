using System;
using System.Buffers;

namespace Nexus.Extensibility
{
    public struct ReadResult<T> : IDisposable
    {
        #region Fields

        private IMemoryOwner<T> _dataOwner;
        private IMemoryOwner<byte> _statusOwner;

        #endregion

        #region Constuctors

        public ReadResult(int length)
        {
            _dataOwner = MemoryPool<T>.Shared.Rent(length);
            _statusOwner = MemoryPool<byte>.Shared.Rent(length);

            this.Length = length;

            this.Data.Span.Clear();
            this.Status.Span.Clear();
        }

        #endregion

        #region Properties

        public Memory<T> Data => _dataOwner.Memory;

        public Memory<byte> Status => _statusOwner.Memory;

        public int Length { get; }

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
