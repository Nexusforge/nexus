using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Id,nq}")]
    public record Representation
    {
        #region Fields

        private static Regex _detailValidator = new Regex(@"^(?:[a-zA-Z][a-zA-Z0-9_]*)?$");
        private static HashSet<NexusDataType> _nexusDataTypeValues = new HashSet<NexusDataType>(Enum.GetValues<NexusDataType>());

        #endregion

        #region Constructors

        public Representation(NexusDataType dataType, TimeSpan samplePeriod, string? detail = null)
        {
            if (!_nexusDataTypeValues.Contains(dataType))
                throw new ArgumentException($"The identifier '{dataType}' is not valid.");

            this.DataType = dataType;

            if (samplePeriod.Equals(default))
                throw new ArgumentException($"The sample period '{samplePeriod}' is not valid.");

            this.SamplePeriod = samplePeriod;

            if (detail != null && !_detailValidator.IsMatch(detail))
                throw new ArgumentException($"The representation detail '{detail}' is not valid.");

            this.Detail = detail;
        }

        #endregion

        #region Properties

        [JsonIgnore]
        public string Id
        {
            get
            {
                return string.IsNullOrWhiteSpace(this.Detail)
                    ? $"{this.SamplePeriod.ToUnitString()}"
                    : $"{this.SamplePeriod.ToUnitString()}_{this.Detail}";
            }
        }

        public NexusDataType DataType { get; init; }

        public TimeSpan SamplePeriod { get; init; }

        public string? Detail { get; init; }

        [JsonIgnore]
        public int ElementSize => ((int)this.DataType & 0xFF) >> 3;

        internal BackendSource BackendSource { get; set; }

        #endregion

        #region "Methods"

        internal Representation DeepCopy()
        {
            return this;
        }

        #endregion
    }
}