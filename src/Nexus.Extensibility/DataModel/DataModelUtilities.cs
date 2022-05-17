using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexus.DataModel
{
    internal static class DataModelUtilities
    {
        public static JsonElement MergeProperties(JsonElement properties1, JsonElement properties2)
        {
            if (properties1.ValueKind != JsonValueKind.Object || properties2.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"The JSON elements to merge must be a JSON object. Instead it is {properties1.ValueKind}.");

            var mergedProperties = new JsonObject();
            MergeObjects(mergedProperties, properties1, properties2);
                
            return JsonSerializer.SerializeToElement(mergedProperties);
        }

        private static void MergeObjects(JsonObject currentObject, JsonElement root1, JsonElement root2)
        {
            foreach (var property in root1.EnumerateObject())
            {
                if (root2.TryGetProperty(property.Name, out JsonElement newValue) && newValue.ValueKind != JsonValueKind.Null)
                {
                    var originalValue = property.Value;
                    var originalValueKind = originalValue.ValueKind;

                    if (newValue.ValueKind == JsonValueKind.Object && originalValueKind == JsonValueKind.Object)
                    {
                        var newObject = new JsonObject();
                        currentObject[property.Name] = newObject;

                        MergeObjects(newObject, originalValue, newValue);
                    }

                    else if (newValue.ValueKind == JsonValueKind.Array && originalValueKind == JsonValueKind.Array)
                    {
                        var newArray = new JsonArray();
                        currentObject[property.Name] = newArray;

                        MergeArrays(newArray, originalValue, newValue);
                    }

                    else
                        currentObject[property.Name] = ToJsonNode(newValue);
                }

                else
                {
                    currentObject[property.Name] = ToJsonNode(property.Value);
                }
            }

            foreach (var property in root2.EnumerateObject())
            {
                if (!root1.TryGetProperty(property.Name, out _))
                    currentObject[property.Name] = ToJsonNode(property.Value);
            }
        }

        private static void MergeArrays(JsonArray currentArray, JsonElement root1, JsonElement root2)
        {
            foreach (var element in root1.EnumerateArray())
            {
                currentArray.Add(element);
            }

            foreach (var element in root2.EnumerateArray())
            {
                currentArray.Add(element);
            }
        }

        public static JsonNode? ToJsonNode(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.Object => JsonObject.Create(element),
                JsonValueKind.Array => JsonArray.Create(element),
                _ => JsonValue.Create(element)
            };
        }
    }
}