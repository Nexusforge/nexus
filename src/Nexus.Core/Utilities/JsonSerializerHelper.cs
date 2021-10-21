using Nexus.Core;
using System.Text.Json;

namespace Nexus.Utilities
{
    internal static class JsonSerializerHelper
    {
        public static string Serialize<T>(T value)
        {
            var options = new JsonSerializerOptions() { WriteIndented = true };
            options.Converters.Add(new TimeSpanConverter());

            return JsonSerializer.Serialize(value, options);
        }

        public static T Deserialize<T>(string jsonString)
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new TimeSpanConverter());

            return JsonSerializer.Deserialize<T>(jsonString, options);
        }
    }
}
