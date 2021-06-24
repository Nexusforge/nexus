using Nexus.Extensibility;
using Nexus.Infrastructure;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Id,nq}")]
    public record Dataset
    {
        #region Properties

        public string Id { get; init; }

        public NexusDataType DataType { get; init; }

        [JsonIgnore]
        public int ElementSize => ((int)this.DataType & 0xFF) >> 3;

        [JsonIgnore]
        public Channel Channel { get; internal set; }

        internal BackendSource BackendSource { get; set; }

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

        #endregion
    }
}