using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Infrastructure;
using Nexus.Services;
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
        #region Properties

        public IFileAccessManager FileAccessManager { get; set; }

        public Uri ResourceLocator
        {
            set
            {
                if (!value.IsAbsoluteUri || value.IsFile)
                {
                    this.Root = value.IsAbsoluteUri
                        ? value.AbsolutePath
                        : value.ToString();
                }
                else
                {
                    throw new Exception("Only file URIs are supported.");
                }
            }
        }

        public ILogger Logger { get; set; }

        public Dictionary<string, string> Parameters { get; set; }

        private string Root { get; set; }

        #endregion

        #region Methods

        public Task<List<Catalog>> GetCatalogsAsync(CancellationToken cancellationToken)
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

                    List<Catalog> cache;

                    // (5.a) cache file exists
                    if (File.Exists(cacheFilePath))
                    {
                        cache = JsonSerializerHelper.Deserialize<List<Catalog>>(cacheFilePath);
                        cache.ForEach(catalog => catalog.Initialize());

                        foreach (var catalogId in catalogIds)
                        {
                            var catalog = cache.FirstOrDefault(catalog => catalog.Id == catalogId);
                            var currentMonthFolder = Path.Combine(dataFolderPath, WebUtility.UrlEncode(catalogId), currentMonth.ToString("yyyy-MM"));

                            // catalog is in cache ...
                            if (catalog != null)
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
                List<Catalog> catalogs;

                if (cacheChanged || !File.Exists(mainCacheFilePath))
                {
                    var cacheFiles = Directory.EnumerateFiles(cacheFolderPath, "*-*.json");
                    catalogs = new List<Catalog>();

                    var message = "Merging cache files into main cache ...";

                    try
                    {
                        this.Logger.LogInformation(message);

                        foreach (var cacheFile in cacheFiles)
                        {
                            var cache = JsonSerializerHelper.Deserialize<List<Catalog>>(cacheFile);
                            cache.ForEach(catalog => catalog.Initialize());

                            foreach (var catalog in cache)
                            {
                                var reference = catalogs.FirstOrDefault(current => current.Id == catalog.Id);

                                if (reference != null)
                                    reference.Merge(catalog, ChannelMergeMode.NewWins);
                                else
                                    catalogs.Add(catalog);
                            }
                        }

                        this.Logger.LogInformation($"{message} Done.");
                    }
                    catch (Exception ex)
                    {
                        this.Logger.LogError($"{message} Error: {ex.GetFullMessage()}");
                        throw;
                    }

                    JsonSerializerHelper.Serialize(catalogs, mainCacheFilePath);
                }
                else
                {
                    catalogs = JsonSerializerHelper.Deserialize<List<Catalog>>(mainCacheFilePath);
                    catalogs.ForEach(catalog => catalog.Initialize());
                }

                return catalogs;
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

        public Task ReadSingleAsync(Dataset dataset, ReadResult result, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var catalog = dataset.Channel.Catalog;
                var channel = dataset.Channel;
                var catalogFolderPath = Path.Combine(this.Root, "DATA", WebUtility.UrlEncode(catalog.Id));
                var samplesPerDay = new SampleRateContainer(dataset.Id).SamplesPerDay;

                if (!Directory.Exists(catalogFolderPath))
                    return Task.CompletedTask;

                var periodPerFile = TimeSpan.FromDays(1);

                // read data
                var currentBegin = begin.RoundDown(periodPerFile);
                var fileLength = (int)Math.Round(periodPerFile.TotalDays * samplesPerDay, MidpointRounding.AwayFromZero);
                var fileOffset = (int)Math.Round((begin - currentBegin).TotalDays * samplesPerDay, MidpointRounding.AwayFromZero);
                var bufferOffset = 0;
                var remainingBufferLength = result.Data.Length / dataset.ElementSize;

                while (remainingBufferLength > 0)
                {
                    var filePath = Path.Combine(
                        catalogFolderPath,
                        currentBegin.ToString("yyyy-MM"),
                        currentBegin.ToString("dd"),
                        $"{channel.Id}_{dataset.Id.Replace(" ", "_")}.nex");

                    var fileBlock = fileLength - fileOffset;
                    var currentBlock = Math.Min(remainingBufferLength, fileBlock);

                    if (File.Exists(filePath))
                    {
                        try
                        {
                            this.FileAccessManager?.Register(filePath, CancellationToken.None);
                            var aggregationData = AggregationFile.Read<byte>(filePath);

                            // write data
                            if (aggregationData.Length == fileLength * dataset.ElementSize)
                            {
                                aggregationData
                                    .Slice(fileOffset * dataset.ElementSize, currentBlock * dataset.ElementSize)
                                    .CopyTo(result.Data.Span.Slice(bufferOffset * dataset.ElementSize));

                                result.Status.Span
                                    .Slice(bufferOffset, currentBlock)
                                    .Fill(1);
                            }
                        }
                        catch (Exception ex)
                        {
                            this.Logger.LogWarning($"Could not process file '{filePath}'. Reason: {ex.Message}");
                        }
                        finally
                        {
                            this.FileAccessManager?.Unregister(filePath);
                        }
                    }

                    // update loop state
                    fileOffset = 0; // Only the data in the first file may have an offset.
                    bufferOffset += currentBlock;
                    remainingBufferLength -= currentBlock;
                    currentBegin += periodPerFile;
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

        private Catalog ScanFiles(string catalogId, string currentMonthFolder, AggregationVersioning versioning)
        {
            var catalog = new Catalog(catalogId);

            if (Directory.Exists(currentMonthFolder))
            {
                var message = $"Scanning files for {Path.GetFileName(currentMonthFolder)} ...";
                this.Logger.LogInformation(message);

                var dayFolders = Directory.EnumerateDirectories(currentMonthFolder);

                try
                {
                    foreach (var dayFolder in dayFolders)
                    {
                        var newCatalog = this.GetCatalog(catalogId, dayFolder);
                        catalog.Merge(newCatalog, ChannelMergeMode.NewWins);
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

                    this.Logger.LogInformation($"{message} Done.");
                }
                catch (Exception ex)
                {
                    this.Logger.LogError($"{message} Error: {ex.GetFullMessage()}");
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

        private Catalog GetCatalog(string catalogId, string dayFolder)
        {
            var catalog = new Catalog(catalogId);
            var channelMap = new Dictionary<Guid, Channel>();

            Directory
                .EnumerateFiles(dayFolder, "*.nex", SearchOption.TopDirectoryOnly)
                .ToList()
                .ForEach(filePath =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    var fileNameParts = fileName.Split('_');
                    var id = Guid.Parse(fileNameParts[0]);
                    var datasetName = $"{fileNameParts[1]} {fileNameParts[2]}_{fileNameParts[3]}";

                    if (!channelMap.TryGetValue(id, out var channel))
                    {
                        channel = new Channel(id, catalog);
                        channelMap[id] = channel;
                    }

                    var dataset = new Dataset(datasetName, channel)
                    {
                        DataType = NexusDataType.FLOAT64
                    };

                    channel.Datasets.Add(dataset);
                });

            catalog.Channels.AddRange(channelMap.Values.ToList());

            return catalog;
        }

        #endregion
    }
}
