﻿using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Nexus.Utilities
{
    internal static class JsonSerializerHelper
    {
        private static JsonSerializerOptions _options = new JsonSerializerOptions()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };

        public static string SerializeIntended<T>(T value)
        {
            return JsonSerializer.Serialize(value, _options);
        }

        public static Task SerializeIntendedAsync<T>(Stream utf8Json, T value)
        {
            return JsonSerializer.SerializeAsync(utf8Json, value, _options);
        }
    }
}
