using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.DataModel
{
    public class NexusDatabase
    {
        #region Constructors

        public NexusDatabase()
        {
            this.ProjectContainers = new List<ProjectContainer>();
        }

        #endregion

        #region Properties

        public List<ProjectContainer> ProjectContainers { get; set; }

        #endregion

        #region Methods

        public List<Project> GetProjects()
        {
            return this.ProjectContainers.Select(container => container.Project).ToList();
        }

        public bool TryFindProjectById(string id, out Project project)
        {
            var projectContainer = this.ProjectContainers.FirstOrDefault(projectContainer => projectContainer.Id == id);
            project = projectContainer?.Project;

            return project != null;
        }

        public bool TryFindChannelByIdOrName(string projectId, string channelIdOrName, out Channel channel)
        {
            channel = default;

            if (this.TryFindProjectById(projectId, out var project))
            {
                if (Guid.TryParse(channelIdOrName, out var channelId))
                    channel = project.Channels.FirstOrDefault(current => current.Id == channelId);

                if (channel == null)
                    channel = project.Channels.FirstOrDefault(current => current.Name == channelIdOrName);
            }

            return channel != null;
        }

        public bool TryFindDatasetById(string projectId, string channelIdOrName, string datsetId, out Dataset dataset)
        {
            dataset = default;

            if (this.TryFindChannelByIdOrName(projectId, channelIdOrName, out var channel))
            {
                dataset = channel.Datasets.FirstOrDefault(dataset => dataset.Id == datsetId);

                if (dataset != null)
                    return true;
            }

            return false;
        }

        public bool TryFindDataset(string path, out Dataset dataset)
        {
            var pathSegments = path.Split("/");

            if (pathSegments.Length != 6)
                throw new Exception($"The channel path '{path}' is invalid.");

            var projectName = $"/{pathSegments[1]}/{pathSegments[2]}/{pathSegments[3]}";
            var channelName = pathSegments[4];
            var datasetName = pathSegments[5];

            return this.TryFindDatasetById(projectName, channelName, datasetName, out dataset);
        }

        public bool TryFindDatasetsByGroup(string groupPath, out List<Dataset> datasets)
        {
            var groupPathSegments = groupPath.Split("/");

            if (groupPathSegments.Length != 6)
                throw new Exception($"The group path '{groupPath}' is invalid.");

            var projectName = $"/{groupPathSegments[1]}/{groupPathSegments[2]}/{groupPathSegments[3]}";
            var groupName = groupPathSegments[4];
            var datasetName = groupPathSegments[5];

            return this.TryFindDatasetsByGroup(projectName, groupName, datasetName, out datasets);
        }

        private bool TryFindDatasetsByGroup(string projectName, string groupName, string datasetName, out List<Dataset> datasets)
        {
            datasets = new List<Dataset>();

            var projectContainer = this.ProjectContainers.FirstOrDefault(projectContainer => projectContainer.Id == projectName);

            if (projectContainer != null)
            {
                var channels = projectContainer.Project.Channels
                    .Where(channel => channel.Group.Split('\n')
                    .Contains(groupName))
                    .OrderBy(channel => channel.Name)
                    .ToList();

                datasets
                    .AddRange(channels
                    .SelectMany(channel => channel.Datasets
                    .Where(dataset => dataset.Id == datasetName)));

                if (datasets.Any())
                    return true;
            }

            return false;
        }

        #endregion
    }
}