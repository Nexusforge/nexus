﻿using Nexus.DataModel;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Core
{
    internal static class CatalogContainersExtensions
    {
        public static async Task<CatalogItem> FindAsync(
            this CatalogContainer parent, 
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var catalogItem = await parent.TryFindAsync(resourcePath, cancellationToken);

            if (catalogItem is null)
                throw new Exception($"The resource path {resourcePath} could not be found.");

            return catalogItem;
        }

        public static async Task<CatalogItem?> TryFindAsync(
            this CatalogContainer parent,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var pathParts = resourcePath.Split('/');
            var catalogId = string.Join('/', pathParts[..^2]);
            var catalogContainer = await parent.TryFindCatalogContainerAsync(catalogId, cancellationToken);

            if (catalogContainer is null)
                return default;

            var catalogInfo = await catalogContainer.GetCatalogInfoAsync(cancellationToken);

            if (catalogInfo is not null)
            {
                _ = catalogInfo.Catalog.TryFind(resourcePath, out var catalogItem);
                return catalogItem;
            }

            else
            {
                return default;
            }
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
