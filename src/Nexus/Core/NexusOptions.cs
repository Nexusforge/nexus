using System.IO;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Core
{
    [DataContract]
    public class NexusOptions
    {
        public NexusOptions()
        {
            // unset, mutable
            this.DataBaseFolderPath = string.Empty;

            // preset, mutable
            this.AggregationChunkSizeMB = 200;
            this.AspBaseUrl = "http://0.0.0.0:8080";
            this.Language = "en";
        }

        // unset, mutable
        public string DataBaseFolderPath { get; set; }

        // preset, mutable

        public uint AggregationChunkSizeMB { get; set; }

        public string AspBaseUrl { get; set; }

        public string Language { get; set; }

        // preset, immutable
        [JsonIgnore]
        public string ExportDirectoryPath => Path.Combine(this.DataBaseFolderPath, "EXPORT");

        public static NexusOptions Load(string filePath)
        {
            var jsonString = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions();
            options.Converters.Add(new TimeSpanConverter());

            return JsonSerializer.Deserialize<NexusOptions>(jsonString, options);
        }

        public void Save(string filePath)
        {
            var folderPath = Path.GetDirectoryName(filePath);
            Directory.CreateDirectory(folderPath);

            var options = new JsonSerializerOptions()
            {
                WriteIndented = true
            };

            options.Converters.Add(new TimeSpanConverter());

            var jsonString = JsonSerializer.Serialize(this, options);
            File.WriteAllText(filePath, jsonString);
        }
    }
}
