using Nexus.Core;
using Nexus.Sources;
using System.Security.Claims;
using System.Text.RegularExpressions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Utilities
{
    internal static class AuthorizationUtilities
    {
        public static bool IsCatalogReadable(string catalogId, CatalogMetadata catalogMetadata, ClaimsPrincipal? owner, ClaimsPrincipal principal)
        {
            var identity = principal.Identity;

            if (identity is not null && identity.IsAuthenticated)
            {
                if (catalogId == CatalogContainer.RootCatalogId)
                    return true;

                var isAdmin = principal.IsInRole(NexusRoles.ADMINISTRATOR);
                var isOwner = owner is not null && owner?.FindFirstValue(Claims.Subject) == principal.FindFirstValue(Claims.Subject);

                var canReadCatalog = principal.HasClaim(
                    claim => claim.Type == NexusClaims.CAN_READ_CATALOG &&
                    Regex.IsMatch(catalogId, claim.Value));

                var canAccessGroup = catalogMetadata.GroupMemberships is not null && principal.HasClaim(
                    claim => claim.Type == NexusClaims.CAN_READ_CATALOG_GROUP &&
                    catalogMetadata.GroupMemberships.Any(group => Regex.IsMatch(group, claim.Value)));

                var implicitAccess = 
                    catalogId == Sample.LocalCatalogId || 
                    catalogId == Sample.RemoteCatalogId;

                return isAdmin || isOwner || canReadCatalog || canAccessGroup || implicitAccess;
            }

            return false;
        }

        public static bool IsCatalogWritable(string catalogId, ClaimsPrincipal principal)
        {
            var identity = principal.Identity;

            if (identity is not null && identity.IsAuthenticated)
            {
                var isAdmin = principal.IsInRole(NexusRoles.ADMINISTRATOR);

                var canWriteCatalog = principal.HasClaim(claim => claim.Type == NexusClaims.CAN_WRITE_CATALOG &&
                                                        Regex.IsMatch(catalogId, claim.Value));

                return isAdmin || canWriteCatalog;
            }

            return false;
        }
    }
}
