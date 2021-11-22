using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Nexus.DataModel
{
    /// <summary>
    /// A representation is part of a resource.
    /// </summary>
    [DebuggerDisplay("{Id,nq}")]
    public record Representation
    {
        #region Fields

        private static Regex _detailValidator = new Regex(@"^(?:[a-zA-Z][a-zA-Z0-9_]*)?$");
        private static HashSet<NexusDataType> _nexusDataTypeValues = new HashSet<NexusDataType>(Enum.GetValues<NexusDataType>());

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="Representation"/>.
        /// </summary>
        /// <param name="dataType">The <see cref="NexusDataType"/>.</param>
        /// <param name="samplePeriod">The sample period.</param>
        /// <param name="detail">A more detailed identifier like "min", "max", "mean" or "std".</param>
        /// <param name="isPrimary">Indicates the primary representation to be used for aggregations, which is only relevant for resources with multiple representations.</param>
        /// <exception cref="ArgumentException">Thrown when the resource identifier, the sample period or the detail values are not valid.</exception>
        public Representation(NexusDataType dataType, TimeSpan samplePeriod, string? detail = null, bool isPrimary = false)
        {
            if (!_nexusDataTypeValues.Contains(dataType))
                throw new ArgumentException($"The identifier '{dataType}' is not valid.");

            this.DataType = dataType;

            if (samplePeriod.Equals(default))
                throw new ArgumentException($"The sample period '{samplePeriod}' is not valid.");

            this.SamplePeriod = samplePeriod;

            if (detail is not null && !_detailValidator.IsMatch(detail))
                throw new ArgumentException($"The representation detail '{detail}' is not valid.");

            this.Detail = detail;
            this.IsPrimary = isPrimary;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the identifer of the representation. It is constructed using the sample period and the optional detail.
        /// </summary>
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

        /// <summary>
        /// Gets the data type.
        /// </summary>
        public NexusDataType DataType { get; }

        /// <summary>
        /// Gets the sample period.
        /// </summary>
        public TimeSpan SamplePeriod { get; }

        /// <summary>
        /// Gets the detail.
        /// </summary>
        public string? Detail { get; }

        /// <summary>
        /// Gets a value which indicates the primary representation to be used for aggregations. The value of this property is only relevant for resources with multiple representations.
        /// </summary>
        public bool IsPrimary { get; }

        /// <summary>
        /// Gets the number of bits per element.
        /// </summary>
        [JsonIgnore]
        public int ElementSize => ((int)this.DataType & 0xFF) >> 3;

        #endregion

        #region "Methods"

        internal Representation DeepCopy()
        {
            return this;
        }

        #endregion
    }
}