using System;

namespace Nexus.Infrastructure
{
    public class NexusCatalogDescription
    {
        public NexusCatalogDescription(Guid guid, int version, string primaryGroupName, string secondaryGroupName, string catalogName)
        {
            this.Guid = guid;
            this.Version = version;
            this.PrimaryGroupName = primaryGroupName;
            this.SecondaryGroupName = secondaryGroupName;
            this.CatalogName = catalogName;
        }

        public Guid Guid { get; set; }

        public int Version { get; set; }

        public string PrimaryGroupName { get; set; }

        public string SecondaryGroupName { get; set; }

        public string CatalogName { get; set; }

        public void Validate()
        {
            string errorMessage;

            if (this.Version < 0)
            {
                throw new Exception(ErrorMessage.NexusCatalogDescription_InvalidVersion);
            }

            if (!NexusUtilities.CheckNamingConvention(this.PrimaryGroupName, out errorMessage))
            {
                throw new Exception($"The PrimaryGroupName is invalid: { errorMessage }");
            }

            if (!NexusUtilities.CheckNamingConvention(this.SecondaryGroupName, out errorMessage))
            {
                throw new Exception($"The SecondaryGroupName is invalid: { errorMessage }");
            }

            if (!NexusUtilities.CheckNamingConvention(this.CatalogName, out errorMessage))
            {
                throw new Exception($"The CatalogName is invalid: { errorMessage }");
            }
        }
    }
}
