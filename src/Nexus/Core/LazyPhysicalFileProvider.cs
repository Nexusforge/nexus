using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Nexus.Core
{
    internal class LazyPhysicalFileProvider : IFileProvider
    {
        private string _folderPath;
        private PhysicalFileProvider _physicalFileProvider;

        public LazyPhysicalFileProvider(string folderPath)
        {
            _folderPath = folderPath;
        }

        private PhysicalFileProvider GetPhysicalFileProvider()
        {
            if (!Directory.Exists(_folderPath))
                Directory.CreateDirectory(_folderPath);

            if (_physicalFileProvider is null || _physicalFileProvider.Root != _folderPath)
                _physicalFileProvider = new PhysicalFileProvider(_folderPath);

            return _physicalFileProvider;
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            return GetPhysicalFileProvider().GetDirectoryContents(subpath);
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            return GetPhysicalFileProvider().GetFileInfo(subpath);
        }

        public IChangeToken Watch(string filter)
        {
            return GetPhysicalFileProvider().Watch(filter);
        }
    }
}
