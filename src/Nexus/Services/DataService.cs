using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.DataModel;
using Nexus.Core;
using Nexus.Infrastructure;
using Nexus.Extensibility;
using Nexus.Extension.Famos;
using Nexus.Extension.Mat73;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Nexus.Utilities;

namespace Nexus.Services
{
    public class DataService
    {
        #region Fields

        private ILogger _logger;
        private UserIdService _userIdService;
        private IDatabaseManager _databaseManager;
        private PathsOptions _pathsOptions;

        private ulong _chunkSize;

        #endregion

        #region Types

        private record ExportContext(TimeSpan SamplePeriod,
                                     List<IGrouping<BackendSource, DatasetRecord>> GroupedDatasetRecords,
                                     ExportParameters ExportParameters);

        #endregion

        #region Constructors

        public DataService(IDatabaseManager databaseManager,
                           UserIdService userIdService,
                           ILoggerFactory loggerFactory,
                           IOptions<AggregationOptions> aggregationOptions,
                           IOptions<PathsOptions> pathsOptions)
        {
            _databaseManager = databaseManager;
            _userIdService = userIdService;
            _logger = loggerFactory.CreateLogger("Nexus");
            _pathsOptions = pathsOptions.Value;
            _chunkSize = aggregationOptions.Value.ChunkSizeMB * 1000 * 1000UL;

            this.Progress = new Progress<ProgressUpdatedEventArgs>();
        }

        #endregion

        #region Properties

        public Progress<ProgressUpdatedEventArgs> Progress { get; }

        #endregion

        #region Methods

        public Task<List<AvailabilityResult>> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, AvailabilityGranularity granularity, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                var dataSources = await _databaseManager.GetDataSourcesAsync(_userIdService.User, catalogId, cancellationToken);

                var tasks = dataSources.Select(dataSourceForUsing =>
                {
                    using var dataSource = dataSourceForUsing;
                    return dataSource.GetAvailabilityAsync(catalogId, begin, end, granularity, cancellationToken);
                }).ToList();

                await Task.WhenAll(tasks);

