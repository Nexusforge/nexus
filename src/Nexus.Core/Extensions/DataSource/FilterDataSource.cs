using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Filters;
using Nexus.Roslyn;
using Nexus.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    [ExtensionIdentification(FilterDataSource.Id, "Nexus filters", "Dynamically loads and compiles user-defined filters.")]
    internal class FilterDataSource : IDataSource
    {
        #region Fields

        public const string Id = "Nexus.Builtin.Filters";

        private ResourceCatalog[] _catalogs;

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

        public ILogger<DataSourceController> DataSourceControllerLogger { get; set; }

        public Func<CatalogCollection> GetCatalogCollection { get; set; }

        public Func<string, bool> IsCatalogAccessible { get; set; }

        public Func<BackendSource, Task<IDataSourceController>> GetDataSourceControllerAsync { get; set; }

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

        public static bool TryGetFilterCodeDefinition(string resourceId, BackendSource backendSource, out CodeDefinition codeDefinition)
        {
            codeDefinition = default;

            if (FilterDataSource.FilterDataSourceCache.TryGetValue(backendSource.ResourceLocator, out var cacheEntries))
            {
                var cacheEntry = cacheEntries
                    .FirstOrDefault(entry => entry.SupportedResourceIds.Contains(resourceId));

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

        public async Task<ResourceCatalog[]> GetCatalogsAsync(CancellationToken cancellationToken)
        {
            var catalogs = new Dictionary<string, ResourceCatalog>();

            if (this.TryGetFilterSettings(out var filterSettings))
            {
                await this.PopulateCacheAsync(filterSettings, cancellationToken);
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

                        // create representation
                        var representation = new Representation(
                            dataType: NexusDataType.FLOAT64,
                            samplePeriod: filterCodeDefinition.SamplePeriod);

                        // create resource
                        try
                        {
                            var resource = new ResourceBuilder(id: localFilterChannel.ResourceId)
                                .WithUnit(localFilterChannel.Unit)
                                .WithDescription(localFilterChannel.Description)
                                .WithGroups(localFilterChannel.Group)
                                .AddRepresentation(representation)
                                .Build();

                            // get or create catalog
                            if (!catalogs.TryGetValue(localFilterChannel.CatalogId, out var catalog))
                                catalog = new ResourceCatalogBuilder(id: localFilterChannel.CatalogId)
                                    .AddResource(resource)
                                    .Build();

                            else
                                catalog = catalog with { Resources = new List<Resource>() { resource } };

                            catalogs[localFilterChannel.CatalogId] = catalog;
                        }
                        catch (Exception ex)
                        {
                            this.Context.Logger.LogError(ex, "Skip creation of resource {resourceId}", localFilterChannel.ResourceId);
                        }
                    }
                }
            }

            _catalogs = catalogs.Values.ToArray();
            return _catalogs;
        }

        public Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            return Task.FromResult((DateTime.MaxValue, DateTime.MinValue));
        }

        public Task<double> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            return Task.FromResult(1.0);
        }

        public Task ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var counter = 0.0;
                var catalogCollection = this.GetCatalogCollection();

                foreach (var (catalogItem, data, status) in requests)
                {
                    var (catalog, resource, representation) = catalogItem;
                    var cacheEntry = _cacheEntries.FirstOrDefault(current => current.SupportedResourceIds.Contains(resource.Id));

                    if (cacheEntry is null)
                        throw new Exception($"The requested resource path {catalogItem.GetPath()} could not be found.");

                    // fill database
                    GetFilterData getData = (string catalogId, string resourceId, string representationId, DateTime begin, DateTime end) =>
                    {
#warning improve this (PhysicalName)
                        var catalog = catalogCollection.CatalogContainers
                            .FirstOrDefault(container => container.Id == catalogId || container.PhysicalName == catalogId);

                        if (catalog == null)
                            throw new Exception($"Unable to find catalog {catalogId}.");

                        var subCatalogItem = catalogCollection.Find(catalog.Id, resourceId, representationId);

                        if (!this.IsCatalogAccessible(catalog.Id))
                            throw new UnauthorizedAccessException("The current user is not allowed to access this filter.");

                        var dataSourceController = this.GetDataSourceControllerAsync(subCatalogItem.Representation.BackendSource).Result;
                        var pipe = new Pipe();

                        var doubleStream = dataSourceController.ReadAsStream(
                            begin,
                            end, 
                            subCatalogItem,
                            this.DataSourceControllerLogger);

                        var doubleData = new double[doubleStream.Length / 8];
                        var byteData = new CastMemoryManager<double, byte>(doubleData.AsMemory()).Memory;

                        while (byteData.Length > 0)
                        {
                            var read = doubleStream.ReadAsync(byteData, cancellationToken).Result;

                            if (read == 0)
                                throw new Exception("The stream ended early.");

                            byteData = byteData[read..];
                        }

                        return doubleData;
                    };

                    // execute
                    var filter = cacheEntry.FilterProvider.Filters.First(filter => filter.ResourceId == resource.Id);
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
                    filterSettings = JsonSerializerHelper.Deserialize<FilterSettings>(jsonString);

                    // add to cache
                    var filterSettings2 = filterSettings; // to make compiler happy
                    FilterDataSource.FilterSettingsCache.AddOrUpdate(this.Context.ResourceLocator, filterSettings, (key, value) => filterSettings2);

                    return true;
                }
            }

            return false;
        }

        private async Task PopulateCacheAsync(FilterSettings filterSettings, CancellationToken cancellationToken)
        {
            var filterCodeDefinitions = filterSettings.CodeDefinitions
                .Where(filterSetting => filterSetting.CodeType == CodeType.Filter && filterSetting.IsEnabled)
                .ToList();

            this.Context.Logger.LogInformation("Compile {FilterCount} filters", filterCodeDefinitions.Count);

            var cacheEntries = new FilterDataSourceCacheEntry[filterCodeDefinitions.Count];

            var compileTasks = Enumerable
                .Range(0, filterCodeDefinitions.Count)
                .Select(async i =>
                {
                    var filterCodeDefinition = filterCodeDefinitions[i];
                    filterCodeDefinition.Code = this.PrepareCode(filterCodeDefinition.Code);

                    var additionalCodeFiles = filterSettings.GetSharedFiles(filterCodeDefinition.Owner)
                        .Select(codeDefinition => codeDefinition.Code)
                        .ToList();

                    var roslynCatalog = new RoslynProject(filterCodeDefinition, additionalCodeFiles);
                    using var peStream = new MemoryStream();

                    var emitResult = (await roslynCatalog.Workspace.CurrentSolution.Projects.First()
                        .GetCompilationAsync(cancellationToken))
                        .Emit(peStream);

                    if (!emitResult.Success)
                        return;

                    peStream.Seek(0, SeekOrigin.Begin);

                    var loadContext = new FilterDataSourceLoadContext();
                    var assembly = loadContext.LoadFromStream(peStream);

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
                        var supportedChanneIds = filterProvider.Filters.Select(filter => filter.ResourceId).ToList();
                        cacheEntries[i] = new FilterDataSourceCacheEntry(filterCodeDefinition, loadContext, filterProvider, supportedChanneIds);
                    }
                    catch (Exception ex)
                    {
                        this.Context.Logger.LogError(ex, "Failed to instantiate the filter provider {FilterName} of user {FilterOwner}", filterCodeDefinition.Name, filterCodeDefinition.Owner);
                    }
                })
                .ToArray();

            await Task.WhenAll(compileTasks);

            _cacheEntries.AddRange(cacheEntries.Where(cacheEntry => cacheEntry is not null));
        }

        private string PrepareCode(string code)
        {
            // change signature
            code = code.Replace(
                "DataProvider dataProvider",
                "GetFilterData getData");

            // matches strings like "= dataProvider.IN_MEMORY_TEST_ACCESSIBLE.T1.REPRESENTATION_1_s_mean;"
            var pattern1 = @"=\s*dataProvider\.([a-zA-Z_0-9]+)\.([a-zA-Z_0-9]+)\.REPRESENTATION_([a-zA-Z_0-9]+);";
            code = Regex.Replace(code, pattern1, match =>
            {
                var catalogId = match.Groups[1].Value;
                var resourceId = match.Groups[2].Value;
                var representationId = match.Groups[3].Value;

                return $"= getData(\"{catalogId}\", \"{resourceId}\", \"{representationId}\", begin, end);";
            });

            // matches strings like "= dataProvider.Read(campaignId, resourceId, representationId, begin, end);"
            var pattern3 = @"=\s*dataProvider\.Read\((.*?),(.*?),(.*?),(.*?),(.*?)\);";
            code = Regex.Replace(code, pattern3, match =>
            {
                var catalogId = match.Groups[1].Value;
                var resourceId = match.Groups[2].Value;
                var representationId = match.Groups[3].Value;
                var begin = match.Groups[4].Value;
                var end = match.Groups[5].Value;

                return $"= getData({catalogId}, {resourceId}, {representationId}, {begin}, {end});";
            });

            // matches strings like "= dataProvider.Read(campaignId, resourceId, representationId);"
            var pattern2 = @"=\s*dataProvider\.Read\((.*?),(.*?),(.*?)\);";
            code = Regex.Replace(code, pattern2, match =>
            {
                var catalogId = match.Groups[1].Value;
                var resourceId = match.Groups[2].Value;
                var representationId = match.Groups[3].Value;

                return $"= getData({catalogId}, {resourceId}, {representationId}, begin, end);";
            });

            return code;
        }

        #endregion
    }
}
