using Microsoft.Extensions.Options;
using Nexus.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace Nexus.Services
{
    internal class DatabaseManager : IDatabaseManager
    {
        // generated, small files:
        //
        // <application data>/config/project.json
        // <application data>/config/news.json
        // <application data>/config/filters.json
        // <application data>/config/users.db
        // <application data>/config/catalogs/abc.json
        // <application data>/config/users/def.json

        // use defined or potentially large files:
        //
        // <application data>/catalogs/abc
        // <application data>/users/def/code
        // <application data>/cache
        // <application data>/export
        // <user profile>/.nexus/packages

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

        public bool TryReadCatalogMetadata(string catalogId, out Stream? stream)
        {
            var catalogMetadataFileName = $"{WebUtility.UrlEncode(catalogId)}.json";
            var filePath = Path.Combine(_pathsOptions.Config, "catalogs", catalogMetadataFileName);

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
            var catalogMetadataFileName = $"{WebUtility.UrlEncode(catalogId)}.json";
            var folderPath = Path.Combine(_pathsOptions.Config, "catalogs");

            Directory.CreateDirectory(folderPath);

            var filePath = Path.Combine(folderPath, catalogMetadataFileName);

            return File.Open(filePath, FileMode.Truncate, FileAccess.Write);
        }

        public IEnumerable<string> EnumerateAttachements(string catalogId)
        {
            var catalogFolderName = WebUtility.UrlEncode(catalogId);
            var attachementFolder = Path.Combine(_pathsOptions.Catalogs, catalogFolderName);

            if (Directory.Exists(attachementFolder))
                return Directory.GetFiles(attachementFolder);

            else
                return Enumerable.Empty<string>();
        }

        public bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, out Stream attachment)
        {
            attachment = null;

            var catalogFolderName = WebUtility.UrlEncode(catalogId);
            var attachementFolder = Path.Combine(_pathsOptions.Catalogs, catalogFolderName);

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
            Directory.CreateDirectory(_pathsOptions.Export);

            var file = Path.Combine(_pathsOptions.Export, fileName);

            return File.OpenWrite(file);
        }
    }
}