using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nexus.Core;
using Nexus.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class JwtService
    {
        #region Fields

        private ConcurrentDictionary<string, List<RefreshToken>> _refreshTokenStore = new ConcurrentDictionary<string, List<RefreshToken>>();

        private SignInManager<IdentityUser> _signInManager;
        private UserManager<IdentityUser> _userManager;
        private UsersOptions _usersOptions;
        private SecurityOptions _securityOptions;
        private SigningCredentials _signingCredentials;

        #endregion

        #region Constructors

        public JwtService(
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager, 
            IOptions<UsersOptions> usersOptions,
            IOptions<SecurityOptions> securityOptions)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _usersOptions = usersOptions.Value;
            _securityOptions = securityOptions.Value;

            var key = Convert.FromBase64String(_securityOptions.Base64JwtSigningKey);
            _signingCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature);
        }

        #endregion

        #region Methods

        public async Task<(AuthenticateResponse?, string?)> AuthenticateAsync(AuthenticateRequest authenticateRequest)
        {
            // get user
            var user = await _signInManager.UserManager.FindByNameAsync(authenticateRequest.Username);

            if (user is null)
                return (null, "The user does not exist.");

            // check if user e-mail address is confirmed
            var isConfirmed =
                !_usersOptions.VerifyEmail ||
                await _userManager.IsEmailConfirmedAsync(user);

            if (!isConfirmed)
                return (null, "The e-mail address is not confirmed.");

            // sign in
            var signInResult = await _signInManager.CheckPasswordSignInAsync(user, authenticateRequest.Password, false);

            if (!signInResult.Succeeded)
                return (null, "Sign-in failed.");

            // generate authentication response
            var response = await this.GenerateAuthenticationResponseAsync(user);

            return (response, null);
        }

        public async Task<(AuthenticateResponse?, string?)> RefreshTokenAsync(string token)
        {
            // get user and refresh token
            var (success, user, refreshToken, message) = await this.TryGetUserAndTokenAsync(token);

            if (!success)
                return (null, message);

            // check token
            if (refreshToken.IsExpired)
                return (null, "The refresh token is expired.");

            // generate authentication response
            var response = await this.GenerateAuthenticationResponseAsync(user);

            return (response, null);
        }

        public Task RevokeTokenAsync(string token)
        {
            return Task.Run(() =>
            {
                var usernameEntry = _refreshTokenStore.FirstOrDefault(entry =>
                {
                    var refreshTokens = entry.Value;

                    lock (refreshTokens)
                    {
                        refreshTokens.RemoveAll(current => current.Token == token);
                    }
                });
            });
        }

        private async Task<(bool, IdentityUser?, RefreshToken?, string?)> TryGetUserAndTokenAsync(string token)
        {
            // find token and username
            RefreshToken refreshToken = null!;

            var usernameEntry = _refreshTokenStore.FirstOrDefault(entry =>
            {
                var refreshTokens = entry.Value;

                lock (refreshTokens)
                {
                    refreshToken = refreshTokens.FirstOrDefault(current => current.Token == token);
                    return refreshToken is not null;
                }
            });

            if (usernameEntry.Equals(default))
                return (false, null, null, "The refresh token is invalid.");

            // find user
            var user = await _signInManager.UserManager.FindByNameAsync(usernameEntry.Key);

            if (user is null)
                return (false, null, null, "The user does not exist.");

            return (true, user, refreshToken, null);
        }

        private async Task<AuthenticateResponse> GenerateAuthenticationResponseAsync(IdentityUser user)
        {
            // get user claims
            var claims = await _signInManager.UserManager.GetClaimsAsync(user);

            // generate a token pair
            var jwtToken = this.GenerateJwtToken(user.Id, claims);
            var refreshToken = this.GenerateRefreshToken();

            // clean up list of refresh tokens and save new refresh token
            var refreshTokens = _refreshTokenStore.GetOrAdd(user.Id, new List<RefreshToken>());

            lock (refreshTokens)
            {
                refreshTokens.RemoveAll(current => current.IsExpired);
            }

            refreshTokens.Add(refreshToken);

            // return response
            var authenticateResponse = new AuthenticateResponse(jwtToken, refreshToken.Token);

            return authenticateResponse;
        }

        private string GenerateJwtToken(string userId, IList<Claim> claims)
        {
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new Claim[]
                {
                    new Claim(ClaimTypes.Name, userId)
                }.Concat(claims)),
                Expires = DateTime.UtcNow.AddMinutes(60),
                SigningCredentials = _signingCredentials
            };

            var tokenHandler = new JsonWebTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return token;
        }

        private RefreshToken GenerateRefreshToken()
        {
            var randomBytes = RandomNumberGenerator.GetBytes(64);
            var token = Convert.ToBase64String(randomBytes);
            var expires = DateTime.UtcNow.AddDays(1);

            return new RefreshToken(token, expires);
        }

        #endregion
    }
}
