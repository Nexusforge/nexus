using System.Security.Claims;
using System.Threading.Tasks;

namespace Nexus.Services
{
    public interface IUserManagerWrapper
    {
        Task InitializeAsync();
        Task<ClaimsPrincipal> GetClaimsPrincipalAsync(string username);
    }
}
