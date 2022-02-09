using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    internal class DataSourceDoubleStream : Stream
    {
        #region Fields

        private long _position;
        private long _length;
        private Stream _reader;

        #endregion

        #region Constructors

        public DataSourceDoubleStream(long length, PipeReader reader)
        {
            _length = length;
            _reader = reader.AsStream();
        }

        #endregion

        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get
            {
                return _position;
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        #endregion

        #region Methods

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override int Read(Span<byte> buffer)
        {
            throw new NotImplementedException();
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        {
            return _reader.BeginRead(buffer, offset, count, callback, state);
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            var readCount = _reader.EndRead(asyncResult);
            _position += readCount;
            return readCount;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var readCount = await _reader.ReadAsync(buffer, offset, count, cancellationToken);
            _position += readCount;
            return readCount;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var readCount = await _reader.ReadAsync(buffer, cancellationToken);
            _position += readCount;
            return readCount;
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            return _reader.CopyToAsync(destination, bufferSize, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        protected override void Dispose(bool disposing)
        {
            _reader.Dispose();
        }

        #endregion
    }
}
