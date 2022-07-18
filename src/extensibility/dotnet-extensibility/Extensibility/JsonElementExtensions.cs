using System.Text.Json;

namespace Nexus.Extensibility
{
#warning Remove as soon as there is framework level support (may take a while)
    /// <summary>
    /// A static class with extensions for <see cref="JsonElement"/>.
    /// </summary>
    public static class JsonElementExtensions
    {
        /// <summary>
        /// Reads the value of the specified property as string if it exists.
        /// </summary>
        /// <param name="element">The JSON element.</param>
        /// <param name="propertyName">The propery name.</param>
        /// <returns></returns>
        public static string? GetStringValue(this JsonElement? element, string propertyName)
        {
            if (element.HasValue && 
                element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(propertyName, out var propertyValue) &&
                propertyValue.ValueKind == JsonValueKind.String)
                return propertyValue.GetString();

            return default;
        }

        /// <summary>
        /// Reads the value of the specified property as string array if it exists.
        /// </summary>
        /// <param name="element">The JSON element.</param>
        /// <param name="propertyName">The property name.</param>
        /// <returns></returns>
        public static string?[]? GetStringArray(this JsonElement? element, string propertyName)
        {
            if (element.HasValue && 
                element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(propertyName, out var propertyValue) &&
                propertyValue.ValueKind == JsonValueKind.Array)
                return propertyValue
                    .EnumerateArray()
                    .Where(current => current.ValueKind == JsonValueKind.String)
                    .Select(current => current.GetString())
                    .ToArray();

            return default;
        }
    }
}