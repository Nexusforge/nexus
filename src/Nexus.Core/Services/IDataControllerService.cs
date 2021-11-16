using Nexus.Core;
using Nexus.Extensibility;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface IDataControllerService
    {
        Task<IDataSourceController> GetDataSourceControllerAsync(
            BackendSource backendSource,
            CancellationToken cancellationToken,
            BackendSourceCache? backendSourceCache = default);

        Task<IDataWriterController> GetDataWriterControllerAsync(
            Uri resourceLocator, 
            ExportParameters exportParameters, 
            CancellationToken cancellationToken);
    }
}