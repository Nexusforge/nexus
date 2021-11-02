using Microsoft.AspNetCore.Mvc;
using Nexus.Models.V1;
using Nexus.Services;
using System.Threading.Tasks;

namespace Nexus.Controllers.V1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class AccountController : ControllerBase
    {
        #region Fields

        private JwtService _jwtService;

        #endregion

        #region Constructors

        public AccountController(JwtService jwtService)
        {
            _jwtService = jwtService;
        }

        #endregion

        /// <summary>
        /// Creates a bearer token.
        /// </summary>
        /// <returns></returns>
        [HttpPost("token")]
        public async Task<ActionResult<string>> GetToken(UserCredentials credentials)
        {
            (var result, var success) = await _jwtService.GenerateTokenAsync(credentials);

            if (success)
                return this.Ok(result);

            else
                return this.Unauthorized(result);
        }
    }
}
