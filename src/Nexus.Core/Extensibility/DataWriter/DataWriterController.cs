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
            TimeSpan samplePeriod,
            TimeSpan filePeriod,
            List<DatasetPipeReader> datasetPipeReaders,
            CancellationToken cancellationToken)
        {
            /* validation */
            foreach (var datasetPipeWriters in datasetPipeReaders)
            {
                if (datasetPipeWriters.DatasetRecord.Dataset.GetSampleRate().Period != samplePeriod)
                    throw new ValidationException("All datasets must be of the same sample period.");
            }

            if (!datasetPipeReaders.Any())
                return;

            DataWriterController.ValidateParameters(begin, samplePeriod, filePeriod);

            /* go */
            var datasetRecords = datasetPipeReaders
                .Select(datasetPipeReader => datasetPipeReader.DatasetRecord)
                .ToArray();

            var consumedPeriod = TimeSpan.Zero;

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
                    await this.DataWriter.OpenAsync(fileBegin, samplePeriod, datasetRecords.ToArray() /* copy */, cancellationToken);

                    _lastFileBegin = fileBegin;
                }

                /* write */
                var readResultTasks = datasetPipeReaders
                    .Select(datasetPipeReader => datasetPipeReader.DataReader.ReadAsync(cancellationToken))
                    .ToArray();

                var readResults = await NexusCoreUtilities.WhenAll(readResultTasks).ConfigureAwait(false);

                if (readResults.Any(readResult => readResult.IsCompleted))
                    break;

                var elementCount = readResults.Min(readResult => readResult.Buffer.First.Cast<double>().Length);

                if (elementCount == 0)
                    throw new ValidationException("The pipe is empty.");

                var fileOffset = currentBegin - fileBegin;
                var remainingFilePeriod = filePeriod - fileOffset;
                var remainingBufferPeriod = samplePeriod * elementCount;
                var currentPeriod = new TimeSpan(Math.Min(remainingFilePeriod.Ticks, remainingBufferPeriod.Ticks));

                var writeRequests = datasetPipeReaders.Zip(readResults).Select(zipped =>
                {
                    var (datasetPipeReader, readResult) = zipped;

                    return new WriteRequest(
                        datasetPipeReader.DatasetRecord, 
                        readResult.Buffer.First.Cast<double>().Slice(elementCount));
                });

                await this.DataWriter.WriteAsync(fileOffset, samplePeriod, writeRequests.ToArray() /* copy */, cancellationToken);

                /* advance */
                foreach (var ((_, dataReader), readResult) in datasetPipeReaders.Zip(readResults))
                {
                    dataReader.AdvanceTo(readResult.Buffer.GetPosition(elementCount * sizeof(double)));
                }

                consumedPeriod += currentPeriod;
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