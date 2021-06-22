using Microsoft.Extensions.Configuration;
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

        IConfiguration Configuration { set; }

        #endregion

        #region Methods

        Task OnParametersSetAsync()
        {
            return Task.CompletedTask;
        }

        Task<List<Catalog>> GetCatalogsAsync(CancellationToken cancellationToken);

        Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(string catalogId,
                                                               CancellationToken cancellationToken);

        Task<double> GetAvailabilityAsync(string catalogId,
                                          DateTime begin,
                                          DateTime end,
                                          CancellationToken cancellationToken);

        Task ReadSingleAsync(Dataset dataset,
                             ReadResult result,
                             DateTime begin,
                             DateTime end,
                             CancellationToken cancellationToken);

        #endregion
    }
}
