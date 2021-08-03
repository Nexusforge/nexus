using Nexus.Extensibility;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface IDataSourceControllerService
    {
        Task<DataSourceController> GetControllerAsync(ClaimsPrincipal user, BackendSource backendSource, CancellationToken cancellationToken);
    }
}