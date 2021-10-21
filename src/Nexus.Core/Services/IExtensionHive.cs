using Nexus.Extensibility;
using Nexus.PackageManagement;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface IExtensionHive
    {
        T GetInstance<T>(string identifier) where T : IExtension;
        Task LoadPackagesAsync(IEnumerable<PackageReference> packageReferences, CancellationToken cancellationToken);
        bool TryGetInstance<T>(string identifier, out T instance) where T : IExtension;
    }
}