using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Extensibility
{
    public record DataWriterContext(
        Uri ResourceLocator,
        Dictionary<string, string> Configuration,
        ILogger Logger);

    public record WriteRequest(
        CatalogItem CatalogItem,
        ReadOnlyMemory<double> Data);

    [AttributeUsage(AttributeTargets.Class)]
    public class DataWriterFormatNameAttribute : Attribute
    {
        public DataWriterFormatNameAttribute(string formatName)
        {
            this.FormatName = formatName;
        }

        public string FormatName { get; }
    }

    internal abstract class OptionAttribute : Attribute
    {
        public OptionAttribute(string configurationKey, string label)
        {
            this.ConfigurationKey = configurationKey;
            this.Label = label;
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
            this.DefaultValue = defaultValue;

            this.KeyValueMap = keys
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
            this.DefaultValue = defaultValue;

            this.Minmum = minmum;
            this.Maximum = maximum;
        }

        public int DefaultValue { get; }
        public int Minmum { get; }
        public int Maximum { get; }
    }
}
