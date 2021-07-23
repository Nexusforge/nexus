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

        private static Regex _idValidator = new Regex(@"^[0-9]+_(?:Hz|min|s|ms|us|ns)(?:_[a-zA-Z][a-zA-Z0-9_]*)?$");
        private static HashSet<NexusDataType> _nexusDataTypeValues = new HashSet<NexusDataType>(Enum.GetValues<NexusDataType>());

        private string _id;
        private NexusDataType _dataType;

        #endregion

        #region Properties

        public string Id
        {
            get
            {
                return _id;
            }
            init
            {
                if (!_idValidator.IsMatch(value))
                    throw new ArgumentException($"The identifier '{value}' is not valid.");

                _id = value;
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

        internal TimeSpan GetSamplePeriod()
        {
            var parts = this.Id.Split("_");
            var number = long.Parse(parts[0]);

            if (number < 1)
                throw new Exception($"The frequency value '{number}' is invalid.");

            var unit = parts[1];

            if (unit == "Hz")
            {
                return TimeSpan.FromTicks(1000 * 1000 * 1000 / 100 / number);
            }
            else
            {
                return unit switch
                {
                    "ns"    => TimeSpan.FromTicks(number / 100),
                    "us"    => TimeSpan.FromTicks(number * 1000 / 100),
                    "ms"    => TimeSpan.FromTicks(number * 1000 * 1000 / 100),
                    "s"     => TimeSpan.FromTicks(number * 1000 * 1000 * 1000 / 100),
                    "min"   => TimeSpan.FromTicks(number *   60 * 1000 * 1000 * 1000 / 100),
                    _       => throw new Exception($"The unit '{unit}' is not supported.")
                };
            }

        }

        internal Representation DeepCopy()
        {
            return this;
        }

        #endregion
    }
}