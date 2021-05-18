namespace Nexus.Core
{
    public class EmailOptions
    {
        public EmailOptions()
        {
            // unset, mutable
            this.ServerAddress = string.Empty;

            // preset, mutable
            this.Port = 25;
            this.SenderEmail = "noreply@nexus.org";
        }

        // unset, mutable
        public string ServerAddress { get; set; }


        // preset, mutable
        public uint Port { get; set; }

        public string SenderEmail { get; set; }
    }
}
