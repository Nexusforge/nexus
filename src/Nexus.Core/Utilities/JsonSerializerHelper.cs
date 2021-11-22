using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Utilities
{
    internal static class JsonSerializerHelper
    {
        public static string SerializeIntended<T>(T value)
        {
            var options = new JsonSerializerOptions() 
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
            };

            return JsonSerializer.Serialize(value, options);
        }
    }
}
