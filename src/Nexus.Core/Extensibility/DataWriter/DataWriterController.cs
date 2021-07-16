﻿using Microsoft.Extensions.Logging;
using Nexus.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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

        public DataWriterController(IDataWriter dataWriter, BackendSource backendSource, ILogger logger)
        {
            this.DataWriter = dataWriter;
            this.BackendSource = backendSource;
            this.Logger = logger;
        }

        #endregion

        #region Properties

        private IDataWriter DataWriter { get; }

        private BackendSource BackendSource { get; }

        private ILogger Logger { get; }

        #endregion

        #region Methods

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            var context = new DataWriterContext()
            {
                ResourceLocator = this.BackendSource.ResourceLocator,
                Configuration = this.BackendSource.Configuration,
                Logger = this.Logger
            };

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
            foreach (var catalogItemPipeWriters in catalogItemPipeReaders)
            {
                if (catalogItemPipeWriters.CatalogItem.Representation.GetSamplePeriod() != samplePeriod)
                    throw new ValidationException("All representations must be of the same sample period.");
            }

            if (!catalogItemPipeReaders.Any())
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

                progress?.Report(baseProgress + relativeProgress);
            };

            /* catalog items */
            var catalogItems = catalogItemPipeReaders
                .Select(catalogItemPipeReader => catalogItemPipeReader.CatalogItem)
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
                        catalogItems,
                        cancellationToken);

                    _lastFileBegin = fileBegin;
                }

                /* read */
                var readResultTasks = catalogItemPipeReaders
                    .Select(catalogItemPipeReader => catalogItemPipeReader.DataReader.ReadAsync(cancellationToken))
                    .ToArray();

                var readResults = await NexusCoreUtilities.WhenAll(readResultTasks).ConfigureAwait(false);

                if (readResults.Any(readResult => readResult.IsCompleted))
                    break;

                /* write */
                var elementCount = readResults.Min(readResult => readResult.Buffer.First.Cast<byte, double>().Length);

                if (elementCount == 0)
                    throw new ValidationException("The pipe is empty.");

                var fileOffset = currentBegin - fileBegin;
                var remainingFilePeriod = filePeriod - fileOffset;
                var bufferPeriod = samplePeriod * elementCount;
                currentPeriod = new TimeSpan(Math.Min(remainingFilePeriod.Ticks, bufferPeriod.Ticks));

                var requests = catalogItemPipeReaders.Zip(readResults).Select(zipped =>
                {
                    var (catalogItemPipeReader, readResult) = zipped;

                    return new WriteRequest(
                        catalogItemPipeReader.CatalogItem, 
                        readResult.Buffer.First.Cast<byte, double>().Slice(elementCount));
                }).ToArray();

                await this.DataWriter.WriteAsync(
                    fileOffset, 
                    requests, 
                    dataWriterProgress,
                    cancellationToken);

                /* advance */
                foreach (var ((_, dataReader), readResult) in catalogItemPipeReaders.Zip(readResults))
                {
                    dataReader.AdvanceTo(readResult.Buffer.GetPosition(elementCount * sizeof(double)));
                }

                consumedPeriod += currentPeriod;
                progress?.Report(consumedPeriod.Ticks / (double)totalPeriod.Ticks);
            }

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
    }
}