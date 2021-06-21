using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System.IO;

namespace Nexus.Core
{
    public class LazyPhysicalFileProvider : IFileProvider
    {
        private string _relativePath;
        private string _dataBaseFolderPath;
        private PhysicalFileProvider _physicalFileProvider;

        public LazyPhysicalFileProvider(string dataBaseFolderPath, string relativePath)
        {
            _dataBaseFolderPath = dataBaseFolderPath;
            _relativePath = relativePath;
        }

        private PhysicalFileProvider GetPhysicalFileProvider()
        {
            var path = Path.Combine(_dataBaseFolderPath, _relativePath);

            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            if (_physicalFileProvider == null || _physicalFileProvider.Root != path)
                _physicalFileProvider = new PhysicalFileProvider(path);

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
