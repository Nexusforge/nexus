using System;

namespace Nexus.Extensibility
{
    public struct ReadResult
    {
        #region Constuctors

        internal ReadResult(Memory<byte> data, Memory<byte> status)
        {
            this.Data = data;
            this.Status = status;
        }

        #endregion

        #region Properties

        public Memory<byte> Data { get; }

        public Memory<byte> Status { get; }

        #endregion
    }
}
