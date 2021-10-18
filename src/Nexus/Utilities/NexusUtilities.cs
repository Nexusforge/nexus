using Microsoft.AspNetCore.Identity;
using Nexus.Core;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nexus.Utilities
{
    internal static class NexusUtilities
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

        public static bool IsCatalogAccessible(ClaimsPrincipal principal, string catalogId, CatalogCollection catalogCollection)
        {
            if (principal == null)
                return false;

            var identity = principal.Identity;
            var catalogContainer = catalogCollection.CatalogContainers.First(current => current.Id == catalogId);
            var catalogMeta = catalogContainer.CatalogMetadata;

            return NexusUtilities.IsCatalogAccessible(principal, catalogMeta);
        }

        public static bool IsCatalogAccessible(ClaimsPrincipal principal, ResourceCatalog catalog)
        {
            if (principal == null)
                return false;

            var identity = principal.Identity;

            if (catalog.Properties.License.LicensingScheme == CatalogLicensingScheme.None)
            {
                return true;
            }
            else if (identity.IsAuthenticated)
            {
                var isAdmin = principal.HasClaim(claim => claim.Type == Claims.IS_ADMIN && claim.Value == "true");

                var canAccessCatalog = principal.HasClaim(claim => claim.Type == Claims.CAN_ACCESS_CATALOG &&
                                                          claim.Value.Split(";").Any(current => current == catalog.Id));

                var canAccessGroup = principal.HasClaim(claim => claim.Type == Claims.CAN_ACCESS_GROUP &&
                                                        claim.Value.Split(";").Any(group => catalog.Properties.GroupMemberships.Contains(group)));

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
