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

        public DataWriterContext(string systemName, string dataDirectoryPath, NexusCatalogDescription catalogDescription, IList<CustomMetadataEntry> customMetadataEntrySet)
        {
            Contract.Requires(customMetadataEntrySet != null);

            customMetadataEntrySet.ToList().ForEach(customMetaDataEntry =>
            {
                if (!NexusUtilities.CheckNamingConvention(customMetaDataEntry.Key, out errorDescription))
                    throw new ArgumentException($"Argument '{ nameof(customMetadataEntrySet) }', value '{ customMetaDataEntry.Key }': { errorDescription }");
            });

            this.SystemName = systemName;
            this.DataDirectoryPath = dataDirectoryPath;
            this.CatalogDescription = catalogDescription;
            this.CustomMetadataEntrySet = customMetadataEntrySet;
        }

        public string SystemName { get; private set; }

        public string DataDirectoryPath { get; private set; }

        public NexusCatalogDescription CatalogDescription { get; private set; }

        public IList<CustomMetadataEntry> CustomMetadataEntrySet { get; private set; }
    }
}