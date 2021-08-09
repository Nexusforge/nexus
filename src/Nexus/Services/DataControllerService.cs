using Microsoft.Extensions.Logging;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Utilities;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class DataControllerService : IDataControllerService
    {
        private ExtensionHive _extensionHive;
        private IDatabaseManager _databaseManager;
        private IFileAccessManager _fileAccessManager;
        private IUserIdService _userIdService;
        private ILogger _logger;
        private ILoggerFactory _loggerFactory;

        public DataControllerService(
            ExtensionHive extensionHive,
            IDatabaseManager databaseManager,
            IFileAccessManager fileAccessManager,
            IUserIdService userIdService,
            ILogger<DataControllerService> logger,
            ILoggerFactory loggerFactory)
        {
            _extensionHive = extensionHive;
            _databaseManager = databaseManager;
            _fileAccessManager = fileAccessManager;
            _userIdService = userIdService;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        public async Task<IDataSourceController> GetDataSourceControllerAsync(BackendSource backendSource, CancellationToken cancellationToken)
        {
            var logger = _loggerFactory.CreateLogger($"{backendSource.Type} - {backendSource.ResourceLocator}");
            var dataSource = _extensionHive.GetInstance<IDataSource>(backendSource.Type);

            // special case checks
            if (dataSource.GetType() == typeof(AggregationDataSource))
            {
                ((AggregationDataSource)dataSource).FileAccessManager = _fileAccessManager;
            }
            else if (dataSource.GetType() == typeof(FilterDataSource))
            {
                var filterDataSource = (FilterDataSource)dataSource;

                filterDataSource.Catalogs = _databaseManager.Database;

                filterDataSource.IsCatalogAccessible =
                    catalogId => NexusUtilities.IsCatalogAccessible(_userIdService.User, catalogId, _databaseManager.Database);

                filterDataSource.GetDataSourceControllerAsync =
                    backendSource => this.GetDataSourceControllerAsync(backendSource, cancellationToken);
            }

            var controller = new DataSourceController(dataSource, backendSource, logger);
            _ = _databaseManager.State.BackendSourceToCatalogsMap.TryGetValue(backendSource, out var catalogs);
            await controller.InitializeAsync(catalogs, cancellationToken);

            return controller;
        }

        public async Task<IDataWriterController> GetDataWriterControllerAsync(Uri resourceLocator, ExportParameters exportParameters, CancellationToken cancellationToken)
        {
            var logger = _loggerFactory.CreateLogger($"{exportParameters.Writer} - {resourceLocator}");
            var dataWriter = _extensionHive.GetInstance<IDataWriter>(exportParameters.Writer);
            var controller = new DataWriterController(dataWriter, resourceLocator, exportParameters.Configuration, logger);
            await controller.InitializeAsync(cancellationToken);

            return controller;
        }
    }
}
