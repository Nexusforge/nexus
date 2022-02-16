using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.Utilities;
using System.ComponentModel.DataAnnotations;

namespace Nexus.Extensibility
{
#warning Add "CheckFileSize" method (e.g. for Famos).

    internal class DataWriterController : IDataWriterController
    {
        #region Constructors

        public DataWriterController(
            IDataWriter dataWriter, 
            Uri resourceLocator, 
            Dictionary<string, string> configuration, 
            ILogger<DataWriterController> logger)
        {
            this.DataWriter = dataWriter;
            this.ResourceLocator = resourceLocator;
            this.Configuration = configuration;
            this.Logger = logger;
        }

        #endregion

        #region Properties

        private IDataWriter DataWriter { get; }

        private Uri ResourceLocator { get; }

        private Dictionary<string, string> Configuration { get; }

        private ILogger Logger { get; }

        #endregion

        #region Methods

        public async Task InitializeAsync(ILogger logger, CancellationToken cancellationToken)
        {
            var context = new DataWriterContext(
                ResourceLocator: this.ResourceLocator,
                Configuration: this.Configuration,
                Logger: logger);

            await this.DataWriter.SetContextAsync(context, cancellationToken);
        }

        public async Task WriteAsync(
            DateTime begin,
            DateTime end,
            TimeSpan samplePeriod,
            TimeSpan filePeriod,
            CatalogItemPipeReader[] catalogItemPipeReaders,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            /* validation */
            if (!catalogItemPipeReaders.Any())
                return;

            foreach (var catalogItemPipeReader in catalogItemPipeReaders)
            {
                if (catalogItemPipeReader.CatalogItem.Representation.SamplePeriod != samplePeriod)
                    throw new ValidationException("All representations must be of the same sample period.");
            }

            DataWriterController.ValidateParameters(begin, samplePeriod, filePeriod);

            /* periods */
            var totalPeriod = end - begin;
            this.Logger.LogDebug("The total period is {TotalPeriod}", totalPeriod);

            var consumedPeriod = TimeSpan.Zero;
            var currentPeriod = default(TimeSpan);

            /* progress */
            var dataWriterProgress = new Progress<double>();

            /* no need to remove handler because of short lifetime of IDataWriter */
            dataWriterProgress.ProgressChanged += (sender, progressValue) =>
            {
                var baseProgress = consumedPeriod.Ticks / (double)totalPeriod.Ticks;
                var relativeProgressFactor = currentPeriod.Ticks / (double)totalPeriod.Ticks;
                var relativeProgress = progressValue * relativeProgressFactor;

                progress?.Report(baseProgress + relativeProgress);
            };

            /* catalog items */
            var catalogItems = catalogItemPipeReaders
                .Select(catalogItemPipeReader => catalogItemPipeReader.CatalogItem)
                .ToArray();

            /* go */
            var lastFileBegin = default(DateTime);

            await NexusCoreUtilities.FileLoopAsync(begin, end, filePeriod,
                async (fileBegin, fileOffset, duration) =>
            {
                /* Concept: It never happens that the data of a read operation is spreaded over 
                 * multiple buffers. However, it may happen that the data of multiple read 
                 * operations are copied into a single buffer (important to ensure that multiple 
                 * bytes of a single value are always copied together). When the first buffer
                 * is (partially) read, call the "PipeReader.Advance" function to tell the pipe
                 * the number of bytes we have consumed. This way we slice our way through 
                 * the buffers so it is OK to only ever read the first buffer of a read result.
                 */

                cancellationToken.ThrowIfCancellationRequested();

                var currentBegin = fileBegin + fileOffset;
                this.Logger.LogTrace("Process period {CurrentBegin} to {CurrentEnd}", currentBegin, currentBegin + duration);

                /* close / open */
                if (fileBegin != lastFileBegin)
                {
                    /* close */
                    if (lastFileBegin != default)
                        await this.DataWriter.CloseAsync(cancellationToken);

                    /* open */
                    await this.DataWriter.OpenAsync(
                        fileBegin,
                        filePeriod,
                        samplePeriod,
                        catalogItems,
                        cancellationToken);
                }

                lastFileBegin = fileBegin;

                /* loop */
                var consumedFilePeriod = TimeSpan.Zero;
                var remainingPeriod = duration;

                while (remainingPeriod > TimeSpan.Zero)
                {
                    /* read */
                    var readResultTasks = catalogItemPipeReaders
                        .Select(catalogItemPipeReader => catalogItemPipeReader.DataReader.ReadAsync(cancellationToken))
                        .ToArray();

                    var readResults = await NexusCoreUtilities.WhenAll(readResultTasks);
                    var bufferPeriod = readResults.Min(readResult => readResult.Buffer.First.Cast<byte, double>().Length) * samplePeriod;

                    if (bufferPeriod == default)
                        throw new ValidationException("The pipe is empty.");

                    /* write */
                    currentPeriod = new TimeSpan(Math.Min(remainingPeriod.Ticks, bufferPeriod.Ticks));
                    var currentLength = (int)(currentPeriod.Ticks / samplePeriod.Ticks);

                    var requests = catalogItemPipeReaders.Zip(readResults).Select(zipped =>
                    {
                        var (catalogItemPipeReader, readResult) = zipped;

                        var writeRequest = new WriteRequest(
                            catalogItemPipeReader.CatalogItem,
                            readResult.Buffer.First.Cast<byte, double>().Slice(0, currentLength));

                        return writeRequest;
                    }).ToArray();

                    await this.DataWriter.WriteAsync(
                        fileOffset + consumedFilePeriod,
                        requests,
                        dataWriterProgress,
                        cancellationToken);

                    /* advance */
                    foreach (var ((_, dataReader), readResult) in catalogItemPipeReaders.Zip(readResults))
                    {
                        dataReader.AdvanceTo(readResult.Buffer.GetPosition(currentLength * sizeof(double)));
                    }

                    /* update loop state */
                    consumedPeriod += currentPeriod;
                    consumedFilePeriod += currentPeriod;
                    remainingPeriod -= currentPeriod;

                    progress?.Report(consumedFilePeriod.Ticks / (double)duration.Ticks);
                }
            });

            /* close */
            await this.DataWriter.CloseAsync(cancellationToken);

            foreach (var (_, dataReader) in catalogItemPipeReaders)
            {
                await dataReader.CompleteAsync();
            }
        }

        private static void ValidateParameters(DateTime begin, TimeSpan samplePeriod, TimeSpan filePeriod)
        {
            if (begin.Ticks % samplePeriod.Ticks != 0)
                throw new ValidationException("The begin parameter must be a multiple of the sample period.");

            if (filePeriod.Ticks % samplePeriod.Ticks != 0)
                throw new ValidationException("The file period parameter must be a multiple of the sample period.");
        }

        #endregion

        #region IDisposable

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    var disposable = this.DataWriter as IDisposable;
                    disposable?.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}