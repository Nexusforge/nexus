using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface IUserIdService
    {
        ClaimsPrincipal User { get; }

        Task<ClaimsPrincipal> GetUserAsync();
    }
}