﻿using Microsoft.Extensions.Logging;
using Nexus.Buffers;
using Nexus.DataModel;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Extensibility
{
#warning Add "CheckFileSize" method (e.g. for Famos).
    public abstract class DataWriterController : IDataWriter
    {
        #region "Fields"

        private DateTime _lastFileBegin;
        private DateTime _lastWrite;

        private List<Dataset> _datasets;

        #endregion

        #region "Constructors"

        public DataWriterController(ILogger logger)
        {
            this.BasePeriod = TimeSpan.FromSeconds(1);
        }

        #endregion

        #region "Properties"

        protected DataWriterContext DataWriterContext { get; private set; }

        protected TimeSpan BasePeriod { get; }

        #endregion

        #region "Methods"

        public void Configure(DataWriterContext dataWriterContext, List<Dataset> datasets)
        {
            this.DataWriterContext = dataWriterContext;
            _datasets = datasets;

            this.OnConfigure();
        }

        public void Write(DateTime begin, TimeSpan bufferPeriod, IList<IBuffer> buffers)
        {
            if (begin < _lastWrite)
                throw new ArgumentException(ErrorMessage.DataWriterExtensionLogicBase_DateTimeAlreadyWritten);

            if (begin != begin.RoundDown(this.BasePeriod))
                throw new ArgumentException(ErrorMessage.DataWriterExtensionLogicBase_DateTimeGranularityTooHigh);

            if (bufferPeriod.Milliseconds > 0)
                throw new ArgumentException(ErrorMessage.DataWriterExtensionLogicBase_DateTimeGranularityTooHigh);

            var bufferOffset = TimeSpan.Zero;

            var channelContexts = _datasets
                .Zip(buffers, (channelDescription, buffer) => new ChannelContext(channelDescription, buffer))
                .ToList();

            var channelContextGroups = channelContexts
                .GroupBy(channelContext => channelContext.ChannelDescription.SampleRate)
                .Select(group => new ChannelContextGroup(group.Key, group.ToList()))
                .ToList();

            while (bufferOffset < bufferPeriod)
            {
                var currentBegin = begin + bufferOffset;

                DateTime fileBegin;

                if (this.Settings.SingleFile)
                    fileBegin = _lastFileBegin != DateTime.MinValue ? _lastFileBegin : begin;
                else
                    fileBegin = currentBegin.RoundDown(this.Settings.FilePeriod);

                var fileOffset = currentBegin - fileBegin;

                var remainingFilePeriod = this.Settings.FilePeriod - fileOffset;
                var remainingBufferPeriod = bufferPeriod - bufferOffset;

                var period = new TimeSpan(Math.Min(remainingFilePeriod.Ticks, remainingBufferPeriod.Ticks));

                // ensure that file granularity is low enough for all sample rates
                foreach (var contextGroup in channelContextGroups)
                {
                    var sampleRate = contextGroup.SampleRate;
                    var totalSeconds = (int)Math.Round(this.Settings.FilePeriod.TotalSeconds, MidpointRounding.AwayFromZero);
                    var length = totalSeconds * sampleRate.SamplesPerSecond;

                    if (length < 1)
                        throw new Exception(ErrorMessage.DataWriterExtensionLogicBase_FileGranularityTooHigh);
                }

                // check if file must be created or updated
                if (fileBegin != _lastFileBegin)
                {
                    this.OnPrepareFile(fileBegin, channelContextGroups);

                    _lastFileBegin = fileBegin;
                }

                // write data
                foreach (var contextGroup in channelContextGroups)
                {
                    var sampleRate = contextGroup.SampleRate;

                    var actualFileOffset = this.TimeSpanToIndex(fileOffset, sampleRate);
                    var actualBufferOffset = this.TimeSpanToIndex(bufferOffset, sampleRate);
                    var actualPeriod = this.TimeSpanToIndex(period, sampleRate);

                    this.OnWrite(
                        contextGroup,
                        actualFileOffset,
                        actualBufferOffset,
                        actualPeriod
                    );

                    this.Logger.LogInformation($"data written to file");
                }

                bufferOffset += period;
            }

            _lastWrite = begin + bufferPeriod;
        }

        protected virtual void OnConfigure()
        {
            //
        }

        protected abstract void OnPrepareFile(DateTime startDateTime, List<ChannelContextGroup> channelContextGroupSet);

        protected abstract void OnWrite(ChannelContextGroup contextGroup, ulong fileOffset, ulong bufferOffset, ulong length);

        private ulong TimeSpanToIndex(TimeSpan timeSpan, SampleRateContainer sampleRate)
        {
            return (ulong)(timeSpan.TotalSeconds * (double)sampleRate.SamplesPerSecond);
        }

        #endregion
    }
}