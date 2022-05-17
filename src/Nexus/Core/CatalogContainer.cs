﻿using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using System.Diagnostics;
using System.Security.Claims;

namespace Nexus.Core
{
    [DebuggerDisplay("{Id,nq}")]
    internal class CatalogContainer
    {
        public const string RootCatalogId = "/";

        private SemaphoreSlim _semaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        private LazyCatalogInfo? _lazyCatalogInfo;
        private CatalogContainer[]? _childCatalogContainers;
        private ICatalogManager _catalogManager;
        private IDatabaseService _databaseService;
        private IDataControllerService _dataControllerService;

        public CatalogContainer(
            CatalogRegistration catalogRegistration,
            ClaimsPrincipal? owner,
            DataSourceRegistration dataSourceRegistration,
            CatalogMetadata metadata,
            ICatalogManager catalogManager,
            IDatabaseService databaseService,
            IDataControllerService dataControllerService)
        {
            Id = catalogRegistration.Path;
            Title = catalogRegistration.Title;
            IsTransient = catalogRegistration.IsTransient;
            Owner = owner;
            DataSourceRegistration = dataSourceRegistration;
            Metadata = metadata;

            _catalogManager = catalogManager;
            _databaseService = databaseService;
            _dataControllerService = dataControllerService;
        }

        public string Id { get; }
        public string Title { get; }
        public bool IsTransient { get; }

        public ClaimsPrincipal? Owner { get; }

        public string PhysicalName => Id.TrimStart('/').Replace('/', '_');

        public DataSourceRegistration DataSourceRegistration { get; }

        public CatalogMetadata Metadata { get; internal set; }

        public static CatalogContainer CreateRoot(ICatalogManager catalogManager, IDatabaseService databaseService)
        {
            return new CatalogContainer(new CatalogRegistration(RootCatalogId, string.Empty), default!, default!, default!, catalogManager, databaseService, default!);
        }

        public async Task<IEnumerable<CatalogContainer>> GetChildCatalogContainersAsync(
            CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync();

            try
            {
                if (IsTransient || _childCatalogContainers is null)
                    _childCatalogContainers = await _catalogManager.GetCatalogContainersAsync(this, cancellationToken);

                return _childCatalogContainers;
            }
            finally
            {
                _semaphore.Release();
            }
        }

#warning Use Lazy instead?
        public async Task<LazyCatalogInfo> GetLazyCatalogInfoAsync(CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync();

            try
            {
                await EnsureLazyCatalogInfoAsync(cancellationToken);

                var lazyCatalogInfo = _lazyCatalogInfo;

                if (lazyCatalogInfo is null)
                    throw new Exception("this should never happen");

                return lazyCatalogInfo;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task UpdateMetadataAsync(CatalogMetadata metadata)
        {
            await _semaphore.WaitAsync();

            try
            {
                // persist
                using var stream = _databaseService.WriteCatalogMetadata(Id);
                await JsonSerializerHelper.SerializeIntendedAsync(stream, metadata);

                // assign
                Metadata = metadata;

                // trigger merging of catalog and catalog overrides
                _lazyCatalogInfo = default;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task EnsureLazyCatalogInfoAsync(CancellationToken cancellationToken)
        {
            if (IsTransient || _lazyCatalogInfo is null)
            {
                var catalogBegin = default(DateTime);
                var catalogEnd = default(DateTime);

                using var controller = await _dataControllerService.GetDataSourceControllerAsync(DataSourceRegistration, cancellationToken);
                var catalog = await controller.GetCatalogAsync(Id, cancellationToken);

                // get begin and end of project
                var catalogTimeRange = await controller.GetTimeRangeAsync(catalog.Id, cancellationToken);

                // merge time range
                if (catalogBegin == DateTime.MinValue)
                    catalogBegin = catalogTimeRange.Begin;

                else
                    catalogBegin = new DateTime(Math.Min(catalogBegin.Ticks, catalogTimeRange.Begin.Ticks), DateTimeKind.Utc);

                if (catalogEnd == DateTime.MinValue)
                    catalogEnd = catalogTimeRange.End;

                else
                    catalogEnd = new DateTime(Math.Max(catalogEnd.Ticks, catalogTimeRange.End.Ticks), DateTimeKind.Utc);

                // merge catalog
                if (Metadata?.Overrides is not null)
                    catalog = catalog.Merge(Metadata.Overrides);

                _lazyCatalogInfo = new LazyCatalogInfo(catalogBegin, catalogEnd, catalog);
            }
        }
    }
}
