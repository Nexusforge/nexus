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

        private TimeSpan _samplePeriod;
        private string _detail;
        private NexusDataType _dataType;

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

        public TimeSpan SamplePeriod
        {
            get
            {
                return _samplePeriod;
            }
            init
            {
                if (value.Equals(default))
                    throw new ArgumentException($"The sample period '{value}' is not valid.");

                _samplePeriod = value;
            }
        }

        public string? Detail
        {
            get
            {
                return _detail;
            }
            init
            {
                if (value != null && !_detailValidator.IsMatch(value))
                    throw new ArgumentException($"The representation detail '{value}' is not valid.");

                _detail = value;
            }
        }

        public NexusDataType DataType
        {
            get
            {
                return _dataType;
            }
            init
            {
                if (!_nexusDataTypeValues.Contains(value))
                    throw new ArgumentException($"The identifier '{value}' is not valid.");

                _dataType = value;
            }
        }

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