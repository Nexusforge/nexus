#pragma warning disable CS1591 // Fehledes XML-Kommentar für öffentlich sichtbaren Typ oder Element

using Nexus.Services;
using System.Security.Claims;

namespace Nexus.Core
{
    public record NexusUser(string UserId, List<Claim> Claims, List<RefreshToken> RefreshTokens);
}

#pragma warning restore CS1591 // Fehledes XML-Kommentar für öffentlich sichtbaren Typ oder Element
