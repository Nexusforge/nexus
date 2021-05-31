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

        public record ProjectDescription()
        {
            public Dictionary<string, DataAccessDescription> DataAccess { get; init; }
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

        private Dictionary<string, ProjectDescription> _config;

        #endregion

        #region Methods

        protected override async Task OnParametersSetAsync()
        {
            var configFilePath = Path.Combine(this.RootPath, "config.json");

            if (!File.Exists(configFilePath))
                throw new Exception($"The configuration file does not exist on path '{configFilePath}'.");

            _config = await DeserializeAsync<Dictionary<string, ProjectDescription>>(configFilePath);
        }

        protected override Task<DataAccessDescriptions> GetDataAccessDescriptionsAsync(string projectId, CancellationToken cancellationToken)
        {
            var all = _config[projectId]
                .DataAccess
                .Values
                .Cast<DataAccessDescription>()
                .ToList();

            return Task.FromResult(new DataAccessDescriptions
            {
                All = all,
                Single = dataset => all.First(),
            });
        }

        protected override Task<List<Project>> GetDataModelAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new List<Project>() { new Project("/A/B/C") });
        }

        protected override Task ReadSingleAsync<T>(ReadInfo<T> readInfo, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
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
