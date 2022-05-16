using Microsoft.Extensions.Options;
using Nexus.Core;
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

        Task<string> ExportAsync(
            ExportParameters exportParameters,
            IEnumerable<CatalogItemRequest> catalogItemRequests, 
            Guid exportId,
            CancellationToken cancellationToken);
    }

    internal class DataService : IDataService
    {
        #region Fields

        private DataOptions _dataOptions;
        private ILogger _logger;
        private ILoggerFactory _loggerFactory;
        private IDatabaseService _databaseService;
        private IDataControllerService _dataControllerService;

        #endregion

        #region Constructors

        public DataService(
            IDataControllerService dataControllerService,
            IDatabaseService databaseService,
            IOptions<DataOptions> dataOptions,
            ILogger<DataService> logger,
            ILoggerFactory loggerFactory)
        {
            _dataControllerService = dataControllerService;
            _databaseService = databaseService;
            _dataOptions = dataOptions.Value;
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
            IEnumerable<CatalogItemRequest> catalogItemRequests,
            Guid exportId,
            CancellationToken cancellationToken)
        {
            if (!catalogItemRequests.Any() || exportParameters.Begin == exportParameters.End)
                return string.Empty;

            // find sample period
            var samplePeriods = catalogItemRequests
                .Select(catalogItemRequest => catalogItemRequest.Item.Representation.SamplePeriod)
                .Distinct()
                .ToList();

            if (samplePeriods.Count != 1)
                throw new ValidationException("All representations must be of the same sample period.");

            var samplePeriod = samplePeriods.First();

            // validate file period
            if (exportParameters.FilePeriod.Ticks % samplePeriod.Ticks != 0)
                throw new ValidationException("The file period must be a multiple of the sample period.");

            // start
            var zipFileName = $"{Guid.NewGuid()}.zip";
            var zipArchiveStream = _databaseService.WriteArtifact(zipFileName);
            using var zipArchive = new ZipArchive(zipArchiveStream, ZipArchiveMode.Create);

            // create tmp/target directory
            var tmpFolderPath = Path.Combine(Path.GetTempPath(), "Nexus", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmpFolderPath);

            // copy available licenses
            var catalogIds = catalogItemRequests
                .Select(request => request.Container.Id)
                .Distinct();

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
                var exportContext = new ExportContext(samplePeriod, catalogItemRequests, exportParameters);
                await CreateFilesAsync(exportContext, controller, cancellationToken);
            }
            finally
            {
                controller.Dispose();
            }

            // write zip archive
            WriteZipArchiveEntries(zipArchive, tmpFolderPath, cancellationToken);

            return zipFileName;
        }

        private void CopyLicenseIfAvailable(string catalogId, string targetFolder)
        {
            var enumeratonOptions = new EnumerationOptions() { MatchCasing = MatchCasing.CaseInsensitive };

            if (_databaseService.TryReadFirstAttachment(catalogId, "license.md", enumeratonOptions, out var licenseStream))
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
            var catalogItemRequestPipeReaders = new List<CatalogItemRequestPipeReader>();
            var readingGroups = new List<DataReadingGroup>();

            foreach (var group in exportContext.CatalogItemRequests.GroupBy(request => request.Container))
            {
                var registration = group.Key.DataSourceRegistration;
                var dataSourceController = await _dataControllerService.GetDataSourceControllerAsync(registration, cancellationToken);
                var catalogItemRequestPipeWriters = new List<CatalogItemRequestPipeWriter>();

                foreach (var catalogItemRequest in group)
                {
                    var pipe = new Pipe();
                    catalogItemRequestPipeWriters.Add(new CatalogItemRequestPipeWriter(catalogItemRequest, pipe.Writer));
                    catalogItemRequestPipeReaders.Add(new CatalogItemRequestPipeReader(catalogItemRequest, pipe.Reader));
                }

                readingGroups.Add(new DataReadingGroup(dataSourceController, catalogItemRequestPipeWriters.ToArray()));
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
                _dataOptions,
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
                catalogItemRequestPipeReaders.ToArray(),
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
            ((IProgress<double>)WriteProgress).Report(0);

            try
            {
                // write zip archive entries
                var filePaths = Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories);
                var fileCount = filePaths.Count();
                var currentCount = 0;

                foreach (string filePath in filePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _logger.LogTrace("Write content of {FilePath} to the ZIP archive", filePath);

                    var zipArchiveEntry = zipArchive.CreateEntry(Path.GetFileName(filePath), CompressionLevel.Optimal);

                    using var fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read);
                    using var zipArchiveEntryStream = zipArchiveEntry.Open();

                    fileStream.CopyTo(zipArchiveEntryStream);

                    currentCount++;
                    ((IProgress<double>)WriteProgress).Report(currentCount / (double)fileCount);
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
