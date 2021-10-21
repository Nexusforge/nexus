using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Nexus.Core
{
    internal enum CodeType
    {
        Filter = 1,
        Shared = 99
    }

    internal enum CodeLanguage
    {
        CSharp = 1
    }

    internal record CodeDefinition()
    {
        public CodeDefinition(string owner) : this()
        {
            this.Owner = owner;
        }

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Owner { get; set; }

        public CodeType CodeType { get; set; } = CodeType.Filter;

        public CodeLanguage CodeLanguage { get; set; } = CodeLanguage.CSharp;

        public string Code { get; set; } = string.Empty;

        public bool IsEnabled { get; set; }

        public string Name { get; set; } = string.Empty;

        public TimeSpan SamplePeriod { get; set; }

        public List<string> RequestedCatalogIds { get; set; } = new List<string>();

        public DateTime CreationDate { get; set; } = DateTime.UtcNow;
    }

    internal record FilterSettings
    {
        #region Constructors

        public FilterSettings()
        {
            this.CodeDefinitions = new List<CodeDefinition>();
        }

        #endregion

        #region Properties

        public List<CodeDefinition> CodeDefinitions { get; set; }

        #endregion

        #region Methods

        public List<CodeDefinition> GetSharedFiles(string userName)
        {
            return this.CodeDefinitions
                   .Where(codeDefinition =>
                          codeDefinition.Owner == userName &&
                          codeDefinition.CodeType == CodeType.Shared)
                   .ToList();
        }

        public static FilterSettings Load(string filePath)
        {
            var jsonString = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<FilterSettings>(jsonString);
        }

        public void Save(string filePath)
        {
            var options = new JsonSerializerOptions() { WriteIndented = true };
            var jsonString = JsonSerializer.Serialize(this, options);
            File.WriteAllText(filePath, jsonString);
        }

        #endregion
    }
}
