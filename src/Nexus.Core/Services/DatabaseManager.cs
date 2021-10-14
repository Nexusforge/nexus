using Microsoft.Extensions.Options;
using Nexus.Core;
using System.IO;
using System.Linq;
using System.Net;

namespace Nexus.Services
{
    internal class DatabaseManager : IDatabaseManager
    {
        //data/dbconfig.json
        //data/catalogs/abc/attachements
        //data/catalogs/abc/meta
        //data/users/def/code
        //data/users/users.db
        //data/users/def/additional_extensions.json
        //data/cache/abc
        //data/export

        private PathsOptions _pathsOptions;

        public DatabaseManager(IOptions<PathsOptions> pathsOptions)
        {
            _pathsOptions = pathsOptions.Value;
        }



        //public void SaveCatalogMeta(CatalogProperties catalogMeta)
        //{
        //    var filePath = this.GetCatalogMetaPath(catalogMeta.Id);
        //    var jsonString = JsonSerializer.Serialize(catalogMeta, new JsonSerializerOptions() { WriteIndented = true });
        //    File.WriteAllText(filePath, jsonString);
        //}

        //public void SaveConfig(string folderPath, NexusDatabaseConfig config)
        //{
        //    var filePath = Path.Combine(folderPath, "dbconfig.json");
        //    var jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions() { WriteIndented = true });

        //    File.WriteAllText(filePath, jsonString);
        //}

        //private string GetCatalogMetaPath(string catalogName)
        //{
        //    return Path.Combine(_pathsOptions.Data, "META", $"{catalogName.TrimStart('/').Replace('/', '_')}.json");
        //}

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
            var physcialId = catalogId.TrimStart('/').Replace('/', '_');
            var filePath = Path.Combine(_pathsOptions.Catalogs, physcialId, CatalogMetadataFileName);

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