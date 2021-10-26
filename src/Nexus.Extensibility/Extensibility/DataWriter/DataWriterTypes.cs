using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Extensibility
{
    public record DataWriterContext()
    {
        public Uri ResourceLocator { get; init; }
        public Dictionary<string, string> Configuration { get; init; }
        public ILogger Logger { get; init; }
    }

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

    internal abstract class OptionAttrbute : Attribute
    {
        public OptionAttrbute(string configurationKey, string label)
        {
            this.ConfigurationKey = configurationKey;
            this.Label = label;
        }

        public string ConfigurationKey { get; }

        public string Label { get; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    internal class DataWriterSelectOptionAttribute : OptionAttrbute
    {
        public DataWriterSelectOptionAttribute(string configurationKey, string label, string defaultKey, string[] keys, string[] values)
            : base(configurationKey, label)
        {
            this.DefaultKey = defaultKey;

            this.KeyValueMap = keys
                .Zip(values)
                .ToDictionary(entry => entry.First, entry => entry.Second);
        }

        public string DefaultKey { get; }
        public Dictionary<string, string> KeyValueMap { get; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    internal class DataWriterIntegerNumberInputOptionAttribute : OptionAttrbute
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
