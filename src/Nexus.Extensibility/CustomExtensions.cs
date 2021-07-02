using System;
using System.Linq;

namespace Nexus
{
    public static class CustomExtensions
    {
        private const int NS_PER_TICK = 100;
        private static string[] _postFixes = new[] {"ns", "us", "ms", "s" };

        public static string ToUnitString(this TimeSpan samplePeriod, bool underscore = false)
        {
            var fillChar = underscore ? '_' : ' ';
            var currentValue = samplePeriod.Ticks * NS_PER_TICK;

            for (int i = 0; i < _postFixes.Length; i++)
            {
                var quotient = Math.DivRem(currentValue, 1000, out var remainder);

                if (remainder != 0)
                    return $"{currentValue}{fillChar}{_postFixes[i]}";

                else
                    currentValue = quotient;
            }

            return $"{(int)currentValue}{fillChar}{_postFixes.Last()}";
        }

        public static string ToPath(this Uri url)
        {
            var isRelativeUri = !url.IsAbsoluteUri;

            if (isRelativeUri)
                return url.ToString();

            else if (url.IsFile)
                return url.LocalPath.Replace('\\', '/');

            else
                throw new Exception("Only a file URI can be converted to a path.");
        }
    }
}
