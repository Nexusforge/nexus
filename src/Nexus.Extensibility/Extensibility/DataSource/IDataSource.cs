﻿using Nexus.DataModel;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    /// <summary>
    /// A data source.
    /// </summary>
    public interface IDataSource : IExtension
    {
        /// <summary>
        /// Invoked by Nexus right after construction to provide the context.
        /// </summary>
        /// <param name="context">The <paramref name="context"/>.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns>The task.</returns>
        Task SetContextAsync(
            DataSourceContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// Gets the catalog identifiers that are located below <paramref name="path"/>.
        /// </summary>
        /// <param name="path">The parent path for which to return catalog identifiers.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns>The catalog identifiers task.</returns>
        Task<string[]> GetCatalogIdsAsync(
            string path,
            CancellationToken cancellationToken);

        /// <summary>
        /// Gets the requested <see cref="ResourceCatalog"/>.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns>The catalog request task.</returns>
        Task<ResourceCatalog> GetCatalogAsync(
            string catalogId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Gets the time range of the <see cref="ResourceCatalog"/>.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns>The time range task.</returns>
        Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(
            string catalogId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Gets the availability of the <see cref="ResourceCatalog"/>.
        /// </summary>
        /// <param name="catalogId">The catalog identifier</param>
        /// <param name="begin">The begin of the availability period..</param>
        /// <param name="end">The end of the availability period.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns>The availability task.</returns>
        Task<double> GetAvailabilityAsync(
            string catalogId, 
            DateTime begin,
            DateTime end,                             
            CancellationToken cancellationToken);

        /// <summary>
        /// Performs a number of read requests.
        /// </summary>
        /// <param name="begin">The beginning of the period to read.</param>
        /// <param name="end">The end of the period to read.</param>
        /// <param name="requests">The array of read requests.</param>
        /// <param name="progress">An object to report the read progress.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns>The task.</returns>
        Task ReadAsync(
            DateTime begin,
            DateTime end,
            ReadRequest[] requests,
            IProgress<double> progress,
            CancellationToken cancellationToken);
    }
}
