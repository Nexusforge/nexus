using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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
        private IHttpContextAccessor _httpContextAccessor;
        private IExtensionHive _extensionHive;
        private ILogger _logger;
        private ILoggerFactory _loggerFactory;
        private Dictionary<string, string> _defaultUserConfiguration = new Dictionary<string, string>();

        public DataControllerService(
            AppState appState,
            IHttpContextAccessor httpContextAccessor,
            IExtensionHive extensionHive,
            ILogger<DataControllerService> logger,
            ILoggerFactory loggerFactory)
        {
            _appState = appState;
            _httpContextAccessor = httpContextAccessor;
            _extensionHive = extensionHive;
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
            var userConfiguration = this.GetUserConfiguration();
            var controller = new DataSourceController(dataSource, registration, userConfiguration, logger1);

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
            var controller = new DataWriterController(dataWriter, resourceLocator, exportParameters.Configuration, logger1);

            await controller.InitializeAsync(logger2, cancellationToken);

            return controller;
        }

        private Dictionary<string, string> GetUserConfiguration()
        {
            var httpContext = _httpContextAccessor.HttpContext;

            if (httpContext is null)
                return _defaultUserConfiguration;

            if (!httpContext.Request.Headers.TryGetValue(NexusConfigurationHeaderKey, out var encodedUserConfiguration))
                return _defaultUserConfiguration;

            try
            {
                var userConfiguration = JsonSerializer
                    .Deserialize<Dictionary<string, string>>(Convert.FromBase64String(encodedUserConfiguration.First()));

                return userConfiguration is null
                    ? _defaultUserConfiguration
                    : userConfiguration;
            }
            catch
            {
                return _defaultUserConfiguration;
            }
        }
    }
}
