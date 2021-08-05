using System.Threading;

namespace Nexus.Services
{
    internal interface IFileAccessManager
    {
        void Register(string filePath, CancellationToken cancellationToken);

        void Unregister(string filePath);
    }
}