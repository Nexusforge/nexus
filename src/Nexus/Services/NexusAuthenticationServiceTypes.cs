using Nexus.Core;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Nexus.Services
{
    /// <summary>
    /// A refresh token.
    /// </summary>
    public class RefreshToken
    {
        internal RefreshToken(string token, DateTime expires)
        {
            this.Token = token;
            this.Expires = expires;
        }

        /// <summary>
        /// Gets or sets the primary key.
        /// </summary>
        [Key]
        [JsonIgnore]
        public int? Id { get; init; }

        /// <summary>
        /// Gets or sets the refresh token.
        /// </summary>
        public string Token { get; init; }

        /// <summary>
        /// Gets or sets the date/time when the token was created.
        /// </summary>
        public DateTime Created { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets the date/time when the token expires.
        /// </summary>
        public DateTime Expires { get; init; }

        /// <summary>
        /// Gets a value that indicates if the token has expired.
        /// </summary>
        public bool IsExpired => DateTime.UtcNow >= Expires;

        [Required]
        [JsonIgnore]
        /// <summary>
        /// Gets or sets the owner of this token.
        /// </summary>
        public NexusUser Owner { get; init; }
    }
}
