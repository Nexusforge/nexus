using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility.Tests
{
    public class StructuredFileDataSourceTester : StructuredFileDataSource
    {
        #region Types

        public record CatalogDescription()
        {
            public Dictionary<string, ConfigurationUnit> Config { get; init; }
        }

        class TimeSpanConverter : JsonConverter<TimeSpan>
        {
            public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return TimeSpan.Parse(reader.GetString());
            }

            public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToString());
            }
        }

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

        public Dictionary<string, CatalogDescription> Config { get; private set; }

        private DataSourceContext Context { get; set; }

        #endregion

        #region Methods

        protected override async Task SetContextAsync(DataSourceContext context, CancellationToken cancellationToken)
        {
            this.Context = context;

            var configFilePath = Path.Combine(this.Root, "config.json");

            if (!File.Exists(configFilePath))
                throw new Exception($"The configuration file does not exist on path '{configFilePath}'.");

            this.Config = await DeserializeAsync<Dictionary<string, CatalogDescription>>(configFilePath);
        }

        protected override Task<Configuration> GetConfigurationAsync(CancellationToken cancellationToken)
        {
            var all = this.Config.ToDictionary(
                config => config.Key,
                config => config.Value.Config.Values.Cast<ConfigurationUnit>().ToArray());

            return Task.FromResult(new Configuration
            {
                All = all,
                Single = catalogItem => all[catalogItem.Catalog.Id].First(),
            });
        }

        protected override Task<ResourceCatalog[]> GetCatalogsAsync(CancellationToken cancellationToken)
        {
            if (this.Context.Catalogs is null)
            {
                var catalog = new ResourceCatalog() { Id = "/A/B/C" };
                var resource = new Resource() { Id = Guid.NewGuid() };
                var representation = new Representation() { Id = "1 Hz_mean", DataType = NexusDataType.INT64 };

                resource.Representations.Add(representation);
                catalog.Resources.Add(resource);

                this.Context = this.Context with
                {
                    Catalogs = new[] { catalog }
                };
            }

            return Task.FromResult(this.Context.Catalogs);
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

        protected override async Task<(string[], DateTime)> FindFilePathsAsync(DateTime begin, ConfigurationUnit config)
        {
            if (_overrideFindFilePathsWithNoDateTime)
            {
                var result = await base.FindFilePathsAsync(begin, config).ConfigureAwait(false);
                return (result.Item1, default);
            }
            else
            {
                return await base.FindFilePathsAsync(begin, config);
            }
        }

        #endregion

        #region Helpers

        private static async Task<T> DeserializeAsync<T>(string filePath)
        {
            using var jsonStream = File.OpenRead(filePath);

            var options = new JsonSerializerOptions();
            options.Converters.Add(new TimeSpanConverter());

            return await JsonSerializer
                .DeserializeAsync<T>(jsonStream, options)
                .AsTask()
                .ConfigureAwait(false);
        }

        #endregion
    }
}
