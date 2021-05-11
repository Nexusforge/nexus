﻿using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Nexus.Database
{
    [DebuggerDisplay("{Id,nq}")]
    public class DatasetInfo : ProjectElement
    {
        #region "Constructors"

        public DatasetInfo(string id, ProjectElement parent) : base(id, parent)
        {
            //
        }

        private DatasetInfo()
        {
            //
        }

        #endregion

        #region Properties

        public NexusDataType DataType { get; set; }

        internal DataReaderRegistration Registration { get; set; }

        #endregion

        #region "Methods"

#warning Encode SamplesPerDay in DatasetInfo instead of name?

        public SampleRateContainer GetSampleRate(bool ensureNonZeroIntegerHz = false)
        {
            return new SampleRateContainer(this.Id, ensureNonZeroIntegerHz);
        }

        public override string GetPath()
        {
            return $"{this.Parent.GetPath()}/{this.Id}";
        }

        public override IEnumerable<ProjectElement> GetChilds()
        {
            return new List<DatasetInfo> { };
        }

        public void Merge(DatasetInfo dataset)
        {
            if (this.Parent.Id != dataset.Parent.Id
                || this.DataType != dataset.DataType)
                throw new Exception("The datasets to be merged are not equal.");
        }

        #endregion
    }
}