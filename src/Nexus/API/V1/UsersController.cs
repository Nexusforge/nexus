using Microsoft.AspNetCore.Mvc;
using Nexus.Models;
using Nexus.Services;
using System.Threading.Tasks;

namespace Nexus.Controllers.V1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class UsersController : ControllerBase
    {
        #region Fields

        private JwtService _jwtService;

        #endregion

        #region Constructors

        public UsersController(JwtService jwtService)
        {
            _jwtService = jwtService;
        }

        #endregion

        /// <summary>
        /// Creates a bearer token.
        /// </summary>
        /// <returns></returns>
        [HttpPost("authenticate")]
        public async Task<ActionResult<string>> AuthenticateAsync(AuthenticateRequest authenticateRequest)
        {
#warning Should be extended to be like https://jasonwatmore.com/post/2020/05/25/aspnet-core-3-api-jwt-authentication-with-refresh-tokens

            (var result, var success) = await _jwtService.GenerateTokenAsync(authenticateRequest);

            if (success)
                return this.Ok(result);

            else
                return this.Unauthorized(result);
        }
    }
}
