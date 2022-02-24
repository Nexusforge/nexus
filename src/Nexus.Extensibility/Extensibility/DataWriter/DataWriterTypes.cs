using Microsoft.Extensions.Logging;
using Nexus.DataModel;

namespace Nexus.Extensibility
{
    /// <summary>
    /// The starter package for a data writer.
    /// </summary>
    /// <param name="ResourceLocator">The resource locator.</param>
    /// <param name="Configuration">The configuration.</param>
    /// <param name="Logger">The logger.</param>
    public record DataWriterContext(
        Uri ResourceLocator,
        Dictionary<string, string> Configuration,
        ILogger Logger);

    /// <summary>
    /// A write request.
    /// </summary>
    /// <param name="CatalogItem">The catalog item to be written.</param>
    /// <param name="Data">The data to be written.</param>
    public record WriteRequest(
        CatalogItem CatalogItem,
        ReadOnlyMemory<double> Data);

    /// <summary>
    /// An attribute to provide the file format name to be display in the Nexus GUI.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class DataWriterFormatNameAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instances of the <see cref="DataWriterFormatNameAttribute"/>.
        /// </summary>
        /// <param name="formatName">The file format name to be display in the Nexus GUI.</param>
        public DataWriterFormatNameAttribute(string formatName)
        {
            FormatName = formatName;
        }

        /// <summary>
        /// Gets the file format name.
        /// </summary>
        public string FormatName { get; }
    }

    internal abstract class OptionAttribute : Attribute
    {
        public OptionAttribute(string configurationKey, string label)
        {
            ConfigurationKey = configurationKey;
            Label = label;
        }

        public string ConfigurationKey { get; }

        public string Label { get; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    internal class DataWriterSelectOptionAttribute : OptionAttribute
    {
        public DataWriterSelectOptionAttribute(string configurationKey, string label, string defaultValue, string[] keys, string[] values)
            : base(configurationKey, label)
        {
            DefaultValue = defaultValue;

            KeyValueMap = keys
                .Zip(values)
                .ToDictionary(entry => entry.First, entry => entry.Second);
        }

        public string DefaultValue { get; }

        public Dictionary<string, string> KeyValueMap { get; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    internal class DataWriterIntegerNumberInputOptionAttribute : OptionAttribute
    {
        public DataWriterIntegerNumberInputOptionAttribute(string configurationKey, string label, int defaultValue, int minmum, int maximum) 
            : base(configurationKey, label)
        {
            DefaultValue = defaultValue;

            Minmum = minmum;
            Maximum = maximum;
        }

        public int DefaultValue { get; }
        public int Minmum { get; }
        public int Maximum { get; }
    }
}
