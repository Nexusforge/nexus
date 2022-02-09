using Microsoft.AspNetCore.Identity;
using Nexus.Services;

namespace Nexus.Core
{
    public class NexusUser : IdentityUser
    {
        #region Properties

        public List<RefreshToken> RefreshTokens { get; set; }

        #endregion

        #region Constructors

        public NexusUser()
        {
            //
        }

        public NexusUser(string username) : base(username)
        {
            //
        }

        #endregion
    }
}
