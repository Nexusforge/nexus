using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus
{
    public static class CustomExtensions
    {
        private const int NS_PER_TICK = 100;
        private static int[] _quotients = new[] { 1000, 1000, 1000, 60, 1 };
        private static string[] _postFixes = new[] {"ns", "us", "ms", "s", "min" };

        internal static string ToUnitString(this TimeSpan samplePeriod)
        {
            var currentValue = samplePeriod.Ticks * NS_PER_TICK;

            for (int i = 0; i < _postFixes.Length; i++)
            {
                var quotient = Math.DivRem(currentValue, _quotients[i], out var remainder);

                if (remainder != 0)
                    return $"{currentValue}_{_postFixes[i]}";

                else
                    currentValue = quotient;
            }

            return $"{(int)currentValue}_{_postFixes.Last()}";
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

        public static CatalogItem Find(this IEnumerable<ResourceCatalog> catalogs, string resourcePath)
        {
            if (!catalogs.TryFind(resourcePath, out var catalogItem))
                throw new Exception($"The resource path '{resourcePath}' could not be found.");

            return catalogItem;
        }

        public static bool TryFind(this IEnumerable<ResourceCatalog> catalogs, string resourcePath, out CatalogItem catalogItem)
        {
            catalogItem = default(CatalogItem);

            foreach (var catalog in catalogs)
            {
                if (catalog.TryFind(resourcePath, out catalogItem))
                    break;
            }

            if (catalogItem is null)
                return false;

            return true;
        }
    }
}
