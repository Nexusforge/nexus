using Nexus.Core;
using Nexus.Sources;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace Nexus.Utilities
{
    internal static class AuthorizationUtilities
    {
        public static bool IsCatalogAccessible(string catalogId, CatalogMetadata catalogMetadata, ClaimsPrincipal principal)
        {
            if (principal is null)
                return false;

            var identity = principal.Identity;

            if (identity is not null && identity.IsAuthenticated)
            {
                var isAdmin = principal.HasClaim(claim => claim.Type == NexusClaims.IS_ADMIN && claim.Value == "true");

                var canAccessCatalog = principal.HasClaim(
                    claim => claim.Type == NexusClaims.CAN_ACCESS_CATALOG &&
                    Regex.IsMatch(catalogId, claim.Value));

                var canAccessGroup = catalogMetadata.GroupMemberships is not null && principal.HasClaim(
                    claim => claim.Type == NexusClaims.CAN_ACCESS_GROUP &&
                    catalogMetadata.GroupMemberships.Any(group => Regex.IsMatch(group, claim.Value)));

                var inMemoryDataSourceFullName = typeof(InMemory).FullName ?? throw new Exception("full name is null");
                var implicitAccess = catalogId == inMemoryDataSourceFullName;

                return isAdmin || canAccessCatalog || canAccessGroup || implicitAccess;
            }

            return false;
        }

        public static bool IsCatalogEditable(ClaimsPrincipal principal, string catalogId)
        {
            if (principal is null)
                return false;

            var identity = principal.Identity;

            if (identity is not null && identity.IsAuthenticated)
            {
                var isAdmin = principal.HasClaim(claim => claim.Type == NexusClaims.IS_ADMIN && claim.Value == "true");

                var canEditCatalog = principal.HasClaim(claim => claim.Type == NexusClaims.CAN_EDIT_CATALOG &&
                                                        Regex.IsMatch(catalogId, claim.Value));

                return isAdmin || canEditCatalog;
            }

            return false;
        }

        public static bool IsCatalogVisible(ClaimsPrincipal principal, CatalogMetadata catalogMetadata)
        {
            var identity = principal.Identity;

            // 1. catalog is visible if user is admin
            if (identity is not null && identity.IsAuthenticated)
            {
                var isAdmin = principal.HasClaim(claim => claim.Type == NexusClaims.IS_ADMIN && claim.Value == "true");

                if (isAdmin)
                    return true; // not "return isAdmin"!!
            }

            // 2. other catalogs
            return !catalogMetadata.IsHidden;
        }
    }
}
