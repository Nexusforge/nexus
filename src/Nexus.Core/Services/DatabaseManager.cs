using Microsoft.Extensions.Options;
using Nexus.Core;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        // <application data>/config/users.db
        // <application data>/config/catalogs/abc.json
        // <application data>/config/users/def.json

        // user defined or potentially large files:
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

        public bool TryReadProject([NotNullWhen(true)] out string? project)
        {
            var filePath = Path.Combine(_pathsOptions.Config, "project.json");
            project = null;

            if (File.Exists(filePath))
            {
                project = File.ReadAllText(filePath);
                return true;
            }

            return false;
        }

        public bool TryReadNews([NotNullWhen(true)] out string? news)
        {
            var filePath = Path.Combine(_pathsOptions.Config, "news.json");
            news = null;

            if (File.Exists(filePath))
            {
                news = File.ReadAllText(filePath);
                return true;
            }

            return false;
        }

        public bool TryReadCatalogMetadata(string catalogId, [NotNullWhen(true)] out string? catalogMetadata)
        {
            var catalogMetadataFileName = $"{WebUtility.UrlEncode(catalogId)}.json";
            var filePath = Path.Combine(_pathsOptions.Config, "catalogs", catalogMetadataFileName);

            catalogMetadata = null;

            if (File.Exists(filePath))
            {
                catalogMetadata = File.ReadAllText(filePath);
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

        public bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, [NotNullWhen(true)] out Stream? attachment)
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