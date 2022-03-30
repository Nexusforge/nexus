using System.Text.RegularExpressions;

namespace Nexus.Core
{
    internal static class CatalogContainerExtensions
    {
        private static Regex _resourcePathEvaluator = new Regex(@"(.*)\/([0-9]+_[a-zA-Z]+)(?:_(.+))?", RegexOptions.Compiled);

        public static async Task<CatalogItemRequest> FindAsync(
            this CatalogContainer parent, 
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var request = await parent.TryFindAsync(resourcePath, cancellationToken);

            if (request is null)
                throw new Exception($"The resource path {resourcePath} could not be found.");

            return request;
        }

        public static async Task<CatalogItemRequest?> TryFindAsync(
            this CatalogContainer parent,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var match = _resourcePathEvaluator.Match(resourcePath);

            if (!match.Success)
                return default;

            var kind = RepresentationKind.Original;

            if (match.Groups.Count == 4)
            {
                var rawValue = match.Groups[3].Value;

                if (!Enum.TryParse(rawValue, out kind))
                    return default;
            }

            var catalogId = match.Groups[1].Value;
            var catalogContainer = await parent.TryFindCatalogContainerAsync(catalogId, cancellationToken);

            if (catalogContainer is null)
                return default;

            var catalogInfo = await catalogContainer.GetCatalogInfoAsync(cancellationToken);

            if (catalogInfo is null)
                return default;

            var actualResourcePath = $"{match.Groups[1].Value}/{match.Groups[2].Value}";

            if (!catalogInfo.Catalog.TryFind(actualResourcePath, out var catalogItem))
                return default;

            return new CatalogItemRequest(catalogItem, catalogContainer, kind);
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
    }
}
