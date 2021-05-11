using System;

namespace Nexus.Infrastructure
{
    public class NexusProjectDescription
    {
        public NexusProjectDescription(Guid guid, int version, string primaryGroupName, string secondaryGroupName, string projectName)
        {
            this.Guid = guid;
            this.Version = version;
            this.PrimaryGroupName = primaryGroupName;
            this.SecondaryGroupName = secondaryGroupName;
            this.ProjectName = projectName;
        }

        public Guid Guid { get; set; }

        public int Version { get; set; }

        public string PrimaryGroupName { get; set; }

        public string SecondaryGroupName { get; set; }

        public string ProjectName { get; set; }

        public void Validate()
        {
            string errorMessage;

            if (this.Version < 0)
            {
                throw new Exception(ErrorMessage.NexusProjectDescription_InvalidVersion);
            }

            if (!NexusUtilities.CheckNamingConvention(this.PrimaryGroupName, out errorMessage))
            {
                throw new Exception($"The PrimaryGroupName is invalid: { errorMessage }");
            }

            if (!NexusUtilities.CheckNamingConvention(this.SecondaryGroupName, out errorMessage))
            {
                throw new Exception($"The SecondaryGroupName is invalid: { errorMessage }");
            }

            if (!NexusUtilities.CheckNamingConvention(this.ProjectName, out errorMessage))
            {
                throw new Exception($"The ProjectName is invalid: { errorMessage }");
            }
        }
    }
}
