using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public interface IDataSource : IDisposable
    {
        #region Properties

        string RootPath { set; }

        ILogger Logger { set; }

        Dictionary<string, string> Options { set; }

        #endregion

        #region Methods

        Task OnParametersSetAsync();

        Task<List<Project>> InitializeAsync();

        Task<double> GetAvailabilityAsync(string projectId, DateTime day);

        Task<ReadResult<T>> ReadSingleAsync<T>(Dataset dataset, DateTime begin, DateTime end)
            where T : unmanaged;

        #endregion
    }
}
