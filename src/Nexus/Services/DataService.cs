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

namespace Nexus.Services
{
    public class DataService
    {
        #region Fields

        private ILogger _logger;
        private UserIdService _userIdService;
        private IDatabaseManager _databaseManager;
        private PathsOptions _pathsOptions;

        private ulong _blockSizeLimit;

        #endregion

        #region Constructors

        public DataService(IDatabaseManager databaseManager,
                           UserIdService userIdService,
                           ILoggerFactory loggerFactory,
                           IOptions<PathsOptions> pathsOptions)
        {
            _databaseManager = databaseManager;
            _userIdService = userIdService;
            _logger = loggerFactory.CreateLogger("Nexus");
            _pathsOptions = pathsOptions.Value;
            _blockSizeLimit = 5 * 1000 * 1000UL;

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
                                                  List<Dataset> datasets,
                                                  CancellationToken cancellationToken)
        {
            if (!datasets.Any() || exportParameters.Begin == exportParameters.End)
                return string.Empty;

            var username = _userIdService.GetUserId();

            // find sample rate
            var sampleRates = datasets.Select(dataset => dataset.GetSampleRate());

            if (sampleRates.Select(sampleRate => sampleRate.SamplesPerSecond).Distinct().Count() > 1)
                throw new ValidationException("Channels with different sample rates have been requested.");

            var sampleRate = sampleRates.First();

            // log
            var message = $"User '{username}' exports data: {exportParameters.Begin.ToISO8601()} to {exportParameters.End.ToISO8601()} ... ";
            _logger.LogInformation(message);

            try
            {
                // convert datasets into catalogs
                var catalogIds = datasets.Select(dataset => dataset.Channel.Catalog.Id).Distinct();
                var catalogContainers = _databaseManager.Database.CatalogContainers
                    .Where(catalogContainer => catalogIds.Contains(catalogContainer.Id));

                var sparseCatalogs = catalogContainers.Select(catalogContainer =>
                {
                    var currentDatasets = datasets.Where(dataset => dataset.Channel.Catalog.Id == catalogContainer.Id).ToList();
                    return catalogContainer.ToSparseCatalog(currentDatasets);
                });

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

                foreach (var sparseCatalog in sparseCatalogs)
                {
                    await this.CreateFilesAsync(_userIdService.User, exportParameters, sparseCatalog, directoryPath, cancellationToken);
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
                                 ExportParameters exportParameters,
                                 SparseCatalog sparseCatalog,
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

            // create custom meta data
            var customMetadataEntrySet = new List<CustomMetadataEntry>();
            //customMetadataEntrySet.Add(new CustomMetadataEntry("system_name", "Nexus", CustomMetadataEntryLevel.File));

            if (!string.IsNullOrWhiteSpace(sparseCatalog.License.FileMessage))
                customMetadataEntrySet.Add(new CustomMetadataEntry("license", sparseCatalog.License.FileMessage, CustomMetadataEntryLevel.Catalog));

            // initialize data writer
            var catalogName_splitted = sparseCatalog.Id.Split('/');
            var dataWriterContext = new DataWriterContext("Nexus", directoryPath, new NexusCatalogDescription(Guid.Empty, 0, catalogName_splitted[1], catalogName_splitted[2], catalogName_splitted[3]), customMetadataEntrySet);
            dataWriter.Configure(dataWriterContext, channelDescriptionSet);

            try
            {
                // create temp files
                await this.CreateFilesAsync(user, dataWriter, exportParameters, sparseCatalog, cancellationToken);
                dataWriter.Dispose();               
            }
            finally
            {
                dataWriter.Dispose();
            }
        }

        private async Task CreateFilesAsync(ClaimsPrincipal user,
                                             DataWriterController dataWriter,
                                             ExportParameters exportParameters,
                                             SparseCatalog sparseCatalog,
                                             CancellationToken cancellationToken)
        {
            var datasets = sparseCatalog.Channels.SelectMany(channel => channel.Datasets);
            var backendSourceToDatasetsMap = new Dictionary<BackendSource, List<Dataset>>();

            foreach (var dataset in datasets)
            {
                if (!backendSourceToDatasetsMap.ContainsKey(dataset.Registration))
                    backendSourceToDatasetsMap[dataset.Registration] = new List<Dataset>();

                backendSourceToDatasetsMap[dataset.Registration].Add(dataset);
            }

            var progressHandler = (EventHandler<double>)((sender, e) =>
            {
                this.OnProgress(new ProgressUpdatedEventArgs(e, $"Loading data ..."));
            });

            foreach (var entry in backendSourceToDatasetsMap)
            {
                if (entry.Value.Any())
                {
                    var registration = entry.Key;
                    var controller = await _databaseManager.GetDataSourceControllerAsync(user, registration, cancellationToken);
                    controller.Progress.ProgressChanged += progressHandler;

                    try
                    {
                        await foreach (var progressRecord in controller.ReadAsync(entry.Value, exportParameters.Begin, exportParameters.End, _blockSizeLimit, cancellationToken))
                        {
                            this.ProcessData(dataWriter, progressRecord);
                        }
                    }
                    finally
                    {
                        controller.Progress.ProgressChanged -= progressHandler;
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private void ProcessData(DataWriterController dataWriter, DataSourceProgressRecord progressRecord)
        {
            var buffers = progressRecord.DatasetToResultMap.Select(entry =>
            {
                var data = BufferUtilities.ApplyDatasetStatusByDataType(entry.Key.DataType, entry.Value);
                return (IBuffer)BufferUtilities.CreateSimpleBuffer(data);
            }).ToList();

            var period = progressRecord.End - progressRecord.Begin;
            dataWriter.Write(progressRecord.Begin, period, buffers);

            // clean up
            buffers = null;
            GC.Collect();
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
