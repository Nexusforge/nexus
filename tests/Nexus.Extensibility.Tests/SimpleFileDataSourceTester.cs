using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Nexus.Extensibility.Tests
{
    public class SimpleFileDataSourceTester : SimpleFileDataSource
    {
        #region Methods

        public override async Task<(FileSystemDescription, List<Project>)> InitializeDataModelAsync()
        {
            var configFilePath = Path.Combine(this.RootPath, "config.json");
            var config = await SimpleFileDataSourceTester.DeserializeAsync<FileSystemDescription>(configFilePath);
            var projects = new List<Project>() { new Project("/A/B/C") };

            return (config, projects);
        }

        public override Task<ReadResult<T>> ReadSingleAsync<T>(Dataset dataset, DateTime begin, DateTime end)
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
