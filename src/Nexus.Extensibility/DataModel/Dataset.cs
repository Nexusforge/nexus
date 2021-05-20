using Nexus.Extensibility;
using Nexus.Infrastructure;
using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Id,nq}")]
    public class Dataset
    {
        #region "Constructors"

        public Dataset(string id, Channel channel)
        {
            this.Id = id;
            this.Channel = channel;
        }

        private Dataset()
        {
            //
        }

        #endregion

        #region Properties

        public string Id { get; }

        public NexusDataType DataType { get; set; }

        [JsonIgnore]
        public Channel Channel { get; internal set; }

        internal DataSourceRegistration Registration { get; set; }

        #endregion

        #region "Methods"

#warning Encode SamplesPerDay in Dataset instead of name?

        internal SampleRateContainer GetSampleRate(bool ensureNonZeroIntegerHz = false)
        {
            return new SampleRateContainer(this.Id, ensureNonZeroIntegerHz);
        }

        public string GetPath()
        {
            return $"{this.Channel.GetPath()}/{this.Id}";
        }

        public void Merge(Dataset dataset)
        {
            if (this.Channel.Id != dataset.Channel.Id
                || this.DataType != dataset.DataType)
                throw new Exception("The datasets to be merged are not equal.");
        }

        #endregion
    }
}