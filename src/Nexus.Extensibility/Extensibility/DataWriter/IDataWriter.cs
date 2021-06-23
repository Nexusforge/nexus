using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public interface IDataWriter
    {
        #region Properties

        string TargetFolder { set; }

        ILogger Logger { set; }

        Dictionary<string, string> Configuration { set; }

        #endregion

        #region Methods

        void OnParametersSet()
        {
            //
        }

        void Open(DateTime begin, Dictionary<Catalog, SampleRateContainer> catalogMap);

        void Write(ulong fileOffset, ulong bufferOffset, ulong length, CatalogWriteInfo writeInfoGroup);

        #endregion
    }
}