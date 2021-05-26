using Nexus.DataModel;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public static class DataSourceExtensions
    {
        public static async Task<Project> GetProjectAsync(this IDataSource dataSource, string projectId, CancellationToken cancellationToken)
        {
            var projects = await dataSource.GetDataModelAsync(cancellationToken);
            var project = projects.FirstOrDefault(project => project.Id == projectId);

            if (project is null)
                throw new Exception($"The project '{projectId}' does not exist.");

            return project;
        }
    }
}
