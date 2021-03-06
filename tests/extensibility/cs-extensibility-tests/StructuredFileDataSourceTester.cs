using Nexus.DataModel;
using System.Text.Json;

namespace Nexus.Extensibility.Tests
{
    public class StructuredFileDataSourceTester : StructuredFileDataSource
    {
        #region Types

        public record CatalogDescription(Dictionary<string, FileSource> Config);

        #endregion

        #region Fields

        private bool _overrideFindFilePathsWithNoDateTime;

        #endregion

        #region Constructors

        public StructuredFileDataSourceTester(
            bool overrideFindFilePathsWithNoDateTime = false)
        {
            _overrideFindFilePathsWithNoDateTime = overrideFindFilePathsWithNoDateTime;
        }

        #endregion

        #region Properties

        public Dictionary<string, CatalogDescription> Config { get; private set; } = default!;

        private DataSourceContext Context { get; set; } = default!;

        #endregion

        #region Methods

        protected override async Task SetContextAsync(DataSourceContext context, CancellationToken cancellationToken)
        {
            Context = context;

            var configFilePath = Path.Combine(Root, "config.json");

            if (!File.Exists(configFilePath))
                throw new Exception($"The configuration file does not exist on path {configFilePath}.");

            var jsonString = await File.ReadAllTextAsync(configFilePath, cancellationToken);
            Config = JsonSerializer.Deserialize<Dictionary<string, CatalogDescription>>(jsonString)!;
        }

        protected override Task<FileSourceProvider> GetFileSourceProviderAsync(CancellationToken cancellationToken)
        {
            var all = Config.ToDictionary(
                config => config.Key,
                config => config.Value.Config.Values.Cast<FileSource>().ToArray());

            return Task.FromResult(new FileSourceProvider(
                All: all,
                Single: catalogItem => all[catalogItem.Catalog.Id].First()));
        }

        protected override Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken)
        {
            return Task.FromResult(new CatalogRegistration[] { new CatalogRegistration("/A/B/C", string.Empty) });
        }

        protected override Task<ResourceCatalog> GetCatalogAsync(string catalogId, CancellationToken cancellationToken)
        {
            var representation = new Representation(
                    dataType: NexusDataType.INT64,
                    samplePeriod: TimeSpan.FromSeconds(1));

            var resource = new Resource(id: "Resource1", representations: new List<Representation>() { representation });
            var catalog = new ResourceCatalog(id: "/A/B/C", resources: new List<Resource>() { resource });

            return Task.FromResult(catalog);
        }

        protected override async Task ReadSingleAsync(ReadInfo readInfo, CancellationToken cancellationToken)
        {
            var bytes = await File
                .ReadAllBytesAsync(readInfo.FilePath);

            bytes
                .CopyTo(readInfo.Data.Span);

            readInfo
                .Status
                .Span
                .Fill(1);
        }

        protected override async Task<(string[], DateTime)> FindFilePathsAsync(DateTime begin, FileSource config)
        {
            if (_overrideFindFilePathsWithNoDateTime)
            {
                var result = await base.FindFilePathsAsync(begin, config);
                return (result.Item1, default);
            }
            else
            {
                return await base.FindFilePathsAsync(begin, config);
            }
        }

        #endregion
    }
}
