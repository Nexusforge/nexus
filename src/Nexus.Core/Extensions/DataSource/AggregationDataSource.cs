using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    [ExtensionIdentification("Nexus.Aggregation", "Nexus Aggregation", "Provides access to databases with Nexus aggregation files.")]
    public class AggregationDataSource : IDataSource
    {
        #region Fields

        private ResourceCatalog[] _catalogs;

        #endregion

        #region Properties

        public IFileAccessManager FileAccessManager { get; set; }

        private DataSourceContext Context { get; set; }

        private string Root { get; set; }

        #endregion

        #region Methods

        public Task SetContextAsync(DataSourceContext context, CancellationToken cancellationToken)
        {
            this.Context = context;

            var uri = context.ResourceLocator;

            if (!uri.IsAbsoluteUri || uri.IsFile)
            {
                this.Root = uri.IsAbsoluteUri
                    ? uri.AbsolutePath
                    : uri.ToString();
            }
            else
            {
                throw new Exception("Only file URIs are supported.");
            }

            return Task.CompletedTask;
        }

        public Task<ResourceCatalog[]> GetCatalogsAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                // (0) load versioning file
                var versioningFilePath = Path.Combine(this.Root, "versioning.json");

                var versioning = File.Exists(versioningFilePath)
                    ? AggregationVersioning.Load(versioningFilePath)
                    : new AggregationVersioning();

                // (1) find beginning of database
                var dataFolderPath = Path.Combine(this.Root, "DATA");
                Directory.CreateDirectory(dataFolderPath);

                var firstMonth = DateTime.MaxValue;

                foreach (var catalogDirectory in Directory.EnumerateDirectories(dataFolderPath))
                {
                    var catalogFirstMonth = this.GetCatalogFirstMonthWithData(catalogDirectory);

                    if (catalogFirstMonth != DateTime.MinValue && catalogFirstMonth < firstMonth)
                        firstMonth = catalogFirstMonth;
                }

                // (2) for each month
                var now = DateTime.UtcNow;
                var months = ((now.Year - firstMonth.Year) * 12) + now.Month - firstMonth.Month + 1;
                var currentMonth = firstMonth;

                var cacheFolderPath = Path.Combine(this.Root, "CACHE");
                var mainCacheFilePath = Path.Combine(cacheFolderPath, "main.json");
                Directory.CreateDirectory(cacheFolderPath);

                bool cacheChanged = false;

                for (int i = 0; i < months; i++)
                {
                    // (3) find available catalog ids
                    var catalogIds = Directory
                        .EnumerateDirectories(dataFolderPath)
                        .Select(current => WebUtility.UrlDecode(Path.GetFileName(current)))
                        .ToList();

                    // (4) find corresponding cache file
                    var cacheFilePath = Path.Combine(cacheFolderPath, $"{currentMonth.ToString("yyyy-MM")}.json");

                    List<ResourceCatalog> cache;

                    // (5.a) cache file exists
                    if (File.Exists(cacheFilePath))
                    {
                        cache = JsonSerializerHelper.Deserialize<List<ResourceCatalog>>(cacheFilePath);

                        foreach (var catalogId in catalogIds)
                        {
                            var catalog = cache.FirstOrDefault(catalog => catalog.Id == catalogId);
                            var currentMonthFolder = Path.Combine(dataFolderPath, WebUtility.UrlEncode(catalogId), currentMonth.ToString("yyyy-MM"));

                            // catalog is in cache ...
                            if (catalog != null && versioning.ScannedUntilMap.ContainsKey(catalogId))
                            {
                                // ... but cache is outdated
                                if (this.IsCacheOutdated(catalogId, currentMonthFolder, versioning))
                                {
                                    catalog = this.ScanFiles(catalogId, currentMonthFolder, versioning);
                                    cacheChanged = true;
                                }
                            }
                            // catalog is not in cache
                            else
                            {
                                catalog = this.ScanFiles(catalogId, currentMonthFolder, versioning);
                                cache.Add(catalog);
                                cacheChanged = true;
                            }
                        }
                    }
                    // (5.b) cache file does not exist
                    else
                    {
                        cache = catalogIds.Select(catalogId =>
                        {
                            var currentMonthFolder = Path.Combine(dataFolderPath, WebUtility.UrlEncode(catalogId), currentMonth.ToString("yyyy-MM"));
                            var catalog = this.ScanFiles(catalogId, currentMonthFolder, versioning);
                            cacheChanged = true;
                            return catalog;
                        }).ToList();
                    }

                    // (6) save cache and versioning files
                    if (cacheChanged)
                    {
                        JsonSerializerHelper.Serialize(cache, cacheFilePath);
                        JsonSerializerHelper.Serialize(versioning, versioningFilePath);
                    }

                    currentMonth = currentMonth.AddMonths(1);
                }

                // (7) update main cache
                List<ResourceCatalog> catalogs;

                if (cacheChanged || !File.Exists(mainCacheFilePath))
                {
                    var cacheFiles = Directory.EnumerateFiles(cacheFolderPath, "*-*.json");
                    catalogs = new List<ResourceCatalog>();

                    var message = "Merging cache files into main cache ...";

                    try
                    {
                        this.Context.Logger.LogInformation(message);

                        foreach (var cacheFile in cacheFiles)
                        {
                            var cache = JsonSerializerHelper.Deserialize<List<ResourceCatalog>>(cacheFile);

                            foreach (var catalog in cache)
                            {
                                var reference = catalogs.FirstOrDefault(current => current.Id == catalog.Id);

                                if (reference != null)
                                    reference.Merge(catalog, MergeMode.NewWins);
                                else
                                    catalogs.Add(catalog);
                            }
                        }

                        this.Context.Logger.LogInformation($"{message} Done.");
                    }
                    catch (Exception ex)
                    {
                        this.Context.Logger.LogError($"{message} Error: {ex.GetFullMessage()}");
                        throw;
                    }

                    JsonSerializerHelper.Serialize(catalogs, mainCacheFilePath);
                }
                else
                {
                    catalogs = JsonSerializerHelper.Deserialize<List<ResourceCatalog>>(mainCacheFilePath);
                }

                _catalogs = catalogs.ToArray();
                return _catalogs;
            });
        }

        public Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            var catalogFolderPath = Path.Combine(this.Root, "DATA", WebUtility.UrlEncode(catalogId));

            // first
            var catalogFirstMonth = this.GetCatalogFirstMonthWithData(catalogFolderPath);
            var currentMonthFolder = Path.Combine(catalogFolderPath, catalogFirstMonth.ToString("yyyy-MM"));
            var firstDateTime = this.GetFirstDateTime(Directory.EnumerateDirectories(currentMonthFolder));

            // last
            var catalogLastMonth = this.GetCatalogLastMonthWithData(catalogFolderPath);
            currentMonthFolder = Path.Combine(catalogFolderPath, catalogLastMonth.ToString("yyyy-MM"));
            var lastDateTime = this.GetLastDateTime(Directory.EnumerateDirectories(currentMonthFolder));

            return Task.FromResult((firstDateTime, lastDateTime));
        }

        public Task<double> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var count = 0;
                var result = 0.0;

                var currentBegin = begin;

                while (currentBegin < end)
                {
                    var folderPath = Path.Combine(
                        this.Root,
                        "DATA",
                        WebUtility.UrlEncode(catalogId),
                        currentBegin.ToString("yyyy-MM"),
                        currentBegin.ToString("dd")
                    );

                    result += Directory.Exists(folderPath)
                        ? 1
                        : 0;

                    count++;
                    currentBegin += TimeSpan.FromDays(1);
                }

                return result / count;
            });
        }

        public Task ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                var counter = 0.0;

                foreach (var (catalogItem, data, status) in requests)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var (catalog, resource, representation) = catalogItem;
                    var catalogFolderPath = Path.Combine(this.Root, "DATA", WebUtility.UrlEncode(catalog.Id));
                    var samplePeriod = representation.GetSamplePeriod();

                    if (!Directory.Exists(catalogFolderPath))
                        continue;

                    // read data
                    var filePeriod = TimeSpan.FromDays(1);
                    var fileLength = (int)(filePeriod.Ticks / samplePeriod.Ticks);
                    var bufferOffset = 0;

                    await NexusCoreUtilities.FileLoopAsync(begin, end, filePeriod,
                        (fileBegin, fileOffset, duration) =>
                    {
                        var filePath = Path.Combine(
                            catalogFolderPath,
                            fileBegin.ToString("yyyy-MM"),
                            fileBegin.ToString("dd"),
                            $"{resource.Id}_{representation.Id.Replace(" ", "_")}.nex");

                        var fileElementOffset = (int)(fileOffset.Ticks / samplePeriod.Ticks);
                        var elementCount = (int)(duration.Ticks / samplePeriod.Ticks);

                        if (File.Exists(filePath))
                        {
                            try
                            {
                                this.FileAccessManager?.Register(filePath, CancellationToken.None);
                                var aggregationData = AggregationFile.Read<byte>(filePath);

                                // write data
                                if (aggregationData.Length == fileLength * representation.ElementSize)
                                {
                                    aggregationData
                                        .Slice(fileElementOffset * representation.ElementSize, elementCount * representation.ElementSize)
                                        .CopyTo(data.Span.Slice(bufferOffset * representation.ElementSize));

                                    status.Span
                                        .Slice(bufferOffset, elementCount)
                                        .Fill(1);
                                }
                            }
                            catch (Exception ex)
                            {
                                this.Context.Logger.LogWarning($"Could not process file '{filePath}'. Reason: {ex.GetFullMessage()}");
                            }
                            finally
                            {
                                this.FileAccessManager?.Unregister(filePath);
                            }
                        }

                        bufferOffset += elementCount;

                        return Task.CompletedTask;
                    });             

                    progress.Report(++counter / requests.Length);
                }

                return Task.CompletedTask;
            });
        }

        private bool IsCacheOutdated(string catalogId, string monthFolder, AggregationVersioning versioning)
        {
            if (Directory.Exists(monthFolder))
            {
                var folders = Directory
               .EnumerateDirectories(monthFolder);

                var lastDateTime = this.GetLastDateTime(folders);
                return lastDateTime > versioning.ScannedUntilMap[catalogId];
            }
            else
            {
                return false;
            }
        }

        private ResourceCatalog ScanFiles(string catalogId, string currentMonthFolder, AggregationVersioning versioning)
        {
            var catalog = new ResourceCatalog() { Id = catalogId };

            if (Directory.Exists(currentMonthFolder))
            {
                var message = $"Scanning files for {Path.GetFileName(currentMonthFolder)} ...";
                this.Context.Logger.LogInformation(message);

                var dayFolders = Directory.EnumerateDirectories(currentMonthFolder);

                try
                {
                    foreach (var dayFolder in dayFolders)
                    {
                        var newCatalog = this.GetCatalog(catalogId, dayFolder);
                        catalog = catalog.Merge(newCatalog, MergeMode.NewWins);
                    }

                    // update scanned until
                    var scannedUntil = this.GetLastDateTime(dayFolders);

                    if (versioning.ScannedUntilMap.TryGetValue(catalogId, out var value))
                    {
                        if (scannedUntil > value)
                            versioning.ScannedUntilMap[catalogId] = scannedUntil;
                    }
                    else
                    {
                        versioning.ScannedUntilMap[catalogId] = scannedUntil;
                    }

                    this.Context.Logger.LogInformation($"{message} Done.");
                }
                catch (Exception ex)
                {
                    this.Context.Logger.LogError($"{message} Error: {ex.GetFullMessage()}");
                }
            }

            return catalog;
        }

        private DateTime GetCatalogFirstMonthWithData(string catalogFolderPath)
        {
            var monthFolders = Directory
                    .EnumerateDirectories(catalogFolderPath);

            if (monthFolders.Any())
            {
                var result = monthFolders
                    // convert path to date/time
                    .Select(monthDirectory =>
                    {
                        var dateTime = DateTime.ParseExact(
                            Path.GetFileName(monthDirectory),
                            "yyyy-MM",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
                        );

                        return (monthDirectory, dateTime);
                    })
                    // order by date/time
                    .OrderBy(value => value.dateTime)
                    // find first folder that contains files
                    .FirstOrDefault(value => Directory.EnumerateFiles(value.monthDirectory, "*", SearchOption.AllDirectories).Any())
                    // return only date/time
                    .dateTime;

                return result == default 
                    ? DateTime.MaxValue 
                    : result;
            }
            else
            {
                return DateTime.MaxValue;
            }
        }

        private DateTime GetCatalogLastMonthWithData(string catalogFolderPath)
        {
            var monthFolders = Directory
                    .EnumerateDirectories(catalogFolderPath);

            if (monthFolders.Any())
            {
                var result = monthFolders
                    // convert path to date/time
                    .Select(monthDirectory =>
                    {
                        var dateTime = DateTime.ParseExact(
                            Path.GetFileName(monthDirectory),
                            "yyyy-MM",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
                        );

                        return (monthDirectory, dateTime);
                    })
                    // order by date/time
                    .OrderBy(value => value.dateTime)
                    // find first folder that contains files
                    .LastOrDefault(value => Directory.EnumerateFiles(value.monthDirectory, "*", SearchOption.AllDirectories).Any())
                    // return only date/time
                    .dateTime;

                return result == default
                    ? DateTime.MinValue
                    : result;
            }
            else
            {
                return DateTime.MinValue;
            }
        }

        private DateTime GetFirstDateTime(IEnumerable<string> dayFolders)
        {
            if (dayFolders.Any())
            {
                return dayFolders
                   .Select(dayFolder => DateTime.ParseExact(
                       $"{dayFolder.Substring(dayFolder.Length - 10).Remove(7, 1).Insert(7, "-")}",
                       "yyyy-MM-dd",
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
                   )
                   .OrderBy(value => value)
                   .First();
            }
            else
            {
                return DateTime.MaxValue;
            }
        }

        private DateTime GetLastDateTime(IEnumerable<string> dayFolders)
        {
            if (dayFolders.Any())
            {
                return dayFolders
                   .Select(dayFolder => DateTime.ParseExact(
                       $"{dayFolder.Substring(dayFolder.Length - 10).Remove(7, 1).Insert(7, "-")}",
                       "yyyy-MM-dd",
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
                   )
                   .OrderBy(value => value)
                   .Last();
            }
            else
            {
                return DateTime.MinValue;
            }
        }

        private ResourceCatalog GetCatalog(string catalogId, string dayFolder)
        {
            var catalog = new ResourceCatalog() { Id = catalogId };
            var resourceMap = new Dictionary<Guid, Resource>();

            Directory
                .EnumerateFiles(dayFolder, "*.nex", SearchOption.TopDirectoryOnly)
                .ToList()
                .ForEach(filePath =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    var fileNameParts = fileName.Split('_');
                    var id = Guid.Parse(fileNameParts[0]);
                    var representationName = $"{fileNameParts[1]} {fileNameParts[2]}_{fileNameParts[3]}";

                    if (!resourceMap.TryGetValue(id, out var resource))
                    {
                        resource = new Resource() { Id = id };
                        resourceMap[id] = resource;
                    }

                    var representation = new Representation()
                    {
                        Id = representationName,
                        DataType = NexusDataType.FLOAT64
                    };

                    resource.Representations.Add(representation);
                });

            catalog.Resources.AddRange(resourceMap.Values.ToList());

            return catalog;
        }

        #endregion
    }
}
