using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    public class DataService
    {
        #region Fields

        private ILogger _logger;
        private PathsOptions _pathsOptions;
        private ExtensionHive _extensionHive;
        private IUserIdService _userIdService;
        private IDatabaseManager _databaseManager;
        private IDataSourceControllerService _dataSourceControllerService;

        private uint _chunkSize;

        #endregion

        #region Constructors

        public DataService(IDataSourceControllerService dataSourceControllerService,
                           IDatabaseManager databaseManager,
                           ExtensionHive extensionHive,
                           IUserIdService userIdService,
                           ILogger<DataService> logger,
                           ILoggerFactory loggerFactory,
                           IOptions<AggregationOptions> aggregationOptions,
                           IOptions<PathsOptions> pathsOptions)
        {
            _dataSourceControllerService = dataSourceControllerService;
            _databaseManager = databaseManager;
            _extensionHive = extensionHive;
            _userIdService = userIdService;
            _logger = logger;
            _pathsOptions = pathsOptions.Value;
            _chunkSize = aggregationOptions.Value.ChunkSizeMB * 1000 * 1000;

            this.ReadProgress = new Progress<double>();
            this.WriteProgress = new Progress<double>();
        }

        #endregion

        #region Properties

        public Progress<double> ReadProgress { get; }

        public Progress<double> WriteProgress { get; }

        #endregion

        #region Methods

        public Task<AvailabilityResult[]> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, AvailabilityGranularity granularity, CancellationToken cancellationToken)
        {
#warning this is OK! continue with more methods

            return Task.Run(async () =>
            {
                var backendSources = _databaseManager.State.BackendSourceToCatalogsMap
                    // where the catalog list contains the catalog ID
                    .Where(entry => entry.Value.Any(catalog => catalog.Id == catalogId))
                    // select the backend source
                    .Select(entry => entry.Key);

                var dataSourceControllerTasks = backendSources
                    .Select(backendSource => _dataSourceControllerService.GetControllerAsync(_userIdService.User, backendSource, cancellationToken));

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
                        var disposable = controller as IDisposable;
                        disposable?.Dispose();
                    }
                }
            });
        }

        public async Task<string> ExportDataAsync(ExportParameters exportParameters,
                                                  List<CatalogItem> catalogItems,
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

            // log
            var username = _userIdService.GetUserId();
            var message = $"User '{username}' exports data: {exportParameters.Begin.ToISO8601()} to {exportParameters.End.ToISO8601()} ... ";
            _logger.LogInformation(message);

            try
            {
                // start
                var zipFilePath = Path.Combine(_pathsOptions.Export, $"Nexus_{exportParameters.Begin.ToString("yyyy-MM-ddTHH-mm-ss")}_{samplePeriod.ToUnitString()}_{Guid.NewGuid().ToString().Substring(0, 8)}.zip");
                using var zipArchive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);

                // create tmp/target directory
                var directoryPath = exportParameters.ExportMode switch
                {
                    ExportMode.Web => Path.Combine(Path.GetTempPath(), "Nexus", Guid.NewGuid().ToString()),
                    ExportMode.Local => Path.Combine(_pathsOptions.Export, $"Nexus_{exportParameters.Begin.ToString("yyyy-MM-ddTHH-mm-ss")}_{samplePeriod.ToUnitString()}_{Guid.NewGuid().ToString().Substring(0, 8)}"),
                    _ => throw new Exception("Unsupported export mode.")
                };

                Directory.CreateDirectory(directoryPath);

                // get data writer controller
                var dataWriter = _extensionHive.GetInstance<IDataWriter>(exportParameters.Writer);
                var resourceLocator = new Uri(directoryPath, UriKind.RelativeOrAbsolute);
                var dataWriterController = new DataWriterController(dataWriter, resourceLocator, exportParameters.Configuration, _logger);
                await dataWriterController.InitializeAsync(cancellationToken);

                // write tmp files
                try
                {
                    var exportContext = new ExportContext(samplePeriod, catalogItems, exportParameters);
                    await this.CreateFilesAsync(_userIdService.User, exportContext, dataWriterController, cancellationToken);
                }
                finally
                {
                    var disposable = dataWriter as IDisposable;
                    disposable?.Dispose();
                }

                // write zip archive
                switch (exportParameters.ExportMode)
                {
                    case ExportMode.Web:
                        this.WriteZipArchiveEntries(zipArchive, directoryPath, cancellationToken);
                        break;

                    case ExportMode.Local:
                        break;

                    default:
                        break;
                }

                _logger.LogInformation($"{message} Done.");

                return $"export/{Path.GetFileName(zipFilePath)}";
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} Fail. Reason: {ex.GetFullMessage()}");
                throw;
            }
        }

        private void WriteZipArchiveEntries(ZipArchive zipArchive, string directoryPath, CancellationToken cancellationToken)
        {
#error Provide licenses as extra markdown file placed in ZIP!

            try
            {
                // write zip archive entries
                var filePathSet = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                var fileCount = filePathSet.Count();

                foreach (string filePath in filePathSet)
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
                this.CleanUp(directoryPath);
            }
        }

        private async Task CreateFilesAsync(
            ClaimsPrincipal user, 
            ExportContext exportContext,
            DataWriterController dataWriterController,
            CancellationToken cancellationToken)
        {
            /* reading groups */
            var catalogItemPipeReaders = new List<CatalogItemPipeReader>();
            var groupedCatalogItems = exportContext.CatalogItems.GroupBy(catalogItem => catalogItem.Representation.BackendSource);
            var readingGroups = new List<DataReadingGroup>();

            foreach (var catalogItemGroup in groupedCatalogItems)
            {
                var backendSource = catalogItemGroup.Key;
                var controller = await _databaseManager.GetDataSourceControllerAsync(user, backendSource, cancellationToken);
                var catalogItemPipeWriters = new List<CatalogItemPipeWriter>();

                foreach (var catalogItem in catalogItemGroup)
                {
                    var pipe = new Pipe();
                    catalogItemPipeWriters.Add(new CatalogItemPipeWriter(catalogItem, pipe.Writer, null));
                    catalogItemPipeReaders.Add(new CatalogItemPipeReader(catalogItem, pipe.Reader));
                }

                readingGroups.Add(new DataReadingGroup(controller, catalogItemPipeWriters.ToArray()));
            }

            /* read */
            var exportParameters = exportContext.ExportParameters;

            var reading = DataSourceController.ReadAsync(
                exportParameters.Begin,
                exportParameters.End,
                exportContext.SamplePeriod,
                _chunkSize,
                readingGroups.ToArray(),
                this.ReadProgress,
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
