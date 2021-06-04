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

            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromSeconds(10));

            _communicator = new PipeCommunicator(command, arguments, this.Logger);
            await _communicator.ConnectAsync(timeoutTokenSource.Token);
        }

        public async Task<List<Catalog>> GetCatalogsAsync(CancellationToken cancellationToken)
        {
            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var request = new CatalogsRequest();
            var response = await _communicator.TranceiveAsync<CatalogsRequest, CatalogsResponse>(request, cancellationToken);

            return response.Catalogs;
        }

        public async Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var request = new TimeRangeRequest(catalogId);
            var response = await _communicator.TranceiveAsync<TimeRangeRequest, TimeRangeResponse>(request, cancellationToken);

            var begin = response.Begin.ToUniversalTime();
            var end = response.End.ToUniversalTime();

            return (begin, end);
        }

        public async Task<double> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var request = new AvailabilityRequest(catalogId, begin, end);
            var response = await _communicator.TranceiveAsync<AvailabilityRequest, AvailabilityResponse>(request, cancellationToken);

            return response.Availability;
        }

        public async Task ReadSingleAsync<T>(Dataset dataset, ReadResult<T> readResult, DateTime begin, DateTime end, CancellationToken cancellationToken) where T : unmanaged
        {
            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var request = new ReadSingleRequest(dataset, readResult.Length, begin, end);
            var response = await _communicator.TranceiveAsync<ReadSingleRequest, ReadSingleResponse>(request, cancellationToken);

            var data = _communicator.ReadAsync(readResult.Data, cancellationToken);
            var status = _communicator.ReadAsync(readResult.Status, cancellationToken);
        }

        private CancellationTokenSource GetTimeoutTokenSource(TimeSpan timeout)
        {
            var timeoutToken = new CancellationTokenSource();
            timeoutToken.CancelAfter(timeout);

            return timeoutToken;
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
                    _communicator?.Dispose();
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
