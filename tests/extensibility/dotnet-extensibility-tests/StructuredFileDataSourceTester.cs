using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System.Text.Json;

namespace Nexus.Extensibility.Tests
{
    public class StructuredFileDataSourceTester : StructuredFileDataSource
    {
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

        public Dictionary<string, Dictionary<string, FileSource>> Config { get; private set; } = default!;

        #endregion

        #region Methods

        protected override async Task InitializeAsync(CancellationToken cancellationToken)
        {
            var configFilePath = Path.Combine(Root, "config.json");

            if (!File.Exists(configFilePath))
                throw new Exception($"The configuration file does not exist on path {configFilePath}.");

            var jsonString = await File.ReadAllTextAsync(configFilePath, cancellationToken);
            Config = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, FileSource>>>(jsonString)!;
        }

        protected override Task<Func<string, Dictionary<string, FileSource>>> GetFileSourceProviderAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<Func<string, Dictionary<string, FileSource>>>(catalogId => Config[catalogId]);
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

            var fileSourceId = Config.First().Value.First().Key;

            var resource = new ResourceBuilder(id: "Resource1")
                .WithProperty(StructuredFileDataSource.FileSourceKey, fileSourceId)
                .AddRepresentation(representation)
                .Build();

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
