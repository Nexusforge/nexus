using Microsoft.Extensions.Logging;
using Nexus.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
#warning Add "CheckFileSize" method (e.g. for Famos).

    public class DataWriterController
    {
        #region Fields

        private DateTime _lastFileBegin;

        #endregion

        #region Constructors

        public DataWriterController(IDataWriter dataWriter, ILogger logger)
        {
            this.DataWriter = dataWriter;
            this.Logger = logger;
        }

        #endregion

        #region Properties

        private IDataWriter DataWriter { get; }

        private ILogger Logger { get; }

        #endregion

        #region Methods

        public async Task WriteAsync(
            DateTime begin,
            DateTime end,
            TimeSpan samplePeriod,
            TimeSpan filePeriod,
            List<DatasetPipeReader> datasetPipeReaders,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            /* validation */
            foreach (var datasetPipeWriters in datasetPipeReaders)
            {
                if (datasetPipeWriters.DatasetRecord.Dataset.GetSamplePeriod() != samplePeriod)
                    throw new ValidationException("All datasets must be of the same sample period.");
            }

            if (!datasetPipeReaders.Any())
                return;

            DataWriterController.ValidateParameters(begin, samplePeriod, filePeriod);

            /* periods */
            var totalPeriod = end - begin;
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

                progress.Report(baseProgress + relativeProgress);
            };

            /* misc */
            var datasetRecords = datasetPipeReaders
                .Select(datasetPipeReader => datasetPipeReader.DatasetRecord)
                .ToArray();

#warning Pass license also!

            var groupedDatasetRecords = datasetRecords
                        .GroupBy(datasetRecord => datasetRecord.Catalog)
                        .Select(group => new DatasetRecordGroup(group.Key, "", group.ToArray()))
                        .ToArray();

            /* go */
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                /* pre-calculations */
                var currentBegin = begin + consumedPeriod;

                DateTime fileBegin;

                if (filePeriod == TimeSpan.Zero)
                    fileBegin = _lastFileBegin != DateTime.MinValue ? _lastFileBegin : begin;

                else
                    fileBegin = currentBegin.RoundDown(filePeriod);

                if (fileBegin != _lastFileBegin)
                {
                    /* close */
                    if (_lastFileBegin != DateTime.MinValue)
                        await this.DataWriter.CloseAsync(cancellationToken);

                    /* open */
                    await this.DataWriter.OpenAsync(
                        fileBegin, 
                        samplePeriod,
                        groupedDatasetRecords,
                        cancellationToken);

                    _lastFileBegin = fileBegin;
                }

                /* read */
                var readResultTasks = datasetPipeReaders
                    .Select(datasetPipeReader => datasetPipeReader.DataReader.ReadAsync(cancellationToken))
                    .ToArray();

                var readResults = await NexusCoreUtilities.WhenAll(readResultTasks).ConfigureAwait(false);

                if (readResults.Any(readResult => readResult.IsCompleted))
                    break;

                /* write */
                var elementCount = readResults.Min(readResult => readResult.Buffer.First.Cast<double>().Length);

                if (elementCount == 0)
                    throw new ValidationException("The pipe is empty.");

                var fileOffset = currentBegin - fileBegin;
                var remainingFilePeriod = filePeriod - fileOffset;
                var bufferPeriod = samplePeriod * elementCount;
                currentPeriod = new TimeSpan(Math.Min(remainingFilePeriod.Ticks, bufferPeriod.Ticks));

                var requests = datasetPipeReaders.Zip(readResults).Select(zipped =>
                {
                    var (datasetPipeReader, readResult) = zipped;

                    return new WriteRequest(
                        datasetPipeReader.DatasetRecord, 
                        readResult.Buffer.First.Cast<double>().Slice(elementCount));
                });

                var groupedRequests = requests
                    .GroupBy(writeRequest => writeRequest.DatasetRecord.Catalog)
                    .Select(group => new WriteRequestGroup(group.Key, group.ToArray()))
                    .ToArray();

                await this.DataWriter.WriteAsync(
                    fileOffset, 
                    samplePeriod,
                    groupedRequests, 
                    dataWriterProgress,
                    cancellationToken);

                /* advance */
                foreach (var ((_, dataReader), readResult) in datasetPipeReaders.Zip(readResults))
                {
                    dataReader.AdvanceTo(readResult.Buffer.GetPosition(elementCount * sizeof(double)));
                }

                consumedPeriod += currentPeriod;
                progress.Report(consumedPeriod.Ticks / (double)totalPeriod.Ticks);
            }

            /* close */
            await this.DataWriter.CloseAsync(cancellationToken);

            foreach (var (_, dataReader) in datasetPipeReaders)
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
    }
}