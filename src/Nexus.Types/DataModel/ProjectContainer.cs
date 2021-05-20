using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Id,nq}")]
    public class ProjectContainer
    {
        #region "Constructors"

        public ProjectContainer(string id)
        {
            this.Id = id;
            this.Project = new Project(id);
        }

        private ProjectContainer()
        {
            //
        }

        #endregion

        #region "Properties"

        public string Id { get; set; }

        public string PhysicalName => this.Id.TrimStart('/').Replace('/', '_');

        public Project Project { get; set; }

        public ProjectMeta ProjectMeta { get; set; }

        #endregion

        #region Methods

        public void Initialize()
        {
            this.Project.Initialize();
        }

        public SparseProject ToSparseProject(List<Dataset> datasets)
        {
            var project = new SparseProject(this.Id, this.ProjectMeta.License);
            var channels = datasets.Select(dataset => dataset.Channel).Distinct().ToList();

            project.Channels = channels.Select(reference =>
            {
                var channelMeta = this.ProjectMeta.Channels.First(channelMeta => channelMeta.Id == reference.Id);

                var channel = new Channel(reference.Id, project)
                {
                    Name = reference.Name,
                    Group = reference.Group,

                    Unit = !string.IsNullOrWhiteSpace(channelMeta.Unit) 
                        ? channelMeta.Unit
                        : reference.Unit,

                    Description = !string.IsNullOrWhiteSpace(channelMeta.Description)
                        ? channelMeta.Description
                        : reference.Description
                };

                var referenceDatasets = datasets.Where(dataset => (Channel)dataset.Channel == reference);

                channel.Datasets = referenceDatasets.Select(referenceDataset =>
                {
                    return new Dataset(referenceDataset.Id, channel)
                    {
                        DataType = referenceDataset.DataType,
                        Registration = referenceDataset.Registration
                    };
                }).ToList();

                return channel;
            }).ToList();

            return project;
        }

        #endregion
    }
}