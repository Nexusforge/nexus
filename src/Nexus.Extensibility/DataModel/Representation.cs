using Nexus.Extensibility;
using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Id,nq}")]
    public record Representation
    {
        #region Properties

        public string Id { get; init; }

        public NexusDataType DataType { get; init; }

        [JsonIgnore]
        public int ElementSize => ((int)this.DataType & 0xFF) >> 3;

        internal BackendSource BackendSource { get; set; }

        #endregion

        #region "Methods"

#warning Encode sample period in Representation instead of name?

        internal TimeSpan GetSamplePeriod()
        {
            var parts = this.Id.Split("_");
            var frequency = parts[0];
            parts = frequency.Split(" ");

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