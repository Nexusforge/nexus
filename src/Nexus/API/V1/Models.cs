using System.ComponentModel.DataAnnotations;

namespace Nexus.API.V1
{
    #region Users

    /// <summary>
    /// An authentication request.
    /// </summary>
    /// <param name="UserId">The user ID.</param>
    /// <param name="Password">The password.</param>
    public record AuthenticateRequest(
        [Required] string UserId,
        [Required] string Password);

    /// <summary>
    /// The authentication request response.
    /// </summary>
    /// <param name="JwtToken">The JWT token. <see langword="null"/> when an error occured.</param>
    /// <param name="RefreshToken">The refresh token. <see langword="null"/> when an error occured.</param>
    /// <param name="Error">An optional error message. Not <see langword="null"/> when an error occured.</param>
    public record AuthenticateResponse(
        string? JwtToken,
        string? RefreshToken,
        string? Error);

    /// <summary>
    /// A refresh token request.
    /// </summary>
    /// <param name="RefreshToken">The refresh token.</param>
    public record RefreshTokenRequest(
        [Required] string RefreshToken);

    /// <summary>
    /// The refresh token request response.
    /// </summary>
    /// <param name="JwtToken">The JWT token. <see langword="null"/> when an error occured.</param>
    /// <param name="RefreshToken">The refresh token. <see langword="null"/> when an error occured.</param>
    /// <param name="Error">An optional error message. Not <see langword="null"/> when an error occured.</param>
    public record RefreshTokenResponse(
        string? JwtToken,
        string? RefreshToken,
        string? Error);

    /// <summary>
    /// A revoke token request.
    /// </summary>
    /// <param name="Token">The refresh token.</param>
    public record RevokeTokenRequest(
        [Required] string Token);

    /// <summary>
    /// The revoke token request response.
    /// </summary>
    /// <param name="Error">An optional error message. Not <see langword="null"/> when an error occured.</param>
    public record RevokeTokenResponse(
        string? Error);

    #endregion
}
