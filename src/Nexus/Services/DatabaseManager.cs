using Microsoft.Extensions.Options;
using Nexus.Core;
using System.Diagnostics.CodeAnalysis;

namespace Nexus.Services
{
    internal interface IDatabaseManager
    {
        /* /config/catalogs/catalog_id.json */
        bool TryReadCatalogMetadata(string catalogId, [NotNullWhen(true)] out string? catalogMetadata);
        Stream WriteCatalogMetadata(string catalogId);

        /* /config/project.json */
        bool TryReadProject([NotNullWhen(true)] out string? project);
        Stream WriteProject();

        /* /catalogs/catalog_id/... */
        IEnumerable<string> EnumerateAttachements(string catalogId);
        bool TryReadAttachment(string catalogId, string attachmentId, [NotNullWhen(true)] out Stream? attachment);
        bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, [NotNullWhen(true)] out Stream? attachment);

        /* /artifacts */
        bool TryReadArtifact(string artifactId, [NotNullWhen(true)] out Stream? artifact);
        Stream WriteArtifact(string fileName);
    }

    internal class DatabaseManager : IDatabaseManager
    {
        // generated, small files:
        //
        // <application data>/config/catalogs/catalog_id.json
        // <application data>/config/project.json
        // <application data>/config/users.db

        // user defined or potentially large files:
        //
        // <application data>/catalogs/catalog_id/...
        // <application data>/users/user_name/...
        // <application data>/cache
        // <application data>/export
        // <user profile>/.nexus/packages

        private PathsOptions _pathsOptions;

        public DatabaseManager(IOptions<PathsOptions> pathsOptions)
        {
            _pathsOptions = pathsOptions.Value;
        }

        /* /config/catalogs/catalog_id.json */
        public bool TryReadCatalogMetadata(string catalogId, [NotNullWhen(true)] out string? catalogMetadata)
        {
            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var catalogMetadataFileName = $"{physicalId}.json";
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
            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var catalogMetadataFileName = $"{physicalId}.json";
            var folderPath = Path.Combine(_pathsOptions.Config, "catalogs");

            Directory.CreateDirectory(folderPath);

            var filePath = Path.Combine(folderPath, catalogMetadataFileName);

            return File.Open(filePath, FileMode.Create, FileAccess.Write);
        }

        /* /config/project.json */
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

        public Stream WriteProject()
        {
            Directory.CreateDirectory(_pathsOptions.Config);

            var filePath = Path.Combine(_pathsOptions.Config, "project.json");

            return File.Open(filePath, FileMode.Truncate, FileAccess.Write);
        }

        /* /catalogs/catalog_id/... */
        public IEnumerable<string> EnumerateAttachements(string catalogId)
        {
            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var attachementFolder = Path.Combine(_pathsOptions.Catalogs, physicalId);

            if (Directory.Exists(attachementFolder))
                return Directory.EnumerateFiles(attachementFolder);

            else
                return Enumerable.Empty<string>();
        }

        public bool TryReadAttachment(string catalogId, string attachmentId, [NotNullWhen(true)] out Stream? attachment)
        {
            attachment = null;

            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var attachementFolder = Path.Combine(_pathsOptions.Catalogs, physicalId);

            if (Directory.Exists(attachementFolder))
            {
                var attachmentFile = Path.Combine(attachementFolder, attachmentId);

                if (File.Exists(attachmentFile))
                {
                    attachment = File.OpenRead(attachmentFile);
                    return true;
                }
            }

            return false;
        }

        public bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, [NotNullWhen(true)] out Stream? attachment)
        {
            attachment = null;

            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var attachementFolder = Path.Combine(_pathsOptions.Catalogs, physicalId);

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

        /* /artifact */
        public bool TryReadArtifact(string artifactId, [NotNullWhen(true)] out Stream? artifact)
        {
            artifact = null;

            var artifactFolder = Path.Combine(_pathsOptions.Catalogs, artifactId);

            if (Directory.Exists(artifactFolder))
            {
                var attachmentFile = Path.Combine(artifactFolder, artifactId);

                if (File.Exists(attachmentFile))
                {
                    artifact = File.OpenRead(attachmentFile);
                    return true;
                }
            }

            return false;
        }

        public Stream WriteArtifact(string fileName)
        {
            Directory.CreateDirectory(_pathsOptions.Artifacts);

            var filePath = Path.Combine(_pathsOptions.Artifacts, fileName);

            return File.Open(filePath, FileMode.Create, FileAccess.Write);
        }
    }
}