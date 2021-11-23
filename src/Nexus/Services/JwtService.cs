using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nexus.Core;
using Nexus.DataModel;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class JwtService
    {
        #region Fields

        private static JwtSecurityTokenHandler _tokenHandler = new JwtSecurityTokenHandler();
        private SignInManager<IdentityUser> _signInManager;
        private UserManager<IdentityUser> _userManager;
        private UsersOptions _usersOptions;

        #endregion

        #region Constructors

        public JwtService(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager, IOptions<UsersOptions> usersOptions)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _usersOptions = usersOptions.Value;
        }

        #endregion

        #region Methods

        public async Task<(string, bool)> GenerateTokenAsync(AuthenticateRequest authenticateRequest)
        {
            string result;
            var success = false;
            var user = await _signInManager.UserManager.FindByNameAsync(authenticateRequest.Username);

            if (user is not null)
            {
                var signInResult = await _signInManager.CheckPasswordSignInAsync(user, authenticateRequest.Password, false);
                
                var isConfirmed = 
                    !_usersOptions.VerifyEmail ||
                    await _userManager.IsEmailConfirmedAsync(user);

                if (isConfirmed)
                {
                    if (signInResult.Succeeded)
                    {
                        var claims = await _signInManager.UserManager.GetClaimsAsync(user);
                        claims.Add(new Claim(ClaimTypes.Name, authenticateRequest.Username));
                        claims.Add(new Claim(ClaimTypes.Email, authenticateRequest.Username));

                        var signingCredentials = new SigningCredentials(Startup.SecurityKey, SecurityAlgorithms.HmacSha256);

                        var token = new JwtSecurityToken(issuer: "Nexus",
                                                         audience: signingCredentials.Algorithm,
                                                         claims: claims,
                                                         expires: DateTime.UtcNow.AddMinutes(15),
                                                         signingCredentials: signingCredentials);

                        result = _tokenHandler.WriteToken(token);
                        success = true;
                    }
                    else
                    {
                        result = "Password sign-in failed.";
                    }
                }
                else
                {
                    result = "The account has not been confirmed yet.";
                }
            }
            else
            {
                result = "The user does not exist.";
            }

            return (result, success);
        }

        #endregion
    }
}
