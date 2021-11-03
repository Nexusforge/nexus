using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    [ExtensionIdentification("Nexus.Builtin.Rpc", "Nexus RPC", "Provides access to databases via remote procedure calls.")]
    internal class RpcDataSource : IDataSource, IDisposable
    {
        #region Fields

        public const string Id = "Nexus.Builtin.Rpc";
        private static int API_LEVEL = 1;
        private RpcCommunicator _communicator;
        private IJsonRpcServer _rpcServer;

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

        private DataSourceContext Context { get; set; }

        #endregion

        #region Methods

        public async Task SetContextAsync(DataSourceContext context, CancellationToken cancellationToken)
        {
            this.Context = context;

            // command
            if (!this.Context.Configuration.TryGetValue("command", out var command))
                throw new KeyNotFoundException("The command parameter must be provided.");

            // listen-address
            if (!this.Context.Configuration.TryGetValue("listen-address", out var listenAddressString))
                throw new KeyNotFoundException("The listen-address parameter must be provided.");

            if (!IPAddress.TryParse(listenAddressString, out var listenAddress))
                throw new KeyNotFoundException("The listen-address parameter is not a valid IP-Address.");

            // listen-port
            if (!this.Context.Configuration.TryGetValue("listen-port", out var listenPortString))
                throw new KeyNotFoundException("The listen-port parameter must be provided.");

            if (!ushort.TryParse(listenPortString, out var listenPort))
                throw new KeyNotFoundException("The listen-port parameter is not a valid port.");

            // arguments
            var arguments = this.Context.Configuration.ContainsKey("arguments")
                ? this.Context.Configuration["arguments"]
                : string.Empty;

            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromSeconds(10));

            _communicator = new RpcCommunicator(context.ResourceLocator, command, arguments, listenAddress, listenPort, this.Context.Logger);
            _rpcServer = await _communicator.ConnectAsync(timeoutTokenSource.Token);

            var apiVersion = (await _rpcServer.GetApiVersionAsync(timeoutTokenSource.Token)).ApiVersion;

            if (apiVersion < 1 || apiVersion > RpcDataSource.API_LEVEL)
                throw new Exception($"The API level '{apiVersion}' is not supported.");

            await _rpcServer
                .SetContextAsync(context.ResourceLocator.ToString(), context.Configuration, timeoutTokenSource.Token);
        }

        public async Task<string[]> GetCatalogIdsAsync(CancellationToken cancellationToken)
        {
            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var response = await _rpcServer
                .GetCatalogIdsAsync(timeoutTokenSource.Token);

            return response.CatalogIds;
        }

        public async Task<ResourceCatalog> GetCatalogAsync(string catalogId, CancellationToken cancellationToken)
        {
            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var response = await _rpcServer
                .GetCatalogAsync(catalogId, timeoutTokenSource.Token);

            return response.Catalog;
        }

        public async Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var response = await _rpcServer
                .GetTimeRangeAsync(catalogId, timeoutTokenSource.Token);

            var begin = response.Begin.ToUniversalTime();
            var end = response.End.ToUniversalTime();

            return (begin, end);
        }

        public async Task<double> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var response = await _rpcServer
                .GetAvailabilityAsync(catalogId, begin, end, timeoutTokenSource.Token);

            return response.Availability;
        }

        public async Task ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var counter = 0.0;

            foreach (var (catalogItem, data, status) in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
                cancellationToken.Register(() => timeoutTokenSource.Cancel());

                var elementCount = data.Length / catalogItem.Representation.ElementSize;

                await _rpcServer
                    .ReadSingleAsync(catalogItem.GetPath(), elementCount, begin, end, timeoutTokenSource.Token);

                await _communicator.ReadRawAsync(data, timeoutTokenSource.Token);
                await _communicator.ReadRawAsync(status, timeoutTokenSource.Token);

                progress.Report(++counter / requests.Length);
            }
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
