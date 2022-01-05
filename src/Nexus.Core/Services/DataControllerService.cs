using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Models;
using System;
using System.Collections.Concurrent;
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

        public async Task<IDataSourceController> GetDataSourceControllerAsync(
            BackendSource backendSource,
            CancellationToken cancellationToken, 
            CatalogCache? catalogCache = default)
        {
            var logger1 = _loggerFactory.CreateLogger<DataSourceController>();
            var logger2 = _loggerFactory.CreateLogger($"{backendSource.Type} - {backendSource.ResourceLocator}");
            var dataSource = _extensionHive.GetInstance<IDataSource>(backendSource.Type);
            var controller = new DataSourceController(dataSource, backendSource, logger1);

            if (catalogCache is null)
                catalogCache = _appState.CatalogState.Cache;

            var actualCatalogCache = catalogCache.GetOrAdd(
                backendSource,
                backendSource => new ConcurrentDictionary<string, ResourceCatalog>());

            await controller.InitializeAsync(actualCatalogCache, logger2, cancellationToken);

            return controller;
        }

        public async Task<IDataWriterController> GetDataWriterControllerAsync(Uri resourceLocator, ExportParameters exportParameters, CancellationToken cancellationToken)
        {
            var logger1 = _loggerFactory.CreateLogger<DataWriterController>();
            var logger2 = _loggerFactory.CreateLogger($"{exportParameters.Type} - {resourceLocator}");
            var dataWriter = _extensionHive.GetInstance<IDataWriter>(exportParameters.Type);
            var controller = new DataWriterController(dataWriter, resourceLocator, exportParameters.Configuration, logger1);

            await controller.InitializeAsync(logger2, cancellationToken);

            return controller;
        }
    }
}
