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
        /// The primary key.
        /// </summary>
        [Key]
        [JsonIgnore]
        public int? Id { get; init; }

        /// <summary>
        /// The refresh token.
        /// </summary>
        public string Token { get; init; }

        /// <summary>
        /// The date/time when the token was created.
        /// </summary>
        public DateTime Created { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// The date/time when the token expires.
        /// </summary>
        public DateTime Expires { get; init; }

        /// <summary>
        /// The date/time when the token was revoked.
        /// </summary>
        public DateTime? Revoked { get; set; }

        /// <summary>
        /// The token that replaced this one.
        /// </summary>
        public string? ReplacedByToken { get; set; }

        /// <summary>
        /// A boolean that indicates if the token has expired.
        /// </summary>
        public bool IsExpired => DateTime.UtcNow >= Expires;

        /// <summary>
        /// A boolean that indicates if the token has been revoked.
        /// </summary>
        public bool IsRevoked => Revoked is not null;

        /// <summary>
        /// A boolean that indicates if the token is active.
        /// </summary>
        public bool IsActive => !IsRevoked && !IsExpired;

        /// <summary>
        /// The owner of this token.
        /// </summary>
        [Required]
        [JsonIgnore]
        public NexusUser Owner { get; set; } = default!;
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
        // properties would become part of the generated clients!

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
    /// Describes an OpenID connect provider.
    /// </summary>
    /// <param name="Scheme">The scheme.</param>
    /// <param name="DisplayName">The display name.</param>
    public record AuthenticationSchemeDescription(
        string Scheme,
        string DisplayName);

    /// <summary>
    /// A package reference.
    /// </summary>
    /// <param name="Id">The unique identifier of the package reference.</param>
    /// <param name="Provider">The provider which loads the package.</param>
    /// <param name="Configuration">The configuration of the package reference.</param>
    public record PackageReference(
        Guid Id,
        string Provider,
        Dictionary<string, string> Configuration);

    /// <summary>
    /// A structure for export parameters.
    /// </summary>
    /// <param name="Begin">The start date/time.</param>
    /// <param name="End">The end date/time.</param>
    /// <param name="FilePeriod">The file period.</param>
    /// <param name="Type">The writer type.</param>
    /// <param name="ResourcePaths">The resource paths to export.</param>
    /// <param name="Configuration">The configuration.</param>
    public record ExportParameters(
        DateTime Begin,
        DateTime End,
        TimeSpan FilePeriod,
        string Type,
        string[] ResourcePaths,
        JsonElement? Configuration);

    /// <summary>
    /// An extension description.
    /// </summary>
    /// <param name="Type">The extension type.</param>
    /// <param name="Description">A nullable description.</param>
    /// <param name="ProjectUrl">A nullable project website URL.</param>
    /// <param name="RepositoryUrl">A nullable source repository URL.</param>
    /// <param name="AdditionalInformation">Additional information about the extension.</param>
    public record ExtensionDescription(
        string Type, 
        string? Description,
        string? ProjectUrl,
        string? RepositoryUrl,
        JsonElement AdditionalInformation);

    /// <summary>
    /// A structure for catalog information.
    /// </summary>
    /// <param name="Id">The identifier.</param>
    /// <param name="Title">The title.</param>
    /// <param name="Contact">A nullable contact.</param>
    /// <param name="Readme">A nullable readme.</param>
    /// <param name="License">A nullable license.</param>
    /// <param name="IsReadable">A boolean which indicates if the catalog is accessible.</param>
    /// <param name="IsWritable">A boolean which indicates if the catalog is editable.</param>
    /// <param name="IsReleased">A boolean which indicates if the catalog is released.</param>
    /// <param name="IsVisible">A boolean which indicates if the catalog is visible.</param>
    /// <param name="IsOwner">A boolean which indicates if the catalog is owned by the current user.</param>
    /// <param name="DataSourceInfoUrl">A nullable info URL of the data source.</param>
    /// <param name="DataSourceType">The data source type.</param>
    /// <param name="DataSourceRegistrationId">The data source registration identifier.</param>
    /// <param name="PackageReferenceId">The package reference identifier.</param>
    public record CatalogInfo(
        string Id,
        string Title,
        string? Contact,
        string? Readme,
        string? License,
        bool IsReadable,
        bool IsWritable,
        bool IsReleased,
        bool IsVisible,
        bool IsOwner,
        string? DataSourceInfoUrl,
        string DataSourceType,
        Guid DataSourceRegistrationId,
        Guid PackageReferenceId);

    /// <summary>
    /// A structure for catalog metadata.
    /// </summary>
    /// <param name="Contact">The contact.</param>
    /// <param name="GroupMemberships">A list of groups the catalog is part of.</param>
    /// <param name="Overrides">Overrides for the catalog.</param>
    public record CatalogMetadata(
        string? Contact,
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
        double[] Data);

    /// <summary>
    /// A data source registration.
    /// </summary>
    /// <param name="Id">The unique identifier of the data source registration.</param>
    /// <param name="Type">The type of the data source.</param>
    /// <param name="ResourceLocator">An URL which points to the data.</param>
    /// <param name="Configuration">Configuration parameters for the instantiated source.</param>
    /// <param name="InfoUrl">An optional info URL.</param>
    /// <param name="ReleasePattern">An optional regular expressions pattern to select the catalogs to be released. By default, all catalogs will be released.</param>
    /// <param name="VisibilityPattern">An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible.</param>
    public record DataSourceRegistration(
        Guid Id,
        string Type,
        Uri ResourceLocator,
        JsonElement? Configuration,
        string? InfoUrl = default,
        string ReleasePattern = ".*",
        string VisibilityPattern = ".*");

    /// <summary>
    /// Description of a job.
    /// </summary>
    /// <param name="Id">The global unique identifier.</param>
    /// <param name="Owner">The owner of the job.</param>
    /// <param name="Type">The job type</param>
    /// <param name="Parameters">The job parameters.</param>
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
    /// <param name="ExceptionMessage">The nullable exception message.</param>
    /// <param name="Result">The nullable result.</param>
    public record JobStatus(
        DateTime Start,
        TaskStatus Status,
        double Progress,
        string? ExceptionMessage,
        object? Result);
}
