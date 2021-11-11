using Nexus.DataModel;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    /// <summary>
    /// A data writer.
    /// </summary>
    public interface IDataWriter : IExtension
    {
        /// <summary>
        /// Invoked by Nexus right after construction to provide the context.
        /// </summary>
        /// <param name="context">The <paramref name="context"/>.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns>The task.</returns>
        Task SetContextAsync(
            DataWriterContext context, 
            CancellationToken cancellationToken);

        /// <summary>
        /// Opens or creates a file for the specified parameters.
        /// </summary>
        /// <param name="fileBegin">The beginning of the file.</param>
        /// <param name="samplePeriod">The sample period.</param>
        /// <param name="catalogItems">An array of catalog items to allow preparation of the file header.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns>The task.</returns>
        Task OpenAsync(
            DateTime fileBegin, 
            TimeSpan samplePeriod,
            CatalogItem[] catalogItems, 
            CancellationToken cancellationToken);

        /// <summary>
        /// Performs a number of write requests.
        /// </summary>
        /// <param name="fileOffset">The offset within the current file.</param>
        /// <param name="requests">The array of write requests.</param>
        /// <param name="progress">An object to report the read progress.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns>The task.</returns>
        Task WriteAsync(
            TimeSpan fileOffset,
            WriteRequest[] requests,
            IProgress<double> progress,
            CancellationToken cancellationToken);

        /// <summary>
        /// Closes the current and flushes the data to disk.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns>The task.</returns>
        Task CloseAsync(
            CancellationToken cancellationToken);
    }
}