﻿using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class DataService
    {
        #region Fields

        private ILogger _logger;
        private ICatalogManager _catalogManager;
        private IDatabaseManager _databaseManager;
        private IDataControllerService _dataControllerService;

        #endregion

        #region Constructors

        public DataService(
            IDataControllerService dataControllerService,
            ICatalogManager catalogManager,
            IDatabaseManager databaseManager,
            ILogger<DataService> logger)
        {
            _dataControllerService = dataControllerService;
            _catalogManager = catalogManager;
            _databaseManager = databaseManager;
            _logger = logger;

            this.ReadProgress = new Progress<double>();
            this.WriteProgress = new Progress<double>();
        }

        #endregion

        #region Properties

        public Progress<double> ReadProgress { get; }

        public Progress<double> WriteProgress { get; }

        #endregion

        #region Methods

        public async Task<AvailabilityResult[]> GetAvailabilityAsync(
            string catalogId, 
            DateTime begin, 
            DateTime end, 
            AvailabilityGranularity granularity,
            CancellationToken cancellationToken)
        {
            var backendSources = _catalogManager.State.BackendSourceToCatalogsMap
                // where the catalog list contains the catalog ID
                .Where(entry => entry.Value.Any(catalog => catalog.Id == catalogId))
                // select the backend source
                .Select(entry => entry.Key);

            var dataSourceControllerTasks = backendSources
                .Select(backendSource => _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken));

            var (controllers, exception) = await dataSourceControllerTasks.WhenAllEx();

            try
            {
                if (!exception.InnerExceptions.Any())
                {
                    var availabilityTasks = controllers.Select(controller => controller.GetAvailabilityAsync(catalogId, begin, end, granularity, cancellationToken));
                    var availabilityResults = await Task.WhenAll(availabilityTasks);

                    return availabilityResults;
                }
                else
                {
                    throw exception;
                }
            }
            finally
            {
                foreach (var controller in controllers)
                {
                    controller.Dispose();
                }
            }
        }

        public async Task<string> ExportAsync(
            ExportParameters exportParameters,
            IEnumerable<CatalogItem> catalogItems,
            Guid exportId,
            CancellationToken cancellationToken)
        {
            if (!catalogItems.Any() || exportParameters.Begin == exportParameters.End)
                return string.Empty;

            // find sample period
            var samplePeriods = catalogItems
                .Select(catalogItem => catalogItem.Representation.SamplePeriod)
                .Distinct()
                .ToList();

            if (samplePeriods.Count != 1)
                throw new ValidationException("All representations must be of the same sample period.");

            var samplePeriod = samplePeriods.First();

            // validate file period
            if (exportParameters.FilePeriod.Ticks % samplePeriod.Ticks != 0)
                throw new ValidationException("The file period must be a multiple of the sample period.");

            // start
            var zipFileName = $"Nexus_{exportParameters.Begin.ToString("yyyy-MM-ddTHH-mm-ss")}_{samplePeriod.ToUnitString()}_{exportId.ToString().Substring(0, 8)}.zip";
            var zipArchiveStream = _databaseManager.WriteExportFile(zipFileName);
            using var zipArchive = new ZipArchive(zipArchiveStream, ZipArchiveMode.Create);

            // create tmp/target directory
            var tmpFolderPath = Path.Combine(Path.GetTempPath(), "Nexus", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmpFolderPath);

            // copy available licenses
            var catalogIds = catalogItems
                .Select(catalogItem => catalogItem.Catalog.Id)
                .Distinct();

            foreach (var catalogId in catalogIds)
            {
                this.CopyLicenseIfAvailable(catalogId, tmpFolderPath);
            }

            // get data writer controller
            var resourceLocator = new Uri(tmpFolderPath, UriKind.RelativeOrAbsolute);
            var controller = await _dataControllerService.GetDataWriterControllerAsync(resourceLocator, exportParameters, cancellationToken);

            // write data files
            try
            {
                var exportContext = new ExportContext(samplePeriod, catalogItems, exportParameters);
                await this.CreateFilesAsync(exportContext, controller, cancellationToken);
            }
            finally
            {
                controller.Dispose();
            }

            // write zip archive
            this.WriteZipArchiveEntries(zipArchive, tmpFolderPath, cancellationToken);

            return zipFileName;
        }

        private void CopyLicenseIfAvailable(string catalogId, string targetFolder)
        {
            var enumeratonOptions = new EnumerationOptions() { MatchCasing = MatchCasing.CaseInsensitive };

            if (_databaseManager.TryReadFirstAttachment(catalogId, "license.md", enumeratonOptions, out var licenseStream))
            {
                var prefix = catalogId.TrimStart('/').Replace('/', '_');
                var targetFileName = $"{prefix}_LICENSE.md";
                var targetFile = Path.Combine(targetFolder, targetFileName);

                using (var targetFileStream = new FileStream(targetFile, FileMode.OpenOrCreate))
                {
                    licenseStream.CopyTo(targetFileStream);
                }

                licenseStream.Dispose();
            }
        }

        private async Task CreateFilesAsync(
            ExportContext exportContext,
            IDataWriterController dataWriterController,
            CancellationToken cancellationToken)
        {
            /* reading groups */
            var catalogItemPipeReaders = new List<CatalogItemPipeReader>();
            var groupedCatalogItems = exportContext.CatalogItems.GroupBy(catalogItem => catalogItem.Representation.BackendSource);
            var readingGroups = new List<DataReadingGroup>();

            foreach (var catalogItemGroup in groupedCatalogItems)
            {
                var backendSource = catalogItemGroup.Key;
                var dataSourceController = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken);
                var catalogItemPipeWriters = new List<CatalogItemPipeWriter>();

                foreach (var catalogItem in catalogItemGroup)
                {
                    var pipe = new Pipe();
                    catalogItemPipeWriters.Add(new CatalogItemPipeWriter(catalogItem, pipe.Writer, null));
                    catalogItemPipeReaders.Add(new CatalogItemPipeReader(catalogItem, pipe.Reader));
                }

                readingGroups.Add(new DataReadingGroup(dataSourceController, catalogItemPipeWriters.ToArray()));
            }

            /* read */
            var exportParameters = exportContext.ExportParameters;

            var reading = DataSourceController.ReadAsync(
                exportParameters.Begin,
                exportParameters.End,
                exportContext.SamplePeriod,
                readingGroups.ToArray(),
                this.ReadProgress,
                _logger,
                cancellationToken);

            /* write */
            var singleFile = exportParameters.FilePeriod == default;

            var filePeriod = singleFile
                ? exportParameters.End - exportParameters.Begin
                : exportParameters.FilePeriod;

            var writing = dataWriterController.WriteAsync(
                exportParameters.Begin,
                exportParameters.End,
                exportContext.SamplePeriod,
                filePeriod,
                catalogItemPipeReaders.ToArray(),
                this.WriteProgress,
                cancellationToken
            );

            /* wait */
            await Task.WhenAll(reading, writing);
        }

        private void WriteZipArchiveEntries(ZipArchive zipArchive, string sourceFolderPath, CancellationToken cancellationToken)
        {
            try
            {
                // write zip archive entries
                var filePaths = Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories);
                var fileCount = filePaths.Count();

                foreach (string filePath in filePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var zipArchiveEntry = zipArchive.CreateEntry(Path.GetFileName(filePath), CompressionLevel.Optimal);

                    using var fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read);
                    using var zipArchiveEntryStream = zipArchiveEntry.Open();

                    fileStream.CopyTo(zipArchiveEntryStream);
                }
            }
            finally
            {
                this.CleanUp(sourceFolderPath);
            }
        }

        private void CleanUp(string directoryPath)
        {
            try
            {
                Directory.Delete(directoryPath, true);
            }
            catch
            {
                //
            }
        }

        #endregion
    }
}
