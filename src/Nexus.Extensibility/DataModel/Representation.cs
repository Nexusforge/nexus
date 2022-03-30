using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Nexus.DataModel
{
    /// <summary>
    /// A representation is part of a resource.
    /// </summary>
    [DebuggerDisplay("{Id,nq}")]
    public record Representation
    {
        #region Fields

        private static HashSet<NexusDataType> _nexusDataTypeValues = new HashSet<NexusDataType>(Enum.GetValues<NexusDataType>());

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="Representation"/>.
        /// </summary>
        /// <param name="dataType">The <see cref="NexusDataType"/>.</param>
        /// <param name="samplePeriod">The sample period.</param>
        /// <exception cref="ArgumentException">Thrown when the resource identifier, the sample period or the detail values are not valid.</exception>
        internal Representation(NexusDataType dataType, TimeSpan samplePeriod)
        {
            if (!_nexusDataTypeValues.Contains(dataType))
                throw new ArgumentException($"The identifier {dataType} is not valid.");

            DataType = dataType;

            if (samplePeriod.Equals(default))
                throw new ArgumentException($"The sample period {samplePeriod} is not valid.");

            SamplePeriod = samplePeriod;
            Id = SamplePeriod.ToUnitString();
        }

        #endregion

        #region Properties

        /// <summary>
        /// The identifer of the representation. It is constructed using the sample period.
        /// </summary>
        [JsonIgnore]
        public string Id { get; }

        /// <summary>
        /// The data type.
        /// </summary>
        public NexusDataType DataType { get; }

        /// <summary>
        /// The sample period.
        /// </summary>
        public TimeSpan SamplePeriod { get; }

        /// <summary>
        /// The number of bits per element.
        /// </summary>
        [JsonIgnore]
        public int ElementSize => ((int)DataType & 0xFF) >> 3;

        #endregion

        #region "Methods"

        internal Representation DeepCopy()
        {
            return this;
        }

        #endregion
    }
}