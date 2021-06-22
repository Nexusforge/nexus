using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    [ExtensionIdentification("Nexus.Rpc", "Nexus RPC", "Provides access to databases via remote procedure calls.")]
    public class RpcDataSource : IDataSource, IDisposable
    {
        #region Fields

        private static int API_LEVEL = 1;
        private RpcCommunicator _communicator;

        #endregion

        #region Properties

        /* Possible features to be implemented for this data source:
         * 
         * Transports: 
         *      - anonymous pipes (done)
         *      - named pipes client
         *      - tcp client
         *      - shared memory
         *      - ...
         *      
         * Protocols:
         *      - simplified SignalR + binary data stream (done)
         *      - 0mq
         *      - messagepack
         *      - gRPC
         *      - ...
         */

        public Uri ResourceLocator { private get; set; }

        public Dictionary<string, string> Configuration { private get; set; }

        public ILogger Logger { private get; set; }

        #endregion

        #region Methods

        public async Task OnParametersSetAsync()
        {
            if (!this.Configuration.TryGetValue("command", out var command))
                throw new KeyNotFoundException("The command parameter must be provided.");

            var arguments = this.Configuration.ContainsKey("arguments")
                ? this.Configuration["arguments"]
                : string.Empty;

            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromSeconds(10));

            _communicator = new RpcCommunicator(command, arguments, this.Logger);

            await _communicator.ConnectAsync(timeoutTokenSource.Token);

            var apiLevel = (await _communicator.InvokeAsync<ApiLevelResponse>(
                "GetApiLevel",
                new object[0], 
                timeoutTokenSource.Token
            )).ApiLevel;

            if (apiLevel < 1 || apiLevel > RpcDataSource.API_LEVEL)
                throw new Exception($"The API level '{apiLevel}' is not supported.");

            await _communicator.SendAsync("SetParameters", new object[]
            {
                this.ResourceLocator.ToString(),
                this.Configuration
            }, timeoutTokenSource.Token);
        }

        public async Task<List<Catalog>> GetCatalogsAsync(CancellationToken cancellationToken)
        {
            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var response = await _communicator.InvokeAsync<CatalogsResponse>(
                "GetCatalogs",
                null, 
                timeoutTokenSource.Token
            );

            response.Catalogs.ForEach(catalog => catalog.Initialize());

            return response.Catalogs;
        }

        public async Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var response = await _communicator.InvokeAsync<TimeRangeResponse>(
                "GetTimeRange",
                new object[] { catalogId },
                timeoutTokenSource.Token
            );

            var begin = response.Begin.ToUniversalTime();
            var end = response.End.ToUniversalTime();

            return (begin, end);
        }

        public async Task<double> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var response = await _communicator.InvokeAsync<AvailabilityResponse>(
                "GetAvailability", 
                new object[] { catalogId, begin, end },
                timeoutTokenSource.Token
            );

            return response.Availability;
        }

        public async Task ReadSingleAsync(Dataset dataset, ReadResult result, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var elementCount = result.Data.Length / dataset.ElementSize;

            var response = await _communicator.InvokeAsync<ReadSingleResponse>(
                "ReadSingle", 
                new object[] { dataset.GetPath(), elementCount, begin, end },
                timeoutTokenSource.Token
            );

            await _communicator.ReadRawAsync(result.Data, timeoutTokenSource.Token);
            await _communicator.ReadRawAsync(result.Status, timeoutTokenSource.Token);
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
