namespace Nexus.Client
{
    /// <summary>
    /// Contains extension methods for instances of type <see cref="NexusClient"/>.
    /// </summary>
    public static class NexusClientExtensions
    {
        /// <summary>
        /// Convenience method to attach configuration data to subsequent Nexus API requests.
        /// </summary>
        /// <param name="client">The client to attach the configuration to.</param>
        /// <param name="configuration">The configuration data as an (key, value) tuple array.</param>
        public static IDisposable AttachConfiguration(this NexusClient client, params (string, string)[] configuration)
        {
            return client.AttachConfiguration(configuration.ToDictionary(entry => entry.Item1, entry => entry.Item2));
        }
    }
}