using System.Text.Json;

namespace Nexus.Utilities
{
    internal static class JsonSerializerHelper
    {
        public static string SerializeIntended<T>(T value)
        {
            var options = new JsonSerializerOptions() { WriteIndented = true };
            return JsonSerializer.Serialize(value, options);
        }
    }
}
