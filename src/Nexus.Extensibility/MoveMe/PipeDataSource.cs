using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    [ExtensionIdentification("Nexus.Pipe", "Nexus Pipe", "Provides access to databases via pipes.")]
    public class PipeDataSource : IDataSource, IDisposable
    {
        #region Fields

        private PipeCommunicator _communicator;

        #endregion

        #region Properties

        public Uri ResourceLocator { private get; set; }

        public ILogger Logger { private get; set; }

        public Dictionary<string, string> Parameters { private get; set; }

        #endregion

        #region Methods

        public async Task OnParametersSetAsync()
        {
#warning add support for named pipes

            if (!this.Parameters.TryGetValue("command", out var command))
                throw new KeyNotFoundException("The command parameter must be provided.");

            var arguments = this.Parameters.ContainsKey("arguments")
                ? this.Parameters["arguments"]
                : string.Empty;

            _communicator = new PipeCommunicator(command, arguments);
            _communicator.Connect();
        }

        public async Task<List<Catalog>> GetCatalogsAsync(CancellationToken cancellationToken)
        {
            var timeoutToken = new CancellationTokenSource();
            timeoutToken.CancelAfter(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutToken.Cancel());

            var request = new CatalogsRequest();
            var response = await _communicator.TranceiveAsync<CatalogsRequest, CatalogResponse>(request, cancellationToken);

            return response.Catalogs;
        }

        public Task<(DateTime Begin, DateTime End)> GetCatalogTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<double> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task ReadSingleAsync<T>(Dataset dataset, ReadResult<T> readResult, DateTime begin, DateTime end, CancellationToken cancellationToken) where T : unmanaged
        {
            throw new NotImplementedException();
        }

        #endregion

        #region IDisposable

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _communicator.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
