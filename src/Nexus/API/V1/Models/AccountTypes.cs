using System.Text.Json.Serialization;

namespace Nexus.Models.V1
{
    public record UserCredentials
    {
        /// <example>test@nexus.org</example>
        [JsonPropertyName("username")]
        public string Username { get; set; }

        /// <example>#test0/User1</example>
        [JsonPropertyName("password")]
        public string Password { get; set; }
    }
}
