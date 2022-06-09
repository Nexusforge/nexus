using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexus.DataModel
{
    internal static class DataModelUtilities
    {
        public static List<Resource>? MergeResources(IReadOnlyList<Resource>? resources1, IReadOnlyList<Resource>? resources2)
        {
            if (resources1 is null && resources2 is null)
                return null;

            if (resources1 is null)
                return resources2!
                    .Select(resource => resource.DeepCopy())
                    .ToList();

            if (resources2 is null)
                return resources1!
                    .Select(resource => resource.DeepCopy())
                    .ToList();

            var mergedResources = resources1
                .Select(resource => resource.DeepCopy())
                .ToList();

            foreach (var newResource in resources2)
            {
                var index = mergedResources.FindIndex(current => current.Id == newResource.Id);

                if (index >= 0)
                {
                    mergedResources[index] = mergedResources[index].Merge(newResource);
                }

                else
                {
                    mergedResources.Add(newResource.DeepCopy());
                }
            }

            return mergedResources;
        }

        public static List<Representation>? MergeRepresentations(IReadOnlyList<Representation>? representations1, IReadOnlyList<Representation>? representations2)
        {
            if (representations1 is null && representations2 is null)
                return null;

            if (representations1 is null)
                return representations2!
                    .Select(representation => representation.DeepCopy())
                    .ToList();

            if (representations2 is null)
                return representations1!
                    .Select(representation => representation.DeepCopy())
                    .ToList();

            var mergedRepresentations = representations1
                .Select(representation => representation.DeepCopy())
                .ToList();

            foreach (var newRepresentation in representations2)
            {
                var index = mergedRepresentations.FindIndex(current => current.Id == newRepresentation.Id);

                if (index >= 0)
                {
                    if (!newRepresentation.Equals(mergedRepresentations[index]))
                        throw new Exception("The representations to be merged are not equal.");

                }

                else
                {
                    mergedRepresentations.Add(newRepresentation);
                }
            }

            return mergedRepresentations;
        }

        public static JsonElement? MergeProperties(JsonElement? properties1, JsonElement? properties2)
        {
            if (!(properties1.HasValue && properties2.HasValue))
                return default;

            if (!properties1.HasValue)
                return properties2;

            if (!properties2.HasValue)
                return properties1;

            if (properties1.Value.ValueKind != JsonValueKind.Object || properties2.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"The JSON elements to merge must be a JSON object. Instead it is {properties1.Value.ValueKind}.");

            var mergedProperties = new JsonObject();
            MergeObjects(mergedProperties, properties1.Value, properties2.Value);
                
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
                    {
                        currentObject[property.Name] = ToJsonNode(newValue);
                    }
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