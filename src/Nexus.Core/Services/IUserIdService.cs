using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface IUserIdService
    {
        IPAddress RemoteIpAddress { get; }
        ClaimsPrincipal User { get; }

        Task<ClaimsPrincipal> GetUserAsync();
        string GetUserId();
    }
}