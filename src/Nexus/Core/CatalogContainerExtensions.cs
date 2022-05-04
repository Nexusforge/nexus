using Nexus.DataModel;
using System.Text.RegularExpressions;

namespace Nexus.Core
{
    internal static class CatalogContainerExtensions
    {
        /* Example resource paths:
         * 
         * /a/b/c/T1/10_ms
         * /a/b/c/T1/100_ms
         * /a/b/c/T1/600_s_mean_polar
         * /a/b/c/T1/600_s_mean_polar#base=100_ms
         */

        private static Regex _resourcePathEvaluator = new Regex(@"(.*)\/(.*)\/([0-9]+_[a-zA-Z]+)(?:_(.+))?", RegexOptions.Compiled);
        private static Regex _resourcePathFragmentEvaluator = new Regex(@"base=(.*)", RegexOptions.Compiled);

        public static async Task<CatalogItemRequest?> TryFindAsync(
            this CatalogContainer parent,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var parts = resourcePath.Split('#', count: 2);

            // path
            var pathMatch = _resourcePathEvaluator.Match(parts[0]);

            if (!pathMatch.Success)
                return default;

            var kind = RepresentationKind.Original;

            if (!string.IsNullOrWhiteSpace(pathMatch.Groups[4].Value))
            {
                var rawValue = pathMatch.Groups[4].Value;

                if (!Enum.TryParse(ToPascalCase(rawValue), out kind))
                    return default;
            }

            // fragment
            var baseResourceId = default(string);

            if (parts.Length == 2)
            {
                var fragmentMatch = _resourcePathFragmentEvaluator.Match(parts[1]);

                if (!fragmentMatch.Success)
                    return default;

                baseResourceId = fragmentMatch.Groups[1].Value;
            }

            // find catalog
            var catalogId = pathMatch.Groups[1].Value;
            var catalogContainer = await parent.TryFindCatalogContainerAsync(catalogId, cancellationToken);

            if (catalogContainer is null)
                return default;

            var lazyCatalogInfo = await catalogContainer.GetLazyCatalogInfoAsync(cancellationToken);

            if (lazyCatalogInfo is null)
                return default;

            // find base item
            CatalogItem? catalogItem;
            CatalogItem? baseCatalogItem = default;

            if (kind is RepresentationKind.Original)
            {
                if (!lazyCatalogInfo.Catalog.TryFind(parts[0], out catalogItem))
                    return default;
            }

            else
            {
                var resourceId = baseResourceId ?? "";
                var actualResourcePath = $"{pathMatch.Groups[1].Value}/{pathMatch.Groups[2].Value}/{resourceId}";

                if (!lazyCatalogInfo.Catalog.TryFind(actualResourcePath, out baseCatalogItem))
                    return default;

                var samplePeriod = DataModelExtensions.ToSamplePeriod(pathMatch.Groups[3].Value);
                var representation = new Representation(NexusDataType.FLOAT64, samplePeriod, kind);

                catalogItem = baseCatalogItem with 
                { 
                    Representation = representation
                };
            }

            return new CatalogItemRequest(catalogItem, baseCatalogItem, catalogContainer);
        }

        public static async Task<CatalogContainer?> TryFindCatalogContainerAsync(
            this CatalogContainer parent,
            string catalogId,
            CancellationToken cancellationToken)
        {
            var childCatalogContainers = await parent.GetChildCatalogContainersAsync(cancellationToken);

            var catalogContainer = childCatalogContainers
                .FirstOrDefault(catalogContainer => catalogId.StartsWith(catalogContainer.Id));

            if (catalogContainer is null)
                return default;

            else if (catalogContainer.Id == catalogId)
                return catalogContainer;

            else
                return await catalogContainer.TryFindCatalogContainerAsync(catalogId, cancellationToken);
        }

        public static string ToPascalCase(string input)
        {
            var camelCase = Regex.Replace(input, "_.", match => match.Value.Substring(1).ToUpper());
            var pascalCase = string.Concat(camelCase[0].ToString().ToUpper(), camelCase.AsSpan(1));

            return pascalCase;
        }
    }
}
