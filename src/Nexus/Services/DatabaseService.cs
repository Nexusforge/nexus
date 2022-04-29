﻿using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using System.Diagnostics.CodeAnalysis;

namespace Nexus.Services
{
    internal interface IDatabaseService
    {
        /* /config/catalogs/catalog_id.json */
        bool TryReadCatalogMetadata(string catalogId, [NotNullWhen(true)] out string? catalogMetadata);
        Stream WriteCatalogMetadata(string catalogId);

        /* /config/project.json */
        bool TryReadProject([NotNullWhen(true)] out string? project);
        Stream WriteProject();

        /* /catalogs/catalog_id/... */
        IEnumerable<string> EnumerateAttachments(string catalogId);
        bool TryReadAttachment(string catalogId, string attachmentId, [NotNullWhen(true)] out Stream? attachment);
        bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, [NotNullWhen(true)] out Stream? attachment);

        /* /artifacts */
        bool TryReadArtifact(string artifactId, [NotNullWhen(true)] out Stream? artifact);
        Stream WriteArtifact(string fileName);

        /* /cache */
        bool TryReadCacheEntry(CatalogItem catalogItem, DateTime begin, [NotNullWhen(true)] out Stream? cacheEntry);
        bool TryWriteCacheEntry(CatalogItem catalogItem, DateTime begin, [NotNullWhen(true)] out Stream? cacheEntry);
        Task ClearCacheEntriesAsync(string catalogId, DateOnly day, TimeSpan timeout, Predicate<string> predicate);
    }

    internal class DatabaseService : IDatabaseService
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

        public DatabaseService(IOptions<PathsOptions> pathsOptions)
        {
            _pathsOptions = pathsOptions.Value;
        }

        /* /config/catalogs/catalog_id.json */
        public bool TryReadCatalogMetadata(string catalogId, [NotNullWhen(true)] out string? catalogMetadata)
        {
            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var catalogMetadataFileName = $"{physicalId}.json";
            var filePath = SafePathCombine(_pathsOptions.Config, Path.Combine("catalogs", catalogMetadataFileName));

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

            var filePath = SafePathCombine(folderPath, catalogMetadataFileName);

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
        public IEnumerable<string> EnumerateAttachments(string catalogId)
        {
            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var attachmentFolder = SafePathCombine(_pathsOptions.Catalogs, physicalId);

            if (Directory.Exists(attachmentFolder))
                return Directory
                    .EnumerateFiles(attachmentFolder, "*", SearchOption.AllDirectories)
                    .Select(attachmentFilePath => attachmentFilePath.Substring(attachmentFolder.Length + 1));

            else
                return Enumerable.Empty<string>();
        }

        public bool TryReadAttachment(string catalogId, string attachmentId, [NotNullWhen(true)] out Stream? attachment)
        {
            attachment = null;

            var physicalId = catalogId.TrimStart('/').Replace("/", "_");
            var attachmentFolder = Path.Combine(_pathsOptions.Catalogs, physicalId);

            if (Directory.Exists(attachmentFolder))
            {
                var attachmentFile = SafePathCombine(attachmentFolder, attachmentId);

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
            var attachmentFolder = SafePathCombine(_pathsOptions.Catalogs, physicalId);

            if (Directory.Exists(attachmentFolder))
            {
                var attachmentFile = Directory
                    .EnumerateFiles(attachmentFolder, searchPattern, enumerationOptions)
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

            var attachmentFile = SafePathCombine(_pathsOptions.Artifacts, artifactId);

            if (File.Exists(attachmentFile))
            {
                artifact = File.OpenRead(attachmentFile);
                return true;
            }

            return false;
        }

        public Stream WriteArtifact(string fileName)
        {
            Directory.CreateDirectory(_pathsOptions.Artifacts);

            var filePath = Path.Combine(_pathsOptions.Artifacts, fileName);

            return File.Open(filePath, FileMode.Create, FileAccess.Write);
        }

        /* /cache */
        private string GetCacheEntryDirectoryPath(string catalogId, DateOnly day)
            => $"{catalogId.TrimStart('/').Replace("/", "_")}/{day.ToString("yyyy-MM")}/{day.ToString("dd")}";

        private string GetCacheEntryId(CatalogItem catalogItem, DateTime begin)
            => $"{GetCacheEntryDirectoryPath(catalogItem.Catalog.Id, DateOnly.FromDateTime(begin))}/{begin.ToString("yyyy-MM-ddTHH-mm-ss-fffffff")}_{catalogItem.Resource.Id}_{catalogItem.Representation.Id}.cache";

        public bool TryReadCacheEntry(CatalogItem catalogItem, DateTime begin, [NotNullWhen(true)] out Stream? cacheEntry)
        {
            cacheEntry = null;

            var cacheEntryFilePath = Path.Combine(_pathsOptions.Cache, GetCacheEntryId(catalogItem, begin));

            try
            {
                cacheEntry = File.Open(cacheEntryFilePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;

            }
            catch
            {
                return false;
            }
        }

        public bool TryWriteCacheEntry(CatalogItem catalogItem, DateTime begin, [NotNullWhen(true)] out Stream? cacheEntry)
        {
            cacheEntry = null;

            var cacheEntryFilePath = Path.Combine(_pathsOptions.Cache, GetCacheEntryId(catalogItem, begin));
            var cacheEntryDirectoryPath = Path.GetDirectoryName(cacheEntryFilePath);

            if (cacheEntryDirectoryPath is null)
                return false;

            Directory.CreateDirectory(cacheEntryDirectoryPath);

            try
            {
                cacheEntry = File.Open(cacheEntryFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;

            }
            catch
            {
                return false;
            }
        }

        public async Task ClearCacheEntriesAsync(string catalogId, DateOnly day, TimeSpan timeout, Predicate<string> predicate)
        {
            var cacheEntryDirectoryPath = GetCacheEntryDirectoryPath(catalogId, day);

            if (Directory.Exists(cacheEntryDirectoryPath))
            {
                var deleteTasks = new List<Task>();

                foreach (var cacheEntry in Directory.EnumerateFiles(cacheEntryDirectoryPath))
                {
                    /* if file should be deleted */
                    if (predicate(cacheEntry))
                    {
                        /* try direct delete */
                        try
                        {
                            File.Delete(cacheEntry);
                        }

                        /* otherwise try asynchronously for a minute */
                        catch (IOException)
                        {
                            deleteTasks.Add(DeleteCacheEntryAsync(cacheEntry, timeout));
                        }
                    }
                }

                await Task.WhenAll(deleteTasks);
            }
        }

        private async Task DeleteCacheEntryAsync(string cacheEntry, TimeSpan timeout)
        {
            var end = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < end)
            {
                try
                {
                    File.Delete(cacheEntry);
                    break;
                }
                catch (IOException)
                {
                    // file is still in use
                }

                await Task.Delay(5);
            }

            if (File.Exists(cacheEntry))
                throw new Exception($"Cannot delete cache entry {cacheEntry}.");
        }

        private string SafePathCombine(string basePath, string relativePath)
        {
            var filePath = Path.GetFullPath(Path.Combine(basePath, relativePath));

            if (!filePath.StartsWith(basePath))
                throw new Exception("Invalid path.");

            return filePath;
        }
    }
}