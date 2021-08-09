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

        public Stream WriteExportFile(string fileName)
        {
            var exportFolder = Path.Combine(_pathsOptions.Export, "export");
            Directory.CreateDirectory(exportFolder);

            var file = Path.Combine(exportFolder, fileName);

            return File.OpenWrite(file);
        }
    }
}