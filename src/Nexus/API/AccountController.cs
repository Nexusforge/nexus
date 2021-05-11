using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using System.Threading.Tasks;

namespace Nexus.Controllers
{
    [Route("api/v1/account")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        #region Fields

        private JwtService<IdentityUser> _jwtService;
        private ILogger _logger;

        #endregion

        #region Constructors

        public AccountController(JwtService<IdentityUser> jwtService,
                                 ILoggerFactory loggerFactory)
        {
            _jwtService = jwtService;
            _logger = loggerFactory.CreateLogger("Nexus Explorer");
        }

        #endregion

        /// <summary>
        /// Creates a bearer token.
        /// </summary>
        /// <returns></returns>
        [HttpPost("token")]
        public async Task<ActionResult<string>> GetStream(UserCredentials credentials)
        {
            var token = await _jwtService.GenerateTokenAsync(credentials);

            if (!string.IsNullOrWhiteSpace(token))
                return new JsonResult(token);
            else
                return this.Unauthorized();
        }
    }
}
