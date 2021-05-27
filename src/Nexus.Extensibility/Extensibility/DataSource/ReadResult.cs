using System;
using System.Buffers;

namespace Nexus.Extensibility
{
    public class ReadResult<T> : IDisposable
    {
        #region Fields

        private IMemoryOwner<T> _datasetOwner;
        private IMemoryOwner<byte> _statusOwner;

        #endregion

        #region Constuctors

        public ReadResult(int length)
        {
            _datasetOwner = MemoryPool<T>.Shared.Rent(length);
            _statusOwner = MemoryPool<byte>.Shared.Rent(length);

            this.Dataset.Span.Clear();
            this.Status.Span.Clear();
        }

        #endregion

        #region Properties

        public Memory<T> Dataset => _datasetOwner.Memory;

        public Memory<byte> Status => _statusOwner.Memory;

        public int Length => this.Dataset.Length;

        #endregion

        #region IDisposable

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _datasetOwner?.Dispose();
                    _statusOwner?.Dispose();
                }
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
