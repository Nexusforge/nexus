using Microsoft.Extensions.Options;
using Nexus.Core;
using System.IO;
using System.Linq;
using System.Net;

namespace Nexus.Services
{
    internal class DatabaseManager : IDatabaseManager
    {
        // data/config/project.json
        // data/config/news.json
        // data/catalogs/abc/attachements
        // data/catalogs/abc/meta
        // data/users/def/code
        // data/users/users.db
        // data/users/def/additional_extensions.json
        // data/cache/abc
        // data/export

        private PathsOptions _pathsOptions;

        public DatabaseManager(IOptions<PathsOptions> pathsOptions)
        {
            _pathsOptions = pathsOptions.Value;
        }

        public bool TryReadProject(out Stream? stream)
        {
            var filePath = Path.Combine(_pathsOptions.Config, "project.json");
            stream = null;

            if (File.Exists(filePath))
            {
                stream = File.OpenRead(filePath);
                return true;
            }

            return false;
        }

        public bool TryReadNews(out Stream? stream)
        {
            var filePath = Path.Combine(_pathsOptions.Config, "news.json");
            stream = null;

            if (File.Exists(filePath))
            {
                stream = File.OpenRead(filePath);
                return true;
            }

            return false;
        }

        public bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, out Stream attachment)
        {
            attachment = null;

            var catalogFolderName = WebUtility.UrlDecode(catalogId);
            var attachementFolder = Path.Combine(_pathsOptions.Catalogs, catalogFolderName);

            Directory.CreateDirectory(catalogFolderName);

            if (Directory.Exists(attachementFolder))
            {
                var attachmentFile = Directory
                    .EnumerateFiles(attachementFolder, searchPattern, enumerationOptions)
                    .FirstOrDefault();

                if (attachmentFile is not null)
                {
                    attachment = File.OpenRead(attachmentFile);
                    return true;
                }
            }

            return false;
        }

        private const string CatalogMetadataFileName = "metadata.json";

        public bool TryReadCatalogMetadata(string catalogId, out Stream? stream)
        {
            var catalogFolderName = WebUtility.UrlDecode(catalogId);
            var filePath = Path.Combine(_pathsOptions.Catalogs, catalogFolderName, CatalogMetadataFileName);

            stream = null;

            if (File.Exists(filePath))
            {
                stream = File.OpenRead(filePath);
                return true;
            }

            return false;
        }

        public Stream WriteCatalogMetadata(string catalogId)
        {
            var physcialId = catalogId.TrimStart('/').Replace('/', '_');
            var folderPath = Path.Combine(_pathsOptions.Catalogs, physcialId);

            Directory.CreateDirectory(folderPath);

            var filePath = Path.Combine(folderPath, CatalogMetadataFileName);

            return File.Open(filePath, FileMode.Truncate, FileAccess.Write);
        }

        public Stream WriteExportFile(string fileName)
        {
            Directory.CreateDirectory(_pathsOptions.Export);

            var file = Path.Combine(_pathsOptions.Export, fileName);

            return File.OpenWrite(file);
        }
    }
}