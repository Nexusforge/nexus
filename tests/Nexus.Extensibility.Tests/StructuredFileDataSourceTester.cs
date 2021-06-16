using Nexus.DataModel;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        private Dictionary<string, CatalogDescription> _config;

        #endregion

        #region Constructors

        public StructuredFileDataSourceTester(
            bool overrideFindFilePathsWithNoDateTime = false)
        {
            _overrideFindFilePathsWithNoDateTime = overrideFindFilePathsWithNoDateTime;
        }

        #endregion

        #region Methods

        protected override async Task OnParametersSetAsync()
        {
            var configFilePath = Path.Combine(this.Root, "config.json");

            if (!File.Exists(configFilePath))
                throw new Exception($"The configuration file does not exist on path '{configFilePath}'.");

            _config = await DeserializeAsync<Dictionary<string, CatalogDescription>>(configFilePath);
        }

        protected override Task<Configuration> GetConfigurationAsync(string catalogId, CancellationToken cancellationToken)
        {
            var all = _config[catalogId]
                .Config
                .Values
                .Cast<ConfigurationUnit>()
                .ToList();

            return Task.FromResult(new Configuration
            {
                All = all,
                Single = dataset => all.First(),
            });
        }

        protected override Task<List<Catalog>> GetCatalogsAsync(CancellationToken cancellationToken)
        {
            var catalog = new Catalog("/A/B/C");
            var channel = new Channel(Guid.NewGuid(), catalog);
            var dataset = new Dataset("1 Hz_mean", channel) { DataType = NexusDataType.INT64 };

            channel.Datasets.Add(dataset);
            catalog.Channels.Add(channel);

            return Task.FromResult(new List<Catalog>() { catalog });
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
