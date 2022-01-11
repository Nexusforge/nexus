using System.Security.Claims;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface IUserManagerWrapper
    {
        Task<ClaimsPrincipal?> GetClaimsPrincipalAsync(string username);
    }
}
