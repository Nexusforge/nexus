using System.Security.Claims;

namespace Nexus.Services
{
    internal interface IUserManagerWrapper
    {
        Task<ClaimsPrincipal?> GetClaimsPrincipalAsync(string username);
    }
}
