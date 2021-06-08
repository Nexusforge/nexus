using System.Threading;

namespace Nexus.Services
{
    public interface IFileAccessManager
    {
        void Register(string filePath, CancellationToken cancellationToken);

        void Unregister(string filePath);
    }
}