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
        private AppState _appState;
        private IExtensionHive _extensionHive;
        private IServiceProvider _serviceProvider;
        private IUserIdService _userIdService;
        private ILogger _logger;
        private ILoggerFactory _loggerFactory;

        public DataControllerService(
            AppState appState,
            IExtensionHive extensionHive,
            IServiceProvider serviceProvider,
            ILogger<DataControllerService> logger,
            ILoggerFactory loggerFactory)
        {
            _appState = appState;
            _extensionHive = extensionHive;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        public async Task<IDataSourceController> GetDataSourceControllerAsync(BackendSource backendSource, CancellationToken cancellationToken)
        {
            var logger = _loggerFactory.CreateLogger($"{backendSource.Type} - {backendSource.ResourceLocator}");

            var dataSource = backendSource.Type switch
            {
                AggregationDataSource.Id    => new AggregationDataSource(),
                FilterDataSource.Id         => new FilterDataSource(),
                InMemoryDataSource.Id       => new InMemoryDataSource(),
                RpcDataSource.Id            => new RpcDataSource(),
                _                           => _extensionHive.GetInstance<IDataSource>(backendSource.Type)
            };

            // special case checks
            if (dataSource.GetType() == typeof(FilterDataSource))
            {
                var filterDataSource = (FilterDataSource)dataSource;
                
                filterDataSource.GetCatalogCollection = () => _appState.CatalogState.CatalogCollection;

#warning !!! "default" should be UserIdService.User

                filterDataSource.IsCatalogAccessible =
                    catalogId => AuthorizationUtilities.IsCatalogAccessible(
                        default,
                        _appState.CatalogState.CatalogCollection.CatalogContainers.First(container => container.Id == catalogId));

                filterDataSource.GetDataSourceControllerAsync =
                    backendSource => this.GetDataSourceControllerAsync(backendSource, cancellationToken);
            }

            var controller = new DataSourceController(dataSource, backendSource, logger);

            if (_appState.CatalogState is not null && _appState.CatalogState.BackendSourceToCatalogsMap.TryGetValue(backendSource, out var catalogs))
                await controller.InitializeAsync(catalogs, cancellationToken);

            else
                await controller.InitializeAsync(null, cancellationToken);

            return controller;
        }

        public async Task<IDataWriterController> GetDataWriterControllerAsync(Uri resourceLocator, ExportParameters exportParameters, CancellationToken cancellationToken)
        {
            var logger = _loggerFactory.CreateLogger($"{exportParameters.Writer} - {resourceLocator}");

            var dataWriter = exportParameters.Writer switch
            {
                CsvDataWriter.Id    => new CsvDataWriter(),
                _                   => _extensionHive.GetInstance<IDataWriter>(exportParameters.Writer)
            };

            var controller = new DataWriterController(dataWriter, resourceLocator, exportParameters.Configuration, logger);
            await controller.InitializeAsync(cancellationToken);

            return controller;
        }
    }
}
