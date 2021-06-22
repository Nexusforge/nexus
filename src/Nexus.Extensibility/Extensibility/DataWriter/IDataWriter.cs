using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
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

        Task OnParametersSetAsync()
        {
            return Task.CompletedTask;
        }

        Task OpenAsync(DateTime begin, List<CatalogWriteInfo> writeInfoGroups, CancellationToken cancellationToken);

        Task WriteAsync(ulong fileOffset, ulong bufferOffset, ulong length, CatalogWriteInfo writeInfoGroup, CancellationToken cancellationToken);

        #endregion
    }
}