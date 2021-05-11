using System.Collections.Generic;
using System.Linq;

namespace Nexus.Database
{
    public class ProjectMeta
    {
        #region Constructors

        public ProjectMeta(string id)
        {
            this.Id = id;
            this.Contact = string.Empty;
            this.ShortDescription = string.Empty;
            this.LongDescription = string.Empty;
            this.IsQualityControlled = false;
            this.IsHidden = false;
            this.License = new ProjectLicense();
            this.Logbook = new List<string>();
            this.GroupMemberships = new List<string>();
            this.Channels = new List<ChannelMeta>();
        }

        private ProjectMeta()
        {
            //
        }

        #endregion

        #region Properties

        public string Id { get; set; }

        public string Contact { get; set; }

        public string ShortDescription { get; set; }

        public string LongDescription { get; set; }

        public bool IsQualityControlled { get; set; }

        public bool IsHidden { get; set; }

        public ProjectLicense License { get; set; }

        public List<string> Logbook { get; set; }

        public List<string> GroupMemberships { get; set; }

        public List<ChannelMeta> Channels { get; set; }

        #endregion

        #region Methods

        public void Initialize(ProjectInfo project)
        {
            if (string.IsNullOrWhiteSpace(this.ShortDescription))
                this.ShortDescription = "<no description available>";

            if (string.IsNullOrWhiteSpace(this.LongDescription))
                this.LongDescription = "<no description available>";

            // create missing channel meta instances
            var channelsToAdd = new List<ChannelMeta>();

            foreach (var referenceChannel in project.Channels)
            {
                var exists = this.Channels.Any(channel => channel.Id == referenceChannel.Id);

                if (!exists)
                    channelsToAdd.Add(new ChannelMeta(referenceChannel.Id));
            }

            this.Channels.AddRange(channelsToAdd);
            this.Channels = this.Channels.OrderBy(channel => channel.Id).ToList();
        }

        #endregion
    }
}
