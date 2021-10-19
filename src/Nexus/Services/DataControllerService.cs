using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Utilities;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class DataControllerService : IDataControllerService
    {
        private AppState _appstate;
        private ExtensionHive _extensionHive;
        private IServiceProvider _serviceProvider;
        private IFileAccessManager _fileAccessManager;
        private IUserIdService _userIdService;
        private ILogger _logger;
        private ILoggerFactory _loggerFactory;

        public DataControllerService(
            AppState appstate,
            ExtensionHive extensionHive,
            IServiceProvider serviceProvider,
            IDatabaseManager databaseManager,
            IFileAccessManager fileAccessManager,
            IUserIdService userIdService,
            ILogger<DataControllerService> logger,
            ILoggerFactory loggerFactory)
        {
            _appstate = appstate;
            _extensionHive = extensionHive;
            _serviceProvider = serviceProvider;
            _fileAccessManager = fileAccessManager;
            _userIdService = userIdService;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        public async Task<IDataSourceController> GetDataSourceControllerAsync(BackendSource backendSource, CancellationToken cancellationToken)
        {
            var logger = _loggerFactory.CreateLogger($"{backendSource.Type} - {backendSource.ResourceLocator}");
            var dataSource = _extensionHive.GetInstance<IDataSource>(backendSource.Type);

            var state = _serviceProvider.GetRequiredService<AppState>().CatalogState;

            // special case checks
            if (dataSource.GetType() == typeof(AggregationDataSource))
            {
                ((AggregationDataSource)dataSource).FileAccessManager = _fileAccessManager;
            }
            else if (dataSource.GetType() == typeof(FilterDataSource))
            {
                var filterDataSource = (FilterDataSource)dataSource;

                filterDataSource.CatalogCollection = state.CatalogCollection;

                filterDataSource.IsCatalogAccessible =
                    catalogId => AuthorizationUtilities.IsCatalogAccessible(
                        _userIdService.User, 
                        catalogId, 
                        state.CatalogCollection.CatalogContainers.First(container => container.Id == catalogId).CatalogMetadata);

                filterDataSource.GetDataSourceControllerAsync =
                    backendSource => this.GetDataSourceControllerAsync(backendSource, cancellationToken);
            }

            var controller = new DataSourceController(dataSource, backendSource, logger);

            if (state.BackendSourceToCatalogsMap.TryGetValue(backendSource, out var catalogs))
                await controller.InitializeAsync(catalogs, cancellationToken);

            else
                await controller.InitializeAsync(null, cancellationToken);

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
