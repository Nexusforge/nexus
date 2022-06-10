using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Nexus.Services
{
    internal interface IDataControllerService
    {
        Task<IDataSourceController> GetDataSourceControllerAsync(
            DataSourceRegistration registration,
            CancellationToken cancellationToken);

        Task<IDataWriterController> GetDataWriterControllerAsync(
            Uri resourceLocator,
            ExportParameters exportParameters,
            CancellationToken cancellationToken);
    }

    internal class DataControllerService : IDataControllerService
    {
        public const string NexusConfigurationHeaderKey = "Nexus-Configuration";

        private AppState _appState;
        private DataOptions _dataOptions;
        private IHttpContextAccessor _httpContextAccessor;
        private IExtensionHive _extensionHive;
        private IProcessingService _processingService;
        private ICacheService _cacheService;
        private ILogger _logger;
        private ILoggerFactory _loggerFactory;

        public DataControllerService(
            AppState appState,
            IHttpContextAccessor httpContextAccessor,
            IExtensionHive extensionHive,
            IProcessingService processingService,
            ICacheService cacheService,
            IOptions<DataOptions> dataOptions,
            ILogger<DataControllerService> logger,
            ILoggerFactory loggerFactory)
        {
            _appState = appState;
            _httpContextAccessor = httpContextAccessor;
            _extensionHive = extensionHive;
            _processingService = processingService;
            _cacheService = cacheService;
            _dataOptions = dataOptions.Value;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        public async Task<IDataSourceController> GetDataSourceControllerAsync(
            DataSourceRegistration registration,
            CancellationToken cancellationToken)
        {
            var logger1 = _loggerFactory.CreateLogger<DataSourceController>();
            var logger2 = _loggerFactory.CreateLogger($"{registration.Type} - {registration.ResourceLocator}");

            var dataSource = _extensionHive.GetInstance<IDataSource>(registration.Type);
            var requestConfiguration = GetRequestConfiguration();

            var controller = new DataSourceController(
                dataSource,
                registration, 
                systemConfiguration: _appState.Project.SystemConfiguration?.Clone(),
                requestConfiguration: requestConfiguration,
                _processingService,
                _cacheService,
                _dataOptions,
                logger1);

            var actualCatalogCache = _appState.CatalogState.Cache.GetOrAdd(
                registration,
                registration => new ConcurrentDictionary<string, ResourceCatalog>());

            await controller.InitializeAsync(actualCatalogCache, logger2, cancellationToken);

            return controller;
        }

        public async Task<IDataWriterController> GetDataWriterControllerAsync(Uri resourceLocator, ExportParameters exportParameters, CancellationToken cancellationToken)
        {
            var logger1 = _loggerFactory.CreateLogger<DataWriterController>();
            var logger2 = _loggerFactory.CreateLogger($"{exportParameters.Type} - {resourceLocator}");
            var dataWriter = _extensionHive.GetInstance<IDataWriter>(exportParameters.Type);
            var requestConfiguration = exportParameters.Configuration;

            var controller = new DataWriterController(
                dataWriter, 
                resourceLocator,
                systemConfiguration: _appState.Project.SystemConfiguration?.Clone(),
                requestConfiguration: requestConfiguration,
                logger1);

            await controller.InitializeAsync(logger2, cancellationToken);

            return controller;
        }

        private JsonElement? GetRequestConfiguration()
        {
            var httpContext = _httpContextAccessor.HttpContext;

            if (httpContext is not null &&
                httpContext.Request.Headers.TryGetValue(NexusConfigurationHeaderKey, out var encodedRequestConfiguration))
            {
                var requestConfiguration = JsonSerializer
                    .Deserialize<JsonElement>(Convert.FromBase64String(encodedRequestConfiguration.First()));

                return requestConfiguration;
            }

            return default;
        }
    }
}
