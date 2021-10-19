using Nexus.Extensibility;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    public interface IDataControllerService
    {
        Task<IDataSourceController> GetDataSourceControllerAsync(BackendSource backendSource, CancellationToken cancellationToken);

        Task<IDataWriterController> GetDataWriterControllerAsync(Uri resourceLocator, ExportParameters exportParameters, CancellationToken cancellationToken);
    }
}