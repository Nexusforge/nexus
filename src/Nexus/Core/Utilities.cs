using Microsoft.AspNetCore.Identity;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nexus.Core
{
    public static class Utilities
    {
        public static async Task<ClaimsPrincipal> GetClaimsPrincipalAsync(string username, UserManager<IdentityUser> userManager)
        {
            var user = await userManager.FindByNameAsync(username);

            if (user == null)
                return null;

            var claims = await userManager.GetClaimsAsync(user);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Fake authentication type"));

            return principal;
        }

        public static bool IsCatalogAccessible(ClaimsPrincipal principal, string catalogId, NexusDatabase database)
        {
            if (principal == null)
                return false;

            var identity = principal.Identity;
            var catalogContainer = database.CatalogContainers.First(current => current.Id == catalogId);
            var catalogMeta = catalogContainer.CatalogMeta;

            return Utilities.IsCatalogAccessible(principal, catalogMeta);
        }

        public static bool IsCatalogAccessible(ClaimsPrincipal principal, CatalogMeta catalogMeta)
        {
            if (principal == null)
                return false;

            var identity = principal.Identity;

            if (catalogMeta.License.LicensingScheme == CatalogLicensingScheme.None)
            {
                return true;
            }
            else if (identity.IsAuthenticated)
            {
                var isAdmin = principal.HasClaim(claim => claim.Type == Claims.IS_ADMIN && claim.Value == "true");

                var canAccessCatalog = principal.HasClaim(claim => claim.Type == Claims.CAN_ACCESS_CATALOG &&
                                                          claim.Value.Split(";").Any(current => current == catalogMeta.Id));

                var canAccessGroup = principal.HasClaim(claim => claim.Type == Claims.CAN_ACCESS_GROUP &&
                                                        claim.Value.Split(";").Any(group => catalogMeta.GroupMemberships.Contains(group)));

                return isAdmin || canAccessCatalog || canAccessGroup;
            }

            return false;
        }

        public static bool IsCatalogEditable(ClaimsPrincipal principal, CatalogMeta catalogMeta)
        {
            if (principal == null)
                return false;

            var identity = principal.Identity;

            if (identity.IsAuthenticated)
            {
                var isAdmin = principal.HasClaim(claim => claim.Type == Claims.IS_ADMIN && claim.Value == "true");

                var canEditCatalog = principal.HasClaim(claim => claim.Type == Claims.CAN_EDIT_CATALOG
                                                       && claim.Value.Split(";").Any(current => current == catalogMeta.Id));

                return isAdmin || canEditCatalog;
            }

            return false;
        }

        public static bool IsCatalogVisible(ClaimsPrincipal principal, CatalogMeta catalogMeta, bool isCatalogAccessible)
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
            if (Constants.HiddenCatalogs.Contains(catalogMeta.Id))
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

        public static string GetEnumLocalization(Enum enumValue)
        {
            return EnumerationDescription.ResourceManager.GetString(enumValue.GetType().Name + "_" + enumValue.ToString());
        }

        public static string GetEnumIconName(Enum enumValue)
        {
            return EnumerationIconName.ResourceManager.GetString(enumValue.GetType().Name + "_" + enumValue.ToString());
        }

        public static List<T> GetEnumValues<T>()
        {
            return Enum.GetValues(typeof(T)).Cast<T>().ToList();
        }

        public static string FormatByteCount(long byteCount)
        {
            if (byteCount >= 1000 * 1000 * 1000)
                return $"{(double)byteCount / 1000 / 1000 / 1000:G3} GB";
            else if (byteCount >= 1000 * 1000)
                return $"{(double)byteCount / 1000 / 1000:G3} MB";
            else if (byteCount >= 1000)
                return $"{(double)byteCount / 1000:G3} kB";
            else
                return $"{(double)byteCount:F0} B";
        }
    }
}
