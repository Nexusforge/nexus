using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Utilities;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class DataSourceControllerService : IDataSourceControllerService
    {
        private ExtensionHive _extensionHive;
        private IDatabaseManager _databaseManager;
        private IServiceProvider _serviceProvider;
        private ILogger _logger;
        private ILoggerFactory _loggerFactory;

        public DataSourceControllerService(
            ExtensionHive extensionHive,
            IDatabaseManager databaseManager,
            IServiceProvider serviceProvider,
            ILogger<DataSourceControllerService> logger,
            ILoggerFactory loggerFactory)
        {
            _extensionHive = extensionHive;
            _databaseManager = databaseManager;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        public async Task<DataSourceController> GetControllerAsync(ClaimsPrincipal user, BackendSource backendSource, CancellationToken cancellationToken)
        {
            var logger = _loggerFactory.CreateLogger($"{backendSource.Type} - {backendSource.ResourceLocator}");
            var dataSource = _extensionHive.GetInstance<IDataSource>(backendSource.Type);

            // special case checks
            if (dataSource.GetType() == typeof(AggregationDataSource))
            {
                var fileAccessManger = _serviceProvider.GetRequiredService<IFileAccessManager>();
                ((AggregationDataSource)dataSource).FileAccessManager = fileAccessManger;
            }
            else if (dataSource.GetType() == typeof(FilterDataSource))
            {
                var filterDataSource = (FilterDataSource)dataSource;

                filterDataSource.Database = _databaseManager.Database;

                filterDataSource.IsCatalogAccessible =
                    catalogId => NexusUtilities.IsCatalogAccessible(user, catalogId, _databaseManager.Database);

                filterDataSource.GetDataSourceAsync =
                    backendSource => this.GetControllerAsync(user, backendSource, cancellationToken);
            }

            // create & initialize
            var controller = new DataSourceController(dataSource, backendSource, logger);
            _ = _databaseManager.State.BackendSourceToCatalogsMap.TryGetValue(backendSource, out var catalogs);
            await controller.InitializeAsync(catalogs.ToArray(), cancellationToken);

            return controller;
        }
    }
}
