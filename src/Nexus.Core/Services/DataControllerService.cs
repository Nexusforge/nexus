using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Utilities;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class DataControllerService : IDataControllerService
    {
        private AppState _appState;
        private IExtensionHive _extensionHive;
        private ILogger _logger;
        private ILoggerFactory _loggerFactory;

        public DataControllerService(
            AppState appState,
            IExtensionHive extensionHive,
            ILogger<DataControllerService> logger,
            ILoggerFactory loggerFactory)
        {
            _appState = appState;
            _extensionHive = extensionHive;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        public async Task<IDataSourceController> GetDataSourceControllerForDataAccessAsync(ClaimsPrincipal user, BackendSource backendSource, CancellationToken cancellationToken)
        {
            var controller = await this.GetDataSourceControllerAsync(backendSource, cancellationToken);

            // special case checks
            var dataSource = ((DataSourceController)controller).DataSource;

            if (dataSource.GetType() == typeof(FilterDataSource))
            {
                var filterDataSource = (FilterDataSource)dataSource;

                filterDataSource.DataSourceControllerLogger = _loggerFactory.CreateLogger<DataSourceController>();

                filterDataSource.GetCatalogContainers = () => _appState.CatalogState.CatalogContainers;

                filterDataSource.IsCatalogAccessible = catalogId =>
                {
                    var catalogContainer = _appState.CatalogState.CatalogContainers.First(container => container.Id == catalogId);

                    return AuthorizationUtilities.IsCatalogAccessible(
                        catalogContainer.Id,
                        catalogContainer.CatalogMetadata,
                        user);
                };

                filterDataSource.GetDataSourceControllerAsync =
                    backendSource => this.GetDataSourceControllerAsync(backendSource, cancellationToken);
            }

            return controller;
        }

        public async Task<IDataSourceController> GetDataSourceControllerAsync(BackendSource backendSource, CancellationToken cancellationToken)
        {
            var logger = _loggerFactory.CreateLogger($"{backendSource.Type} - {backendSource.ResourceLocator}");
            var dataSource = _extensionHive.GetInstance<IDataSource>(backendSource.Type);
            var controller = new DataSourceController(dataSource, backendSource, logger);

            var backendSourceCache = _appState.CatalogState.BackendSourceCache.GetOrAdd(
                backendSource, 
                backendSource => new ConcurrentDictionary<string, ResourceCatalog>());

            await controller.InitializeAsync(backendSourceCache, cancellationToken);

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
