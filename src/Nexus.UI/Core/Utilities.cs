using System.Text.Json;
using System.Text.RegularExpressions;
using Nexus.Api;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Core;

public static class Utilities
{
    public static string ToSpaceFilledCatalogId(string catalogId)
        => catalogId.TrimStart('/').Replace("/", " / ");

    public static string EscapeDataString(string catalogId)
        => Uri.EscapeDataString(catalogId);

    private const int NS_PER_TICK = 100;
    private static long[] _nanoseconds = new[] { (long)1e0, (long)1e3, (long)1e6, (long)1e9, (long)60e9, (long)3600e9, (long)86400e9 };
    private static int[] _quotients = new[] { 1000, 1000, 1000, 60, 60, 24, 1 };
    private static string[] _postFixes = new[] { "ns", "us", "ms", "s", "min", "h", "d" };
    private static Regex _unitStringEvaluator = new Regex(@"^([0-9]+)[\s_]?([a-z]+)$", RegexOptions.Compiled);

    public static string ToUnitString(this TimeSpan samplePeriod, bool withUnderScore = false)
    {
        var fillValue = withUnderScore
            ? "_"
            : " ";

        var currentValue = samplePeriod.Ticks * NS_PER_TICK;

        for (int i = 0; i < _postFixes.Length; i++)
        {
            var quotient = Math.DivRem(currentValue, _quotients[i], out var remainder);

            if (remainder != 0)
                return $"{currentValue}{fillValue}{_postFixes[i]}";

            else
                currentValue = quotient;
        }

        return $"{(int)currentValue}{fillValue}{_postFixes.Last()}";
    }

    public static TimeSpan ToPeriod(string unitString)
    {
        var match = _unitStringEvaluator.Match(unitString);

        if (!match.Success)
            throw new Exception("The provided unit string is invalid.");

        var unitIndex = Array.IndexOf(_postFixes, match.Groups[2].Value);

        if (unitIndex == -1)
            throw new Exception("The provided unit is invalid.");

        var totalNanoSeconds = long.Parse(match.Groups[1].Value) * _nanoseconds[unitIndex];

        if (totalNanoSeconds % NS_PER_TICK != 0)
            throw new Exception("The sample period must be a multiple of 100 ns.");

        var ticks = totalNanoSeconds / NS_PER_TICK;

        return new TimeSpan(ticks);
    }

    public static long GetElementCount(DateTime begin, DateTime end, TimeSpan samplePeriod)
    {
        return (long)((end - begin).Ticks / samplePeriod.Ticks);
    }

    public static long GetByteCount(long elementCount, IEnumerable<CatalogItemSelectionViewModel> selectedatalogItems)
    {
        var elementSize = 8;

        var representationCount = selectedatalogItems
            .Sum(item => item.Kinds.Count);

        return elementCount * elementSize * representationCount;
    }

    public static void ParseResourcePath(
        string resourcePath,
        out string catalogId, 
        out string resourceId, 
        out TimeSpan samplePeriod,
        out RepresentationKind kind,
        out TimeSpan basePeriod)
    {
#warning Replace with regex

        var pathParts1 = resourcePath
            .Split("#", count: 2);

        var pathParts2 = pathParts1[0]
            .Split('/');

        /* catalog id */
        catalogId = string
            .Join('/', pathParts2[..^2]);

        /* resource id */
        resourceId = pathParts2[^2];

        var representationId = pathParts2[^1];
        var representationIdParts = representationId.Split('_', count: 3);

        var unitString = string
            .Join('_', representationIdParts[0..2]);

        samplePeriod = Utilities.ToPeriod(unitString); 

        /* kind */
        if (representationIdParts.Length == 3)
        {
            var rawKind = representationIdParts[2];
            kind = Utilities.StringToKind(rawKind);
        }

        else
        {
            kind = RepresentationKind.Original;
        }

        /* url fragment */
        var baseUnitString = pathParts1[1].Split("=")[1];
        basePeriod = Utilities.ToPeriod(baseUnitString);
    }

    private static Regex _snakeCaseEvaluator = new Regex("(?<=[a-z])([A-Z])", RegexOptions.Compiled);

    public static string? KindToString(RepresentationKind kind)
    {
        var snakeCaseKind = kind == RepresentationKind.Original 
            ? null
            : _snakeCaseEvaluator.Replace(kind.ToString(), "_$1").Trim().ToLower();

        return snakeCaseKind;
    }

    public static RepresentationKind StringToKind(string rawKind)
    {
        var camelCase = Regex.Replace(rawKind, "_.", match => match.Value.Substring(1).ToUpper());
        var pascalCase = string.Concat(camelCase[0].ToString().ToUpper(), camelCase.AsSpan(1));
        var kind = Enum.Parse<RepresentationKind>(pascalCase);

        return kind;
    }

    public static string? GetStringValue(JsonElement? properties, string propertyName)
    {
        if (properties.HasValue && 
            properties.Value.ValueKind == JsonValueKind.Object &&
            properties.Value.TryGetProperty(propertyName, out var propertyValue) &&
            propertyValue.ValueKind == JsonValueKind.String)
            return propertyValue.GetString()!;

        return default;
    }

    public static string[]? GetStringArray(this JsonElement? element, string propertyName)
    {
        if (element.HasValue && 
            element.Value.ValueKind == JsonValueKind.Object &&
            element.Value.TryGetProperty(propertyName, out var propertyValue) &&
            propertyValue.ValueKind == JsonValueKind.Array)
            return propertyValue
                .EnumerateArray()
                .Where(current => current.ValueKind == JsonValueKind.String)
                .Select(current => current.GetString()!)
                .ToArray();

        return default;
    }
}