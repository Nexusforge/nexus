using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Nexus.Extensibility
{
    public class DataWriterContext
    {
        private string errorDescription;

        public DataWriterContext(string systemName, string dataDirectoryPath, NexusProjectDescription projectDescription, IList<CustomMetadataEntry> customMetadataEntrySet)
        {
            Contract.Requires(customMetadataEntrySet != null);

            customMetadataEntrySet.ToList().ForEach(customMetaDataEntry =>
            {
                if (!NexusUtilities.CheckNamingConvention(customMetaDataEntry.Key, out errorDescription))
                    throw new ArgumentException($"Argument '{ nameof(customMetadataEntrySet) }', value '{ customMetaDataEntry.Key }': { errorDescription }");
            });

            this.SystemName = systemName;
            this.DataDirectoryPath = dataDirectoryPath;
            this.ProjectDescription = projectDescription;
            this.CustomMetadataEntrySet = customMetadataEntrySet;
        }

        public string SystemName { get; private set; }

        public string DataDirectoryPath { get; private set; }

        public NexusProjectDescription ProjectDescription { get; private set; }

        public IList<CustomMetadataEntry> CustomMetadataEntrySet { get; private set; }
    }
}