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
    public class SimpleFileDataSourceTester : SimpleFileDataSource
    {
        #region Methods

        protected override async Task<List<SourceDescription>> GetSourceDescriptionsAsync(string projectId, CancellationToken cancellationToken)
        {
            var configFilePath = Path.Combine(this.RootPath, "config.json");

            var config = await SimpleFileDataSourceTester
                .DeserializeAsync<FileSystemDescription>(configFilePath);

            if (!config.Projects.TryGetValue(projectId, out var sourceDescriptionMap))
                throw new Exception($"A configuration for the project '{projectId}' could not be found.");

            return sourceDescriptionMap.Values.ToList();
        }

        protected override Task<List<Project>> GetDataModelAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new List<Project>() { new Project("/A/B/C") });
        }

        protected override Task ReadSingleAsync<T>(Dataset dataset, ReadResult<T> readResult, DateTime begin, DateTime end, CancellationToken cancellationToken)
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
    }
}
