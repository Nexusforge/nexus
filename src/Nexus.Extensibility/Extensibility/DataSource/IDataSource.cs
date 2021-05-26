﻿using Microsoft.Extensions.Logging;
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

        string RootPath { set; }

        ILogger Logger { set; }

        Dictionary<string, string> Options { set; }

        #endregion

        #region Methods

        Task OnParametersSetAsync()
        {
            return Task.CompletedTask;
        }

        Task<List<Project>> GetDataModelAsync(CancellationToken cancellationToken);

        Task<(DateTime Begin, DateTime End)> GetProjectTimeRangeAsync(string projectId,
                                                                      CancellationToken cancellationToken);

        Task<double> GetAvailabilityAsync(string projectId,
                                          DateTime day,
                                          CancellationToken cancellationToken);

        Task<ReadResult<T>> ReadSingleAsync<T>(Dataset dataset,
                                               DateTime begin,
                                               DateTime end,
                                               CancellationToken cancellationToken)
            where T : unmanaged;

        #endregion
    }
}
