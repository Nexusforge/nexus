using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.IO.Pipelines;

namespace Nexus.Services
{
    internal interface IDataService
    {
        Progress<double> ReadProgress { get; }
        Progress<double> WriteProgress { get; }

        Task<string> ExportAsync(ExportParameters exportParameters, Dictionary<CatalogContainer, IEnumerable<CatalogItem>> catalogItemsMap, Guid exportId, CancellationToken cancellationToken);
    }

    internal class DataService : IDataService
    {
        #region Fields

        private GeneralOptions _generalOptions;
        private ILogger _logger;
        private ILoggerFactory _loggerFactory;
        private IDatabaseManager _databaseManager;
        private IDataControllerService _dataControllerService;

        #endregion

        #region Constructors

        public DataService(
            IDataControllerService dataControllerService,
            IDatabaseManager databaseManager,
            IOptions<GeneralOptions> generalOptions,
            ILogger<DataService> logger,
            ILoggerFactory loggerFactory)
        {
            _dataControllerService = dataControllerService;
            _databaseManager = databaseManager;
            _generalOptions = generalOptions.Value;
            _logger = logger;
            _loggerFactory = loggerFactory;

            ReadProgress = new Progress<double>();
            WriteProgress = new Progress<double>();
        }

        #endregion

        #region Properties

        public Progress<double> ReadProgress { get; }

        public Progress<double> WriteProgress { get; }

        #endregion

        #region Methods

        public async Task<string> ExportAsync(
            ExportParameters exportParameters,
            Dictionary<CatalogContainer, IEnumerable<CatalogItem>> catalogItemsMap,
            Guid exportId,
            CancellationToken cancellationToken)
        {
            if (!catalogItemsMap.Any() || exportParameters.Begin == exportParameters.End)
                return string.Empty;

            // find sample period
            var samplePeriods = catalogItemsMap.SelectMany(entry => entry.Value)
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
            var zipArchiveStream = _databaseManager.WriteArtifact(zipFileName);
            using var zipArchive = new ZipArchive(zipArchiveStream, ZipArchiveMode.Create);

            // create tmp/target directory
            var tmpFolderPath = Path.Combine(Path.GetTempPath(), "Nexus", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmpFolderPath);

            // copy available licenses
            var catalogIds = catalogItemsMap.Keys.Select(catalogContainer => catalogContainer.Id);

            foreach (var catalogId in catalogIds)
            {
                CopyLicenseIfAvailable(catalogId, tmpFolderPath);
            }

            // get data writer controller
            var resourceLocator = new Uri(tmpFolderPath, UriKind.Absolute);
            var controller = await _dataControllerService.GetDataWriterControllerAsync(resourceLocator, exportParameters, cancellationToken);

            // write data files
            try
            {
                var exportContext = new ExportContext(samplePeriod, catalogItemsMap, exportParameters);
                await CreateFilesAsync(exportContext, controller, cancellationToken);
            }
            finally
            {
                controller.Dispose();
            }

            // write zip archive
            WriteZipArchiveEntries(zipArchive, tmpFolderPath, cancellationToken);

            return $"api/artifacts/{zipFileName}";
        }

        private void CopyLicenseIfAvailable(string catalogId, string targetFolder)
        {
            var enumeratonOptions = new EnumerationOptions() { MatchCasing = MatchCasing.CaseInsensitive };

            if (_databaseManager.TryReadFirstAttachment(catalogId, "license.md", enumeratonOptions, out var licenseStream))
            {
                try
                {
                    var prefix = catalogId.TrimStart('/').Replace('/', '_');
                    var targetFileName = $"{prefix}_LICENSE.md";
                    var targetFile = Path.Combine(targetFolder, targetFileName);

                    using (var targetFileStream = new FileStream(targetFile, FileMode.OpenOrCreate))
                    {
                        licenseStream.CopyTo(targetFileStream);
                    }
                }
                finally
                {
                    licenseStream.Dispose();
                }
            }
        }

        private async Task CreateFilesAsync(
            ExportContext exportContext,
            IDataWriterController dataWriterController,
            CancellationToken cancellationToken)
        {
            /* reading groups */
            var catalogItemPipeReaders = new List<CatalogItemPipeReader>();
            var readingGroups = new List<DataReadingGroup>();

            foreach (var entry in exportContext.CatalogItemsMap)
            {
                var registration = entry.Key.DataSourceRegistration;
                var dataSourceController = await _dataControllerService.GetDataSourceControllerAsync(registration, cancellationToken);
                var catalogItemPipeWriters = new List<CatalogItemPipeWriter>();

                foreach (var catalogItem in entry.Value)
                {
                    var pipe = new Pipe();
                    catalogItemPipeWriters.Add(new CatalogItemPipeWriter(catalogItem, pipe.Writer));
                    catalogItemPipeReaders.Add(new CatalogItemPipeReader(catalogItem, pipe.Reader));
                }

                readingGroups.Add(new DataReadingGroup(dataSourceController, catalogItemPipeWriters.ToArray()));
            }

            /* cancellation */
            var cts = new CancellationTokenSource();
            cancellationToken.Register(() => cts.Cancel());

            /* read */
            var exportParameters = exportContext.ExportParameters;
            var logger = _loggerFactory.CreateLogger<DataSourceController>();

            var reading = DataSourceController.ReadAsync(
                exportParameters.Begin,
                exportParameters.End,
                exportContext.SamplePeriod,
                readingGroups.ToArray(),
                _generalOptions,
                ReadProgress,
                logger,
                cts.Token);

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
                WriteProgress,
                cts.Token
            );

            /* "WhenAllFailFast": Task.WhenAll does not return when one of the methods fail. */
            var tasks = new List<Task>() { reading, writing };

            while (tasks.Any())
            {
                var task = await Task.WhenAny(tasks);
                cts.Token.ThrowIfCancellationRequested();

                if (task.Exception is not null && task.Exception.InnerException is not null)
                {
                    cts.Cancel();
                    throw task.Exception.InnerException;
                }

                tasks.Remove(task);
            }
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

                    _logger.LogTrace("Write content of {FilePath} to the ZIP archive", filePath);

                    var zipArchiveEntry = zipArchive.CreateEntry(Path.GetFileName(filePath), CompressionLevel.Optimal);

                    using var fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read);
                    using var zipArchiveEntryStream = zipArchiveEntry.Open();

                    fileStream.CopyTo(zipArchiveEntryStream);
                }
            }
            finally
            {
                CleanUp(sourceFolderPath);
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
