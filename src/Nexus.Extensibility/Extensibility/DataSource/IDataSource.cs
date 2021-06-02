using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public interface IDataSource
    {
        #region Properties

        Uri ResourceLocator { set; }

        ILogger Logger { set; }

        Dictionary<string, string> Parameters { set; }

        #endregion

        #region Methods

        Task OnParametersSetAsync()
        {
            return Task.CompletedTask;
        }

        Task<List<Catalog>> GetCatalogsAsync(CancellationToken cancellationToken);

        Task<(DateTime Begin, DateTime End)> GetCatalogTimeRangeAsync(string catalogId,
                                                                      CancellationToken cancellationToken);

        Task<double> GetAvailabilityAsync(string catalogId,
                                          DateTime begin,
                                          DateTime end,
                                          CancellationToken cancellationToken);

        Task ReadSingleAsync<T>(Dataset dataset,
                                ReadResult<T> readResult,
                                DateTime begin,
                                DateTime end,
                                CancellationToken cancellationToken)
            where T : unmanaged;

        #endregion
    }
}
