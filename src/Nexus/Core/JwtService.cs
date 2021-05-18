using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nexus.Core
{
    public class JwtService<TUser> where TUser : class
    {
        #region Fields

        private static JwtSecurityTokenHandler _tokenHandler = new JwtSecurityTokenHandler();
        private SignInManager<TUser> _signInManager;
        private UserManager<TUser> _userManager;

        #endregion

        #region Constructors

        public JwtService(SignInManager<TUser> signInManager, UserManager<TUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        #endregion

        #region Methods

        public async Task<(string, bool)> GenerateTokenAsync(UserCredentials credentials)
        {
            string result;
            var success = false;
            var user = await _signInManager.UserManager.FindByNameAsync(credentials.Username);

            if (user != null)
            {
                var signInResult = await _signInManager.CheckPasswordSignInAsync(user, credentials.Password, false);
                
                var isConfirmed = 
                    !Program.Options.RequireConfirmedAccount ||
                    await _userManager.IsEmailConfirmedAsync(user);

                if (isConfirmed)
                {
                    if (signInResult.Succeeded)
                    {
                        var claims = await _signInManager.UserManager.GetClaimsAsync(user);
                        claims.Add(new Claim(ClaimTypes.Name, credentials.Username));
                        claims.Add(new Claim(ClaimTypes.Email, credentials.Username));

                        var signingCredentials = new SigningCredentials(Startup.SecurityKey, SecurityAlgorithms.HmacSha256);

                        var token = new JwtSecurityToken(issuer: "Nexus",
                                                         audience: signingCredentials.Algorithm,
                                                         claims: claims,
                                                         expires: DateTime.UtcNow.AddSeconds(30),
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
