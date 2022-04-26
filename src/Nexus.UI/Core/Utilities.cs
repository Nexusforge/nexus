using System.Text.RegularExpressions;

namespace Nexus.UI.Core;

public static class Utilities
{
    public static string ToSpaceFilledCatalogId(string catalogId)
        => catalogId.TrimStart('/').Replace("/", " / ");

    public static string EscapeDataString(string catalogId)
        => Uri.EscapeDataString(catalogId);

    private const int NS_PER_TICK = 100;
    private static long[] _nanoseconds = new[] { (long)1e0, (long)1e3, (long)1e6, (long)1e9, (long)60e9 };
    private static int[] _quotients = new[] { 1000, 1000, 1000, 60, 1 };
    private static string[] _postFixes = new[] { "ns", "us", "ms", "s", "min" };
    private static Regex _unitStringEvaluator = new Regex(@"^([0-9]+)[\s_]?([a-z]+)$", RegexOptions.Compiled);

    public static string ToUnitString(this TimeSpan samplePeriod)
    {
        var currentValue = samplePeriod.Ticks * NS_PER_TICK;

        for (int i = 0; i < _postFixes.Length; i++)
        {
            var quotient = Math.DivRem(currentValue, _quotients[i], out var remainder);

            if (remainder != 0)
                return $"{currentValue} {_postFixes[i]}";

            else
                currentValue = quotient;
        }

        return $"{(int)currentValue} {_postFixes.Last()}";
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
}