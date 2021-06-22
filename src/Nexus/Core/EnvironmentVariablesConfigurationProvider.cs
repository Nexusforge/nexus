using Microsoft.Extensions.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Core
{
    public class EnvironmentVariablesConfigurationProvider : ConfigurationProvider, IConfigurationSource
    {
        private string _prefix;

        public EnvironmentVariablesConfigurationProvider(string prefix)
        {
            _prefix = prefix;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return this;
        }

        public override void Load()
        {
            this.Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            Environment.GetEnvironmentVariables()
                .Cast<DictionaryEntry>()
                .Where(entry => entry.Key.ToString().StartsWith(_prefix))
                .ToList()
                .ForEach(entry =>
                {
                    var key = ((string)entry.Key)
                        .Substring(_prefix.Length)
                        .Replace("_", ConfigurationPath.KeyDelimiter);

                    this.Data[key] = (string)entry.Value;
                });
        }
    }
}