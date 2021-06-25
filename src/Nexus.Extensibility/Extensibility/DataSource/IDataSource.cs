using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public interface IDataSource
    {
        #region Methods

        Task SetContextAsync(DataSourceContext context, CancellationToken cancellationToken);

        Task<List<Catalog>> GetCatalogsAsync(CancellationToken cancellationToken);

        Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(string catalogId,
                                                               CancellationToken cancellationToken);

        Task<double> GetAvailabilityAsync(string catalogId,
                                          DateTime begin,
                                          DateTime end,
                                          CancellationToken cancellationToken);

        Task ReadSingleAsync(string datasetPath,
                             ReadResult result,
                             DateTime begin,
                             DateTime end,
                             CancellationToken cancellationToken);

        #endregion
    }
}
