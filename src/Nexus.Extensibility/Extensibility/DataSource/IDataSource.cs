﻿using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public interface IDataSource
    {
        Task SetContextAsync(
            DataSourceContext context, 
            CancellationToken cancellationToken);

        Task<List<Catalog>> GetCatalogsAsync(
            CancellationToken cancellationToken);

        Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(
            string catalogId,
            CancellationToken cancellationToken);

        Task<double> GetAvailabilityAsync(
            string catalogId, 
            DateTime begin,
            DateTime end,                             
            CancellationToken cancellationToken);

        Task ReadAsync(
            DateTime begin,
            DateTime end,
            ReadRequest[] requests,
            CancellationToken cancellationToken);
    }
}
