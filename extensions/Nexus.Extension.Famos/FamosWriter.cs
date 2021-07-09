using ImcFamosFile;
using Microsoft.Extensions.Logging;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexus.Extension.Famos
{
    public class FamosWriter : IDataWriter
    {
        #region "Fields"

        private string _dataFilePath;
        private FamosFile _famosFile;
        private FamosSettings _settings;
        private Dictionary<ulong, int> _spdToFieldIndexMap;

        #endregion
        
        #region "Constructors"

        public FamosWriter(FamosSettings settings, ILogger logger) : base(settings, logger)
        {
            _settings = settings;

            _spdToFieldIndexMap = new Dictionary<ulong, int>();
        }

        #endregion

        #region "Methods"

        protected override void OnPrepareFile(DateTime startDateTime, List<ResourceContextGroup> resourceContextGroupSet)
        {
            _dataFilePath = Path.Combine(this.DataWriterContext.DataDirectoryPath, $"{this.DataWriterContext.CatalogDescription.PrimaryGroupName}_{this.DataWriterContext.CatalogDescription.SecondaryGroupName}_{this.DataWriterContext.CatalogDescription.CatalogName}_V{ this.DataWriterContext.CatalogDescription.Version}_{startDateTime.ToString("yyyy-MM-ddTHH-mm-ss")}Z.dat");

            if (_famosFile != null)
                _famosFile.Dispose();

            this.OpenFile(_dataFilePath, startDateTime, resourceContextGroupSet);
        }

        protected override void OnWrite(ResourceContextGroup contextGroup, ulong fileOffset, ulong bufferOffset, ulong length)
        {
            if (length <= 0)
                throw new Exception(ErrorMessage.FamosWriter_SampleRateTooLow);

            var simpleBuffers = contextGroup.ResourceContextSet.Select(resourceContext => resourceContext.Buffer.ToSimpleBuffer()).ToList();

            var fieldIndex = _spdToFieldIndexMap[contextGroup.SampleRate.SamplesPerDay];
            var field = _famosFile.Fields[fieldIndex];

            _famosFile.Edit(writer =>
            {
                for (int i = 0; i < simpleBuffers.Count; i++)
                {
                    var component = field.Components[i];
                    var data = simpleBuffers[i].Buffer.Slice((int)bufferOffset, (int)length);

                    _famosFile.WriteSingle(writer, component, (int)fileOffset, data);
                }
            });
        }

        private void OpenFile(string dataFilePath, DateTime startDateTime, List<ResourceContextGroup> resourceContextGroupSet)
        {
            if (File.Exists(dataFilePath))
                throw new Exception($"The file {dataFilePath} already exists. Extending an already existing file with additional resources is not supported.");

            var famosFile = new FamosFileHeader();

            // file
            var metadataGroup = new FamosFileGroup("Metadata");

            metadataGroup.PropertyInfo = new FamosFilePropertyInfo(new List<FamosFileProperty>()
            {
                new FamosFileProperty("format_version", this.FormatVersion),
                new FamosFileProperty("system_name", this.DataWriterContext.SystemName),
                new FamosFileProperty("date_time", startDateTime),
            });

            foreach (var customMetadataEntry in this.DataWriterContext.CustomMetadataEntrySet.Where(customMetadataEntry => customMetadataEntry.CustomMetadataEntryLevel == CustomMetadataEntryLevel.File))
            {
                metadataGroup.PropertyInfo.Properties.Add(new FamosFileProperty(customMetadataEntry.Key, customMetadataEntry.Value));
            }

            famosFile.Groups.Add(metadataGroup);

            // file -> catalog
            var catalogGroup = new FamosFileGroup($"{this.DataWriterContext.CatalogDescription.PrimaryGroupName} / {this.DataWriterContext.CatalogDescription.SecondaryGroupName} / {this.DataWriterContext.CatalogDescription.CatalogName}");

            catalogGroup.PropertyInfo = new FamosFilePropertyInfo(new List<FamosFileProperty>()
            {
                new FamosFileProperty("catalog_version", this.DataWriterContext.CatalogDescription.Version)
            });

            foreach (var customMetadataEntry in this.DataWriterContext.CustomMetadataEntrySet.Where(customMetadataEntry => customMetadataEntry.CustomMetadataEntryLevel == CustomMetadataEntryLevel.Catalog))
            {
                catalogGroup.PropertyInfo.Properties.Add(new FamosFileProperty(customMetadataEntry.Key, customMetadataEntry.Value));
            }

            famosFile.Groups.Add(catalogGroup);

            // for each context group
            foreach (var contextGroup in resourceContextGroupSet)
            {
                var totalSeconds = (int)Math.Round(_settings.FilePeriod.TotalSeconds, MidpointRounding.AwayFromZero);
                var totalLength = (int)(totalSeconds * contextGroup.SampleRate.SamplesPerSecond);

                if (totalLength * (double)NexusUtilities.SizeOf(NexusDataType.FLOAT64) > 2 * Math.Pow(10, 9))
                    throw new Exception(ErrorMessage.FamosWriter_DataSizeExceedsLimit);

                // file -> catalog -> resources
                var field = new FamosFileField(FamosFileFieldType.MultipleYToSingleEquidistantTime);

                foreach (ResourceContext resourceContext in contextGroup.ResourceContextSet)
                {
                    var dx = contextGroup.SampleRate.Period.TotalSeconds;
                    var resource = this.PrepareResource(field, resourceContext.ResourceDescription, (int)totalLength, startDateTime, dx);

                    catalogGroup.Resources.Add(resource);
                }

                famosFile.Fields.Add(field);
                _spdToFieldIndexMap[contextGroup.SampleRate.SamplesPerDay] = famosFile.Fields.Count - 1;
            }

            //
            famosFile.Save(dataFilePath, _ => { });
            _famosFile = FamosFile.OpenEditable(dataFilePath);
        }

        private FamosFileResource PrepareResource(FamosFileField field, ResourceDescription resourceDescription, int totalLength, DateTime startDateTme, double dx)
        {
            // component 
            var representationName = $"{resourceDescription.ResourceName}_{resourceDescription.RepresentationName.Replace(" ", "_")}";
            var calibration = new FamosFileCalibration(false, 1, 0, false, resourceDescription.Unit);

            var component = new FamosFileAnalogComponent(representationName, FamosFileDataType.Float64, totalLength, calibration)
            {
                XAxisScaling = new FamosFileXAxisScaling((decimal)dx) { Unit = "s" },
                TriggerTime = new FamosFileTriggerTime(startDateTme, FamosFileTimeMode.Unknown),
            };

            // attributes
            var resource = component.Resources.First();

            resource.PropertyInfo = new FamosFilePropertyInfo(new List<FamosFileProperty>()
            {
                new FamosFileProperty("name", resourceDescription.ResourceName),
                new FamosFileProperty("group", resourceDescription.Group),
                new FamosFileProperty("comment", "yyyy-MM-ddTHH-mm-ssZ: Comment1"),
            });

            field.Components.Add(component);

            return resource;
        }

        protected override void FreeManagedResources()
        {
            base.FreeManagedResources();

            _famosFile?.Dispose();
        }

        #endregion
    }
}