                return tasks.Select(task => task.Result).ToList();
            });
        }

        public async Task<string> ExportDataAsync(ExportParameters exportParameters,
                                                  List<DatasetRecord> datasetRecords,
                                                  CancellationToken cancellationToken)
        {
            if (!datasetRecords.Any() || exportParameters.Begin == exportParameters.End)
                return string.Empty;

            // find sample rate
            var sampleRates = datasetRecords
                .Select(datasetRecord => datasetRecord.Dataset.GetSampleRate())
                .Distinct()
                .ToList();

            if (sampleRates.Count != 1)
                throw new ValidationException("All datasets must be of the same sample period.");

            var sampleRate = sampleRates.First();

            // log
            var username = _userIdService.GetUserId();
            var message = $"User '{username}' exports data: {exportParameters.Begin.ToISO8601()} to {exportParameters.End.ToISO8601()} ... ";
            _logger.LogInformation(message);

            try
            {
                // start
                var zipFilePath = Path.Combine(_pathsOptions.Export, $"Nexus_{exportParameters.Begin.ToString("yyyy-MM-ddTHH-mm")}_{sampleRate.ToUnitString(underscore: true)}_{Guid.NewGuid().ToString().Substring(0, 8)}.zip");
                using var zipArchive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);

                // create tmp/target directory
                var directoryPath = exportParameters.ExportMode switch
                {
                    ExportMode.Web => Path.Combine(Path.GetTempPath(), "Nexus", Guid.NewGuid().ToString()),
                    ExportMode.Local => Path.Combine(_pathsOptions.Export, $"Nexus_{exportParameters.Begin.ToString("yyyy-MM-ddTHH-mm")}_{sampleRate.ToUnitString(underscore: true)}_{Guid.NewGuid().ToString().Substring(0, 8)}"),
                    _ => throw new Exception("Unsupported export mode.")
                };

                Directory.CreateDirectory(directoryPath);

                foreach (var catalogGroup in datasetRecords.GroupBy(datasetRecord => datasetRecord.Catalog.Id))
                {
                    var groupedByBackendSource = datasetRecords
                        .GroupBy(datasetRecord => datasetRecord.Dataset.BackendSource)
                        .ToList();

                    var exportContext = new ExportContext(sampleRate.Period, groupedByBackendSource, exportParameters);
                    await this.CreateFilesAsync(_userIdService.User, exportContext, directoryPath, cancellationToken);
                }

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
            try
            {
                // write zip archive entries
                var filePathSet = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                var currentFile = 0;
                var fileCount = filePathSet.Count();

                foreach (string filePath in filePathSet)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var zipArchiveEntry = zipArchive.CreateEntry(Path.GetFileName(filePath), CompressionLevel.Optimal);

                    this.OnProgress(new ProgressUpdatedEventArgs(currentFile / (double)fileCount, $"Writing file {currentFile + 1} / {fileCount} to ZIP archive ..."));

                    using var fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read);
                    using var zipArchiveEntryStream = zipArchiveEntry.Open();

                    fileStream.CopyTo(zipArchiveEntryStream);
                    currentFile++;
                }
            }
            finally
            {
                this.CleanUp(directoryPath);
            }
        }

        private async Task CreateFilesAsync(ClaimsPrincipal user, 
                                            ExportContext exportContext,
                                            string directoryPath,
                                            CancellationToken cancellationToken)
        {
            var channelDescriptionSet = sparseCatalog.ToChannelDescriptions();
            var singleFile = exportParameters.FileGranularity == FileGranularity.SingleFile;

            TimeSpan filePeriod;

            if (singleFile)
                filePeriod = exportParameters.End - exportParameters.Begin;
            else
                filePeriod = TimeSpan.FromSeconds((int)exportParameters.FileGranularity);

            DataWriterExtensionSettingsBase settings;
            DataWriterController dataWriter;

            switch (exportParameters.FileFormat)
            {
                case FileFormat.CSV:

                    settings = new CsvSettings()
                    {
                        FilePeriod = filePeriod,
                        SingleFile = singleFile,
                        RowIndexFormat = exportParameters.CsvRowIndexFormat,
                        SignificantFigures = exportParameters.CsvSignificantFigures,
                    };

                    dataWriter = new CsvWriter((CsvSettings)settings, NullLogger.Instance);

                    break;

                case FileFormat.FAMOS:

                    settings = new FamosSettings()
                    {
                        FilePeriod = filePeriod,
                        SingleFile = singleFile,
                    };

                    dataWriter = new FamosWriter((FamosSettings)settings, NullLogger.Instance);

                    break;

                case FileFormat.MAT73:

                    settings = new Mat73Settings()
                    {
                        FilePeriod = filePeriod,
                        SingleFile = singleFile,
                    };

                    dataWriter = new Mat73Writer((Mat73Settings)settings, NullLogger.Instance);

                    break;

                default:
                    throw new NotImplementedException();
            }

            if (!string.IsNullOrWhiteSpace(sparseCatalog.License.FileMessage))
                customMetadataEntrySet.Add(new CustomMetadataEntry("license", sparseCatalog.License.FileMessage, CustomMetadataEntryLevel.Catalog));

            // initialize data writer
            var catalogName_splitted = sparseCatalog.Id.Split('/');
            var dataWriterContext = new DataWriterContext("Nexus", directoryPath, new NexusCatalogDescription(Guid.Empty, 0, catalogName_splitted[1], catalogName_splitted[2], catalogName_splitted[3]), customMetadataEntrySet);
            dataWriter.Configure(dataWriterContext, channelDescriptionSet);

            try
            {
                // create temp files
                await this.CreateFilesAsync(user, exportContext, dataWriter, cancellationToken);
                dataWriter.Dispose();               
            }
            finally
            {
                dataWriter.Dispose();
            }
        }

        private async Task CreateFilesAsync(ClaimsPrincipal user, 
                                            ExportContext exportContext,
                                            DataWriterController dataWriter,
                                            CancellationToken cancellationToken)
        {
            /* progressHandler */
            var progressHandler = (EventHandler<double>)((sender, e) =>
            {
                this.OnProgress(new ProgressUpdatedEventArgs(e, $"Loading data ..."));
            });

            /* readingGroups */
            var readingGroupTasks = exportContext.GroupedDatasetRecords.Select(async group =>
            {
                var backendSource = group.Key;
                var controller = await _databaseManager.GetDataSourceControllerAsync(user, backendSource, cancellationToken);
                controller.Progress.ProgressChanged += progressHandler;
                var datasetRecordPipes = group.Select(datasetRecord => new DatasetRecordPipe(datasetRecord, x, null));

                return new DataSourceReadingGroup(controller, datasetRecordPipes);
            });

            await Task.WhenAll(readingGroupTasks);

            var readingGroups = readingGroupTasks
                .Select(readingGroupTask => readingGroupTask.Result)
                .ToList;

            try
            {
                /* read */
                var exportParameters = exportContext.ExportParameters;

                var reading = await DataSourceController.ReadAsync(
                    exportParameters.Begin,
                    exportParameters.End,
                    exportContext.SamplePeriod,
                    _chunkSize,
                    readingGroups,
                    cancellationToken);

                var writing = dataWriter.WriteAsync(datasetRecordPipes, )

                await Task.WhenAll(reading, writing);
#error jetzt würden die pipewriter ja immer weiter schreiben, also auch verschieden lang sein .... man müsste also erstmal die min größe finden
                dataWriter.Write(progressRecord.Begin, period, buffers);              
            }
            finally
            {
                foreach (var readingGroup in readingGroups)
                {
                    readingGroup.Controller.Progress.ProgressChanged -= progressHandler;
                }
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

        private void OnProgress(ProgressUpdatedEventArgs e)
        {
            ((IProgress<ProgressUpdatedEventArgs>)this.Progress).Report(e);
        }

        #endregion
    }
}
