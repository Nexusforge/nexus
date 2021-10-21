using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.DataModel
{
    public static class DataModelExtensions
    {
        #region Fluent API

        internal const string Description = "Description";
        internal const string ShortDescription = "ShortDescription";
        internal const string Warning = "Warning";
        internal const string Contact = "Contact";
        internal const string Unit = "Unit";
        internal const string Groups = "Groups";

        public static ResourceCatalogBuilder WithDescription(this ResourceCatalogBuilder catalogBuilder, string description)
        {
            return catalogBuilder.WithProperty(Description, description);
        }

        public static ResourceCatalogBuilder WithShortDescription(this ResourceCatalogBuilder catalogBuilder, string shortDescription)
        {
            return catalogBuilder.WithProperty(ShortDescription, shortDescription);
        }

        public static ResourceCatalogBuilder WithContact(this ResourceCatalogBuilder catalogBuilder, string contact)
        {
            return catalogBuilder.WithProperty(Contact, contact);
        }

        public static ResourceBuilder WithUnit(this ResourceBuilder resourceBuilder, string unit)
        {
            return resourceBuilder.WithProperty(Unit, unit);
        }

        public static ResourceBuilder WithDescription(this ResourceBuilder resourceBuilder, string description)
        {
            return resourceBuilder.WithProperty(Description, description);
        }

        public static ResourceBuilder WithWarning(this ResourceBuilder resourceBuilder, string warning)
        {
            return resourceBuilder.WithProperty(Warning, warning);
        }

        public static ResourceBuilder WithGroups(this ResourceBuilder resourceBuilder, params string[] groups)
        {
            return resourceBuilder.WithGroups((IEnumerable<string>)groups);
        }

        public static ResourceBuilder WithGroups(this ResourceBuilder resourceBuilder, IEnumerable<string> groups)
        {
            var counter = 0;

            foreach (var group in groups)
            {
                resourceBuilder.WithProperty($"{Groups}:{counter}", group);
                counter++;
            }

            return resourceBuilder;
        }

        #endregion

        #region Misc

        private const int NS_PER_TICK = 100;
        private static int[] _quotients = new[] { 1000, 1000, 1000, 60, 1 };
        private static string[] _postFixes = new[] { "ns", "us", "ms", "s", "min" };

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

        public static string ToUnitString(this TimeSpan samplePeriod)
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

        internal static CatalogItem Find(this IEnumerable<ResourceCatalog> catalogs, string resourcePath)
        {
            if (!catalogs.TryFind(resourcePath, out var catalogItem))
                throw new Exception($"The resource path '{resourcePath}' could not be found.");

            return catalogItem;
        }

        internal static bool TryFind(this IEnumerable<ResourceCatalog> catalogs, string resourcePath, out CatalogItem catalogItem)
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

        #endregion
    }
}
