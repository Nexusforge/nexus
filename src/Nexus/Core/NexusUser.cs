#pragma warning disable CS1591 // Fehledes XML-Kommentar für öffentlich sichtbaren Typ oder Element

using Microsoft.AspNetCore.Identity;
using Nexus.Services;

namespace Nexus.Core
{
    public class NexusUser : IdentityUser
    {
        #region Properties

        public List<RefreshToken> RefreshTokens { get; set; } = null!;

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

#pragma warning restore CS1591 // Fehledes XML-Kommentar für öffentlich sichtbaren Typ oder Element
