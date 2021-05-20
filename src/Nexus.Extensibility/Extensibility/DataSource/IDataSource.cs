using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public interface IDataSource : IDisposable
    {
        Task<List<Project>> InitializeAsync(string rootPath, ILogger logger, Dictionary<string, string> options);

        Task<double> GetAvailabilityAsync(string projectId, DateTime day);

        Task<ReadResult<T>> ReadSingleAsync<T>(Dataset dataset, DateTime begin, DateTime end) 
            where T : unmanaged;
    }
}
