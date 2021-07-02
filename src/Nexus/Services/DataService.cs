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
using System.IO.Pipelines;

namespace Nexus.Services
{
    public class DataService
    {
        #region Fields

        private ILogger _logger;
        private PathsOptions _pathsOptions;
        private UserIdService _userIdService;
        private IDatabaseManager _databaseManager;

        private uint _chunkSize;

        #endregion

        #region Types

        private record ExportContext(TimeSpan SamplePeriod,
                                     List<DatasetRecord> DatasetRecords,
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
            return Task.Run(async () =>
            {
                var dataSources = await _databaseManager.GetDataSourcesAsync(_userIdService.User, catalogId, cancellationToken);

                var tasks = dataSources.Select(dataSourceForUsing =>
                {
                    using var dataSource = dataSourceForUsing;
                    return dataSource.GetAvailabilityAsync(catalogId, begin, end, granularity, cancellationToken);
                }).ToList();

                var availabilityResults = await Task.WhenAll(tasks);

                return availabilityResults;
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

                var exportContext = new ExportContext(sampleRate.Period, datasetRecords, exportParameters);
                await this.CreateFilesAsync(_userIdService.User, exportContext, directoryPath, cancellationToken);

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

                    this.OnReadProgress(new ProgressUpdatedEventArgs(currentFile / (double)fileCount, $"Writing file {currentFile + 1} / {fileCount} to ZIP archive ..."));

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
            /* reading groups */
            var datasetPipeReaders = new List<DatasetPipeReader>();
            var groupedDatasetRecords = exportContext.DatasetRecords.GroupBy(datasetRecord => datasetRecord.Dataset.BackendSource);
            var readingGroups = new List<DataReadingGroup>();

            foreach (var datasetRecordGroup in groupedDatasetRecords)
            {
                var backendSource = datasetRecordGroup.Key;
                var controller = await _databaseManager.GetDataSourceControllerAsync(user, backendSource, cancellationToken);
                var datasetPipeWriters = new List<DatasetPipeWriter>();

                foreach (var datasetRecord in datasetRecordGroup)
                {
                    var pipe = new Pipe();
                    datasetPipeWriters.Add(new DatasetPipeWriter(datasetRecord, pipe.Writer, null));
                    datasetPipeReaders.Add(new DatasetPipeReader(datasetRecord, pipe.Reader));
                }

                readingGroups.Add(new DataReadingGroup(controller, datasetPipeWriters));
            }

            /* read */
            var exportParameters = exportContext.ExportParameters;

            var reading = DataSourceController.ReadAsync(
                exportParameters.Begin,
                exportParameters.End,
                exportContext.SamplePeriod,
                _chunkSize,
                readingGroups,
                this.ReadProgress,
                cancellationToken);

            /* write */
            var writing = dataWriter.WriteAsync(
                exportParameters.Begin,
                exportParameters.End,
                exportContext.SamplePeriod,
                exportParameters.FileGranularity,
                datasetPipeReaders,
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

        private void OnReadProgress(ProgressUpdatedEventArgs e)
        {
            ((IProgress<ProgressUpdatedEventArgs>)this.ReadProgress).Report(e);
        }

        private void OnWriterogress(ProgressUpdatedEventArgs e)
        {
            ((IProgress<ProgressUpdatedEventArgs>)this.WriteProgress).Report(e);
        }

        #endregion
    }
}
