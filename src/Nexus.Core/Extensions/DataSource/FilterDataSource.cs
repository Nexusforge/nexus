﻿using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Filters;
using Nexus.Infrastructure;
using Nexus.Roslyn;
using Nexus.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    [ExtensionIdentification(FilterDataSource.Id, "Nexus filters", "Dynamically loads and compiles user-defined filters.")]
    public class FilterDataSource : IDataSource
    {
        #region Fields

        public const string Id = "Nexus.Filters";

        private List<Catalog> _catalogs;

        private Uri _resourceLocator { get; set; }

        private List<FilterDataSourceCacheEntry> _cacheEntries;

        #endregion

        #region Constructors

        static FilterDataSource()
        {
            FilterDataSource.FilterSettingsCache = new ConcurrentDictionary<Uri, FilterSettings>();
            FilterDataSource.FilterDataSourceCache = new ConcurrentDictionary<Uri, List<FilterDataSourceCacheEntry>>();
        }

        #endregion

        #region Properties

        public INexusDatabase Database { get; set; }

        public Func<string, bool> IsCatalogAccessible { get; set; }

        public Func<BackendSource, Task<DataSourceController>> GetDataSourceAsync { get; set; }

        private DataSourceContext Context { get; set; }

        private string Root { get; set; }

        private static ConcurrentDictionary<Uri, FilterSettings> FilterSettingsCache { get; }

        private static ConcurrentDictionary<Uri, List<FilterDataSourceCacheEntry>> FilterDataSourceCache { get; }

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

            _cacheEntries = FilterDataSource.FilterDataSourceCache.GetOrAdd(context.ResourceLocator, new List<FilterDataSourceCacheEntry>());

            return Task.CompletedTask;
        }

        public static bool TryGetFilterCodeDefinition(DatasetRecord datasetRecord, out CodeDefinition codeDefinition)
        {
            codeDefinition = default;

            if (FilterDataSource.FilterDataSourceCache.TryGetValue(datasetRecord.Dataset.BackendSource.ResourceLocator, out var cacheEntries))
            {
                var cacheEntry = cacheEntries
                    .FirstOrDefault(entry => entry.SupportedChanneIds.Contains(datasetRecord.Channel.Id));

                if (cacheEntry is not null)
                {
                    codeDefinition = cacheEntry.FilterCodeDefinition;
                    return true;
                }
            }

            return false;
        }

        public static void ClearCache()
        {
            FilterDataSource.FilterSettingsCache.Clear();

            // unload DLLs
            var loadContexts = FilterDataSource.FilterDataSourceCache
                .SelectMany(entry => entry.Value.Select(cacheEntry => cacheEntry.LoadContext))
                .ToList();

            FilterDataSource.FilterDataSourceCache.Clear();

            foreach (var loadContext in loadContexts)
            {
                loadContext.Unload();
            }
        }

        public Task<List<Catalog>> GetCatalogsAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var catalogs = new Dictionary<string, Catalog>();

                if (this.TryGetFilterSettings(out var filterSettings))
                {
                    this.PopulateCache(filterSettings);
                    var filterCodeDefinitions = filterSettings.CodeDefinitions.Where(filter => filter.CodeType == CodeType.Filter);

                    foreach (var filterCodeDefinition in filterCodeDefinitions)
                    {
                        var cacheEntry = _cacheEntries.FirstOrDefault(current => current.FilterCodeDefinition == filterCodeDefinition);

                        if (cacheEntry is null)
                            continue;

                        var filterProvider = cacheEntry.FilterProvider;
                        var filterChannels = filterProvider.Filters;

                        foreach (var filterChannel in filterChannels)
                        {
                            var localFilterChannel = filterChannel;

                            // enforce group
                            if (localFilterChannel.CatalogId == FilterConstants.SharedCatalogID)
                            {
                                localFilterChannel = localFilterChannel with
                                {
                                    Group = filterCodeDefinition.Owner.Split('@')[0]
                                };
                            }
                            else if (string.IsNullOrWhiteSpace(localFilterChannel.Group))
                            {
                                localFilterChannel = localFilterChannel with
                                {
                                    Group = "General"
                                };
                            }

                            // create datasets
                            var datasets = new List<Dataset>()
                            {
                                new Dataset()
                                {
                                    Id = filterCodeDefinition.SampleRate,
                                    DataType = NexusDataType.FLOAT64
                                }
                            };

                            // create channel
                            if (!NexusCoreUtilities.CheckNamingConvention(localFilterChannel.ChannelName, out var message))
                            {
                                this.Context.Logger.LogWarning($"Skipping channel '{localFilterChannel.ChannelName}' due to the following reason: {message}.");
                                continue;
                            }

                            var channel = new Channel()
                            {
                                Id = localFilterChannel.ToGuid(cacheEntry.FilterCodeDefinition),
                                Name = localFilterChannel.ChannelName,
                                Group = localFilterChannel.Group,
                                Unit = localFilterChannel.Unit,
                                Datasets = datasets,
                            };

                            channel.Metadata["Description"] = localFilterChannel.Description;

                            // get or create catalog
                            if (!catalogs.TryGetValue(localFilterChannel.CatalogId, out var catalog))
                            {
                                catalog = new Catalog() { Id = localFilterChannel.CatalogId };
                                catalogs[localFilterChannel.CatalogId] = catalog;
                            }

                            catalog.Channels.Add(channel);
                        }
                    }
                }

                _catalogs = catalogs.Values.ToList();
                return _catalogs;
            });
        }

        public Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            return Task.FromResult((DateTime.MaxValue, DateTime.MinValue));
        }

        public Task<double> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            return Task.FromResult(1.0);
        }

        public Task ReadAsync2(DateTime begin, DateTime end, ReadRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var counter = 0.0;

                foreach (var (datasetPath, data, status) in requests)
                {
                    var (catalog, channel, dataset) = Catalog.Find(datasetPath, _catalogs);
                    var cacheEntry = _cacheEntries.FirstOrDefault(current => current.SupportedChanneIds.Contains(channel.Id));


                }

                return Task.CompletedTask;
            });
        }

        public Task ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var counter = 0.0;

                foreach (var (datasetPath, data, status) in requests)
                {
                    var (catalog, channel, dataset) = Catalog.Find(datasetPath, _catalogs);
                    var cacheEntry = _cacheEntries.FirstOrDefault(current => current.SupportedChanneIds.Contains(channel.Id));

                    if (cacheEntry is null)
                        throw new Exception("The requested filter channel ID could not be found.");

                    // fill database
                    GetFilterData getData = (string catalogId, string channelId, string datasetId, DateTime begin, DateTime end) =>
                    {
#warning improve this (PhysicalName)
                        var catalog = this.Database.CatalogContainers
                             .FirstOrDefault(container => container.Id == catalogId || container.PhysicalName == catalogId);

                        if (catalog == null)
                            throw new Exception($"Unable to find catalog with id '{catalogId}'.");

                        var datasetRecord = this.Database.Find(catalog.Id, channelId, datasetId);

                        if (!this.IsCatalogAccessible(catalog.Id))
                            throw new UnauthorizedAccessException("The current user is not allowed to access this filter.");

#warning GetData Should be Async! Deadlock may happen
                        var dataSourceController = this.GetDataSourceAsync(dataset.BackendSource).Result;

#warning GetData Should be Async! Deadlock may happen
                        var progress = new Progress<double>();
                        var request = new ReadRequest(datasetRecord.GetPath(), data, status);
                        dataSourceController.DataSource.ReadAsync(begin, end, new ReadRequest[] { request }, progress, cancellationToken).Wait();
                        var doubleData = new double[status.Length];
                        BufferUtilities.ApplyDatasetStatusByDataType(dataset.DataType, data, status, doubleData);

                        return doubleData;
                    };

                    // execute
                    var filter = cacheEntry.FilterProvider.Filters.First(filter => filter.ToGuid(cacheEntry.FilterCodeDefinition) == channel.Id);
                    var filterResult = MemoryMarshal.Cast<byte, double>(data.Span);

                    cacheEntry.FilterProvider.Filter(begin, end, filter, getData, filterResult);
                    status.Span.Fill(1);

                    progress.Report(++counter / requests.Length);
                }
            });
        }

        private bool TryGetFilterSettings(out FilterSettings filterSettings)
        {
            // search in cache
            if (FilterDataSource.FilterSettingsCache.TryGetValue(this.Context.ResourceLocator, out filterSettings))
            {
                return true;   
            }
            else
            {
                var filePath = Path.Combine(this.Root, "filters.json");

                // read from disk
                if (File.Exists(filePath))
                {
                    var jsonString = File.ReadAllText(filePath);
                    filterSettings = JsonSerializer.Deserialize<FilterSettings>(jsonString);

                    // add to cache
                    var filterSettings2 = filterSettings; // to make compiler happy
                    FilterDataSource.FilterSettingsCache.AddOrUpdate(this.Context.ResourceLocator, filterSettings, (key, value) => filterSettings2);

                    return true;
                }
            }

            return false;
        }

        private void PopulateCache(FilterSettings filterSettings)
        {
            var filterCodeDefinitions = filterSettings.CodeDefinitions
                .Where(filterSetting => filterSetting.CodeType == CodeType.Filter && filterSetting.IsEnabled)
                .ToList();

            var message = $"Compiling {filterCodeDefinitions.Count} filter(s) ...";
            this.Context.Logger.LogInformation(message);

            var cacheEntries = new FilterDataSourceCacheEntry[filterCodeDefinitions.Count];

            Parallel.For(0, filterCodeDefinitions.Count, i =>
            {
                var filterCodeDefinition = filterCodeDefinitions[i];
                filterCodeDefinition.Code = this.PrepareCode(filterCodeDefinition.Code);

                var additionalCodeFiles = filterSettings.GetSharedFiles(filterCodeDefinition.Owner)
                    .Select(codeDefinition => codeDefinition.Code)
                    .ToList();

                var roslynCatalog = new RoslynProject(filterCodeDefinition, additionalCodeFiles);
                using var peStream = new MemoryStream();

                var emitResult = roslynCatalog.Workspace.CurrentSolution.Projects.First()
                    .GetCompilationAsync().Result
                    .Emit(peStream);

                if (!emitResult.Success)
                    return;

                peStream.Seek(0, SeekOrigin.Begin);

                var loadContext = new FilterDataSourceLoadContext();
                var assembly = loadContext.LoadFromStream(peStream);
                var assemblyType = assembly.GetType();

                // filter method info
                var interfaceType = typeof(FilterProviderBase);
                var filterType = assembly
                    .GetTypes()
                    .FirstOrDefault(type => interfaceType.IsAssignableFrom(type));

                if (filterType is null)
                    return;

                try
                {
                    var filterProvider = (FilterProviderBase)Activator.CreateInstance(filterType);
                    var supportedChanneIds = filterProvider.Filters.Select(filter => filter.ToGuid(filterCodeDefinition)).ToList();
                    cacheEntries[i] = new FilterDataSourceCacheEntry(filterCodeDefinition, loadContext, filterProvider, supportedChanneIds);
                }
                catch (Exception ex)
                {
                    this.Context.Logger.LogError($"Failed to instantiate the filter provider '{filterCodeDefinition.Name}' of user {filterCodeDefinition.Owner}. Detailed error: {ex.GetFullMessage()}");
                }
            });

            _cacheEntries.AddRange(cacheEntries.Where(cacheEntry => cacheEntry is not null));
            this.Context.Logger.LogInformation($"{message} Done.");
        }

        private string PrepareCode(string code)
        {
            // change signature
            code = code.Replace(
                "DataProvider dataProvider",
                "GetFilterData getData");

            // matches strings like "= dataProvider.IN_MEMORY_TEST_ACCESSIBLE.T1.DATASET_1_s_mean;"
            var pattern1 = @"=\s*dataProvider\.([a-zA-Z_0-9]+)\.([a-zA-Z_0-9]+)\.DATASET_([a-zA-Z_0-9]+);";
            code = Regex.Replace(code, pattern1, match =>
            {
                var catalogId = match.Groups[1].Value;
                var channelId = match.Groups[2].Value;

#warning: Whenever the space in the dataset name is removed, update this code
                var regex = new Regex("_");
                var datasetId = regex.Replace(match.Groups[3].Value, " ", 1);

                return $"= getData(\"{catalogId}\", \"{channelId}\", \"{datasetId}\", begin, end);";
            });

            // matches strings like "= dataProvider.Read(campaignId, channelId, datasetId, begin, end);"
            var pattern3 = @"=\s*dataProvider\.Read\((.*?),(.*?),(.*?),(.*?),(.*?)\);";
            code = Regex.Replace(code, pattern3, match =>
            {
                var catalogId = match.Groups[1].Value;
                var channelId = match.Groups[2].Value;
                var datasetId = match.Groups[3].Value;
                var begin = match.Groups[4].Value;
                var end = match.Groups[5].Value;

                return $"= getData({catalogId}, {channelId}, {datasetId}, {begin}, {end});";
            });

            // matches strings like "= dataProvider.Read(campaignId, channelId, datasetId);"
            var pattern2 = @"=\s*dataProvider\.Read\((.*?),(.*?),(.*?)\);";
            code = Regex.Replace(code, pattern2, match =>
            {
                var catalogId = match.Groups[1].Value;
                var channelId = match.Groups[2].Value;
                var datasetId = match.Groups[3].Value;

                return $"= getData({catalogId}, {channelId}, {datasetId}, begin, end);";
            });

            return code;
        }

        #endregion
    }
}
