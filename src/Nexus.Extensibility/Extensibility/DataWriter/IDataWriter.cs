using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public interface IDataWriter
    {
        #region Methods

        Task SetContext(DataWriterContext context);

        void Open(DateTime begin, Dictionary<Catalog, TimeSpan> catalogMap);

        void Write(DatasetRecord dataset, Memory<byte> data, Memory<byte> status, ulong fileOffset);

        #endregion
    }
}