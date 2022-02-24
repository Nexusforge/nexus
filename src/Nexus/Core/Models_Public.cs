using Nexus.DataModel;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Core
{
    /// <summary>
    /// A refresh token.
    /// </summary>
    public class RefreshToken
    {
        internal RefreshToken(string token, DateTime expires)
        {
            Token = token;
            Expires = expires;
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
        /// Gets or sets the date/time when the token was revoked.
        /// </summary>
        public DateTime? Revoked { get; set; }

        /// <summary>
        /// Gets the token that replaced this one.
        /// </summary>
        public string? ReplacedByToken { get; set; }

        /// <summary>
        /// Gets a value that indicates if the token has expired.
        /// </summary>
        public bool IsExpired => DateTime.UtcNow >= Expires;

        /// <summary>
        /// Gets a value that indicates if the token has expired.
        /// </summary>
        public bool IsRevoked => Revoked != null;

        /// <summary>
        /// Gets a value that indicates if the token is active.
        /// </summary>
        public bool IsActive => !IsRevoked && !IsExpired;

        /// <summary>
        /// Gets or sets the owner of this token.
        /// </summary>
        [Required]
        [JsonIgnore]
        public NexusUser Owner { get; set; } = null!;
    }

    /// <summary>
    /// Represents a user.
    /// </summary>
    public class NexusUser
    {
        /// <summary>
        /// The user identifier.
        /// </summary>
        [Key]
        public string Id { get; set; } = default!;

        /// <summary>
        /// The user name.
        /// </summary>
        public string Name { get; set; } = default!;

        /// <summary>
        /// The list of refresh tokens.
        /// </summary>
        public List<RefreshToken> RefreshTokens { get; set; } = default!;

        #region Claims

        // - Do not use normal Claim here because the Claim type all its
        // properties would become part of the generated OpenAPI client!

        // - It is difficult to use dictionaries in a database, so below
        // is a workaorund using the JsonSerializer.

        // - Using this method, Include(user => user.Claims) is not required anymore.

        private ReadOnlyDictionary<Guid, NexusClaim>? _claims;

        /// <summary>
        /// The map of claims.
        /// </summary>
        [NotMapped]
        public ReadOnlyDictionary<Guid, NexusClaim> Claims
        {
            get
            {
                if (_claims is null)
                {
                    var dictionary = JsonSerializer.Deserialize<Dictionary<Guid, NexusClaim>>(ClaimsAsJson)!;
                    _claims = new ReadOnlyDictionary<Guid, NexusClaim>(dictionary);
                }

                return _claims;
            }

            set
            {
                ClaimsAsJson = JsonSerializer.Serialize(value);
                _claims = value;
            }
        }

#pragma warning disable CS1591
        [JsonIgnore]
        public string ClaimsAsJson { get; set; } = default!;
#pragma warning restore CS1591

        #endregion
    }

    /// <summary>
    /// Represents a claim.
    /// </summary>
    /// <param name="Type">The claim type.</param>
    /// <param name="Value">The claim value.</param>
    public record NexusClaim(
        string Type, 
        string Value);

    /// <summary>
    /// A refresh token request.
    /// </summary>
    /// <param name="RefreshToken">The refresh token.</param>
    public record RefreshTokenRequest(
        [Required] string RefreshToken);

    /// <summary>
    /// A revoke token request.
    /// </summary>
    /// <param name="Token">The refresh token.</param>
    public record RevokeTokenRequest(
        [Required] string Token);

    /// <summary>
    /// A token pair.
    /// </summary>
    /// <param name="AccessToken">The JWT token.</param>
    /// <param name="RefreshToken">The refresh token.</param>
    public record TokenPair(
        string AccessToken,
        string RefreshToken);

    /// <summary>
    /// A package reference.
    /// </summary>
    /// <param name="Provider">The provider which loads the package.</param>
    /// <param name="Configuration">The configuration of the package reference.</param>
    public record PackageReference(
        string Provider,
        Dictionary<string, string> Configuration);

    /// <summary>
    /// A structure for export parameters.
    /// </summary>
    /// <param name="Begin"><example>2020-02-01T00:00:00Z</example></param>
    /// <param name="End"><example>2020-02-02T00:00:00Z</example></param>
    /// <param name="FilePeriod"><example>00:00:00</example></param>
    /// <param name="Type"><example>Nexus.Writers.Csv</example></param>
    /// <param name="ResourcePaths"><example>["/IN_MEMORY/TEST/ACCESSIBLE/T1/1_s_mean", "/IN_MEMORY/TEST/ACCESSIBLE/V1/1_s_mean"]</example></param>
    /// <param name="Configuration"><example>{ "RowIndexFormat": "Index", "SignificantFigures": "4" }</example></param>
    public record ExportParameters(
        DateTime Begin,
        DateTime End,
        TimeSpan FilePeriod,
        string Type,
        string[] ResourcePaths,
        Dictionary<string, string> Configuration);

    /// <summary>
    /// An extension description.
    /// </summary>
    /// <param name="Type">The extension type.</param>
    /// <param name="Description">An optional description.</param>
    public record ExtensionDescription(
        string Type, 
        string? Description);

    /// <summary>
    /// A structure for catalog metadata.
    /// </summary>
    /// <param name="Contact">The contact.</param>
    /// <param name="IsHidden">A boolean which indicates if the catalog should be hidden.</param>
    /// <param name="GroupMemberships">A list of groups the catalog is part of.</param>
    /// <param name="Overrides">Overrides for the catalog.</param>
    public record CatalogMetadata(
        string? Contact,
        bool IsHidden,
        string[]? GroupMemberships,
        ResourceCatalog? Overrides);

    /// <summary>
    /// A catalog time range.
    /// </summary>
    /// <param name="Begin">The date/time of the first data in the catalog.</param>
    /// <param name="End">The date/time of the last data in the catalog.</param>
    public record CatalogTimeRange(
        DateTime Begin,
        DateTime End);

    /// <summary>
    /// The catalog availability.
    /// </summary>
    /// <param name="Data">The actual availability data.</param>
    public record CatalogAvailability(
        IReadOnlyDictionary<DateTime, double> Data);

    /// <summary>
    /// A backend source.
    /// </summary>
    /// <param name="Type">The type of the backend source.</param>
    /// <param name="ResourceLocator">An URL which points to the data.</param>
    /// <param name="Configuration">Configuration parameters for the instantiated source.</param>
    /// <param name="Publish">A boolean which indicates if the found catalogs should be available for everyone.</param>
    /// <param name="Disable">A boolean which indicates if this backend source should be ignored.</param>
    public record DataSourceRegistration(
        string Type,
        Uri ResourceLocator,
        IReadOnlyDictionary<string, string> Configuration,
        bool Publish,
        bool Disable = false);

    /// <summary>
    /// Description of a job.
    /// </summary>
    /// <param name="Id"><example>06f8eb30-5924-4a71-bdff-322f92343f5b</example></param>
    /// <param name="Owner"><example>test@nexus.localhost</example></param>
    /// <param name="Type"><example>export</example></param>
    /// <param name="Parameters">Job parameters.</param>
    public record Job(
        Guid Id,
        string Type,
        string Owner,
        object? Parameters);

    /// <summary>
    /// Describes the status of the job.
    /// </summary>
    /// <param name="Start">The start date/time.</param>
    /// <param name="Status">The status.</param>
    /// <param name="Progress">The progress from 0 to 1.</param>
    /// <param name="ExceptionMessage">An optional exception message.</param>
    /// <param name="Result">The optional result.</param>
    public record JobStatus(
        DateTime Start,
        TaskStatus Status,
        double Progress,
        string? ExceptionMessage,
        object? Result);

    /// <summary>
    /// Describes an OpenID connect provider.
    /// </summary>
    /// <param name="Scheme">The scheme.</param>
    /// <param name="DisplayName">The display name.</param>
    public record AuthenticationSchemeDescription(
        string Scheme,
        string DisplayName);
}
