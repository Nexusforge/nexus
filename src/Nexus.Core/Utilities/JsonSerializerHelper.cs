using Nexus.Core;
using System.IO;
using System.Text.Json;

namespace Nexus.Utilities
{
    public static class JsonSerializerHelper
    {
        public static void Serialize<T>(T value, string filePath)
        {
            var options = new JsonSerializerOptions() { WriteIndented = true };
            options.Converters.Add(new TimeSpanConverter());

            var jsonString = JsonSerializer.Serialize(value, options);

            File.WriteAllText(filePath, jsonString);
        }

        public static T Deserialize<T>(string filePath)
        {
            var jsonString = File.ReadAllText(filePath);

            var options = new JsonSerializerOptions();
            options.Converters.Add(new TimeSpanConverter());

            return JsonSerializer.Deserialize<T>(jsonString, options);
        }
    }
}
