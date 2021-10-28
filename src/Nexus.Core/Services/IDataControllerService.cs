using Nexus.Extensibility;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface IDataControllerService
    {
        Task<IDataSourceController> GetDataSourceControllerForDataAccessAsync(ClaimsPrincipal user, BackendSource backendSource, CancellationToken cancellationToken);

        Task<IDataSourceController> GetDataSourceControllerAsync(BackendSource backendSource, CancellationToken cancellationToken);

        Task<IDataWriterController> GetDataWriterControllerAsync(Uri resourceLocator, ExportParameters exportParameters, CancellationToken cancellationToken);
    }
}