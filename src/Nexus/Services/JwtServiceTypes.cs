using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Nexus.Services
{
    public class AuthenticateResponse
    {
        public string JwtToken { get; set; }

        public string RefreshToken { get; set; }

        public AuthenticateResponse(string jwtToken, string refreshToken)
        {
            JwtToken = jwtToken;
            RefreshToken = refreshToken;
        }
    }

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

        [Key]
        [JsonIgnore]
        internal int? Id { get; set; }

        /// <summary>
        /// Gets or sets the refresh token.
        /// </summary>
        public string Token { get; init; }

        /// <summary>
        /// Gets or sets the date/time when the token expires.
        /// </summary>
        public DateTime Expires { get; init; }

        /// <summary>
        /// Gets a value that indicates if the token has expired.
        /// </summary>
        public bool IsExpired => DateTime.UtcNow >= Expires;
    }
}
