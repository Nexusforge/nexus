using Nexus.Core;
using Nexus.DataModel;
using System.Linq;
using System.Security.Claims;

namespace Nexus.Utilities
{
    internal static class AuthorizationUtilities
    {
        public static bool IsCatalogAccessible(ClaimsPrincipal principal, CatalogContainer catalogContainer)
        {
            if (principal == null)
                return false;

            var catalogMetadata = catalogContainer.CatalogMetadata;

            return AuthorizationUtilities.IsCatalogAccessible(principal, catalogContainer.Id, catalogMetadata);
        }

        public static bool IsCatalogAccessible(ClaimsPrincipal principal, string catalogId, CatalogMetadata catalogMetadata)
        {
            if (principal == null)
                return false;

            var identity = principal.Identity;

            if (identity.IsAuthenticated)
            {
                var isAdmin = principal.HasClaim(claim => claim.Type == Claims.IS_ADMIN && claim.Value == "true");

                var canAccessCatalog = principal.HasClaim(claim => claim.Type == Claims.CAN_ACCESS_CATALOG &&
                                                          claim.Value.Split(";").Any(current => current == catalogId));

                var canAccessGroup = principal.HasClaim(claim => claim.Type == Claims.CAN_ACCESS_GROUP &&
                                                        claim.Value.Split(";").Any(group => catalogMetadata.GroupMemberships.Contains(group)));

                return isAdmin || canAccessCatalog || canAccessGroup;
            }

            return false;
        }

        public static bool IsCatalogEditable(ClaimsPrincipal principal, string catalogId)
        {
            if (principal == null)
                return false;

            var identity = principal.Identity;

            if (identity.IsAuthenticated)
            {
                var isAdmin = principal.HasClaim(claim => claim.Type == Claims.IS_ADMIN && claim.Value == "true");

                var canEditCatalog = principal.HasClaim(claim => claim.Type == Claims.CAN_EDIT_CATALOG
                                                       && claim.Value.Split(";").Any(current => current == catalogId));

                return isAdmin || canEditCatalog;
            }

            return false;
        }

        public static bool IsCatalogVisible(ClaimsPrincipal principal, string catalogId, CatalogMetadata catalogMeta, bool isCatalogAccessible)
        {
            var identity = principal.Identity;

            // 1. catalog is visible if user is admin (this check must come before 2.)
            if (identity.IsAuthenticated)
            {
                var isAdmin = principal.HasClaim(claim => claim.Type == Claims.IS_ADMIN && claim.Value == "true");

                if (isAdmin)
                    return true; // not "return isAdmin"!!
            }

            // 2. test catalogs are hidden by default
            if (Constants.HiddenCatalogs.Contains(catalogId))
                return false;

            // 3. other catalogs

            // catalog is hidden, addtional checks required
            if (catalogMeta.IsHidden)
                // ignore hidden property in case user has access to catalog
                return isCatalogAccessible;

            // catalog is visible
            else
                return true;
        }
    }
}
