﻿using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    [ExtensionIdentification("Nexus.Rpc", "Nexus RPC", "Provides access to databases via remote procedure calls.")]
    public class RpcDataSource : IDataSource, IDisposable
    {
        #region Fields

        private RpcCommunicator _communicator;

        #endregion

        #region Properties

        public Uri ResourceLocator { private get; set; }

        public Dictionary<string, string> Parameters { private get; set; }

        public ILogger Logger { private get; set; }

        #endregion

        #region Methods

        public async Task OnParametersSetAsync()
        {
            if (!this.Parameters.TryGetValue("command", out var command))
                throw new KeyNotFoundException("The command parameter must be provided.");

            var arguments = this.Parameters.ContainsKey("arguments")
                ? this.Parameters["arguments"]
                : string.Empty;

            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromSeconds(10));

            _communicator = new RpcCommunicator(command, arguments, this.Logger);

            await _communicator.ConnectAsync(timeoutTokenSource.Token);
            await _communicator.SendAsync("SetParameters", new object[]
            {
                this.ResourceLocator.ToString(),
                this.Parameters
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

        public async Task ReadSingleAsync<T>(Dataset dataset, ReadResult<T> readResult, DateTime begin, DateTime end, CancellationToken cancellationToken) where T : unmanaged
        {
            var timeoutTokenSource = this.GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var response = await _communicator.InvokeAsync<ReadSingleResponse>(
                "ReadSingle", 
                new object[] { dataset.GetPath(), readResult.Length, begin, end },
                timeoutTokenSource.Token
            );

            await _communicator.ReadRawAsync(readResult.Data, timeoutTokenSource.Token);
            await _communicator.ReadRawAsync(readResult.Status, timeoutTokenSource.Token);
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
