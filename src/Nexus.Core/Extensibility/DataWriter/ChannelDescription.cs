﻿using Nexus.Buffers;
using Nexus.Infrastructure;
using System;

namespace Nexus.Extensibility
{
    public class ChannelDescription
    {
        #region "Constructors"

        public ChannelDescription(Guid guid,
                                   string channelName,
                                   string datasetName,
                                   string group,
                                   NexusDataType dataType,
                                   SampleRateContainer sampleRate,
                                   string unit,
                                   BufferType bufferType)
        {
            this.Guid = guid;
            this.ChannelName = channelName;
            this.DatasetName = datasetName;
            this.Group = group;
            this.DataType = dataType;
            this.SampleRate = sampleRate;
            this.Unit = unit;
            this.BufferType = bufferType;
        }

        #endregion

        #region "Properties"

        public Guid Guid { get; set; }

        public string ChannelName { get; private set; }

        public string DatasetName { get; private set; }

        public string Group { get; private set; }

        public NexusDataType DataType { get; set; }

        public SampleRateContainer SampleRate { get; private set; }

        public string Unit { get; private set; }

        public BufferType BufferType { get; set; }

        #endregion
    }
}