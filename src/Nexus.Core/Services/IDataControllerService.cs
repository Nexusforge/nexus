using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Models;
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
            CatalogCache? catalogCache = default);

        Task<IDataWriterController> GetDataWriterControllerAsync(
            Uri resourceLocator, 
            ExportParameters exportParameters, 
            CancellationToken cancellationToken);
    }
}