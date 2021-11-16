using Nexus.Core;
using Nexus.DataModel;
using Nexus.Sources;
using System;
using System.Linq;
using System.Security.Claims;

namespace Nexus.Utilities
{
    internal static class AuthorizationUtilities
    {
        public static bool IsCatalogAccessible(string catalogId, CatalogMetadata catalogMetadata, ClaimsPrincipal principal)
        {
            if (principal == null)
                return false;

            var identity = principal.Identity;

            if (identity is not null && identity.IsAuthenticated)
            {
                var isAdmin = principal.HasClaim(claim => claim.Type == Claims.IS_ADMIN && claim.Value == "true");

                var canAccessCatalog = principal.HasClaim(
                    claim => claim.Type == Claims.CAN_ACCESS_CATALOG &&
                    claim.Value.Split(";").Any(current => current == catalogId));

                var canAccessGroup = principal.HasClaim(
                    claim => claim.Type == Claims.CAN_ACCESS_GROUP &&
                    claim.Value.Split(";").Any(group 
                        => catalogMetadata.GroupMemberships is not null && 
                           catalogMetadata.GroupMemberships.Contains(group)));

                var inMemoryDataSourceFullName = typeof(InMemory).FullName ?? throw new Exception("full name is null");
                var implicitAccess = catalogId == inMemoryDataSourceFullName;

                return isAdmin || canAccessCatalog || canAccessGroup || implicitAccess;
            }

            return false;
        }

        public static bool IsCatalogEditable(ClaimsPrincipal principal, string catalogId)
        {
            if (principal == null)
                return false;

            var identity = principal.Identity;

            if (identity is not null && identity.IsAuthenticated)
            {
                var isAdmin = principal.HasClaim(claim => claim.Type == Claims.IS_ADMIN && claim.Value == "true");

                var canEditCatalog = principal.HasClaim(claim => claim.Type == Claims.CAN_EDIT_CATALOG &&
                                                        claim.Value.Split(";").Any(current => current == catalogId));

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
                var isAdmin = principal.HasClaim(claim => claim.Type == Claims.IS_ADMIN && claim.Value == "true");

                if (isAdmin)
                    return true; // not "return isAdmin"!!
            }

            // 2. other catalogs
            return !catalogMetadata.IsHidden;
        }
    }
}
