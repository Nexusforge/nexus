﻿using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Nexus.Buffers
{
    internal class ExtendedBuffer<T> : ExtendedBufferBase where T : unmanaged
    {
        #region Fields

        private IMemoryOwner<T> _buffer;

        #endregion

        #region Constructors

        public ExtendedBuffer(int length) : base(length)
        {
            _buffer = MemoryPool<T>.Shared.Rent(length);
            MemoryMarshal.AsBytes(_buffer.Memory.Span).Fill(0);

            this.ElementSize = NexusUtilities.SizeOf(typeof(T));
        }

        #endregion

        #region Properties

        public override int ElementSize { get; }

        public override Span<byte> RawBuffer => MemoryMarshal.Cast<T, byte>(_buffer.Memory.Span);

        public Memory<T> Buffer => _buffer.Memory;

        #endregion

        #region Methods

        public override ISimpleBuffer ToSimpleBuffer()
        {
            return new SimpleBuffer(BufferUtilities.ApplyDatasetStatus(this.Buffer, this.StatusBuffer));
        }

        public override void Clear()
        {
            this.Buffer.Span.Clear();
            base.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _buffer.Dispose();

            base.Dispose(disposing);
        }

        #endregion
    }
}
