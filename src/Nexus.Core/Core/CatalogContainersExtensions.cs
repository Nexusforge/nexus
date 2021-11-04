using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Core
{
    internal static class CatalogContainersExtensions
    {
        public static async Task<CatalogItem> FindAsync(
            this IEnumerable<CatalogContainer> catalogContainers, 
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var catalogItem = await catalogContainers.TryFindAsync(resourcePath, cancellationToken);

            if (catalogItem is null)
                throw new Exception($"The resource path {resourcePath} could not be found.");

            return catalogItem;
        }

        public static async Task<CatalogItem?> TryFindAsync(
            this IEnumerable<CatalogContainer> catalogContainers,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var pathParts = resourcePath.Split('/');
            var catalogId = $"{pathParts[0]}/{pathParts[1]}/{pathParts[2]}";
            var catalogContainer = catalogContainers.FirstOrDefault(container => container.Id == catalogId);

            if (catalogContainer == null)
                return default;

            var catalog = await catalogContainer.GetCatalogAsync(cancellationToken);

            if (catalog is not null)
            {
                _ = catalog.TryFind(resourcePath, out var catalogItem);
                return catalogItem;
            }

            else
            {
                return default;
            }
        }
    }
}
