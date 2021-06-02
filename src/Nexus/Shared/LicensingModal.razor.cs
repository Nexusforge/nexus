﻿using MatBlazor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Nexus.Core;
using Nexus.Services;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nexus.Shared
{
    public partial class LicensingModal
    {
        #region Properties

        [Inject]
        public UserState UserState { get; set; }

        [Inject]
        public AuthenticationStateProvider AuthenticationStateProvider { get; set; }

        [Inject]
        public ToasterService ToasterService { get; set; }

        [Inject]
        public UserManager<IdentityUser> UserManager { get; set; }

        [Parameter]
        public bool IsOpen { get; set; }

        [Parameter]
        public EventCallback<bool> IsOpenChanged { get; set; }

        #endregion

        #region Methods

        private async Task Accept()
        {
            var authenticationState = await this.AuthenticationStateProvider.GetAuthenticationStateAsync();
            var principal = authenticationState.User;
            var claimType = Claims.CAN_ACCESS_CATALOG;

            if (principal.Identity.IsAuthenticated)
            {
                var user = await this.UserManager.GetUserAsync(principal);
                var claims = await this.UserManager.GetClaimsAsync(user);
                var claim = claims.FirstOrDefault(claim => claim.Type == claimType);
                var catalogId = this.UserState.CatalogContainer.Id;

                if (claim == null)
                {
                    var newValue = catalogId;
                    claim = new Claim(claimType, newValue);
                    await this.UserManager.AddClaimAsync(user, claim);
                }
                else if (!claim.Value.Split(';').Contains(catalogId))
                {
                    var newValue = claim != null
                        ? string.Join(';', claim.Value, catalogId)
                        : catalogId;
                    var newClaim = new Claim(claimType, newValue);
                    await this.UserManager.ReplaceClaimAsync(user, claim, newClaim);
                }
            }

            this.ToasterService.ShowSuccess(message: "Please log out and log in again for the changes to take effect.", icon: MatIconNames.Lock_open);
            this.OnIsOpenChanged(false);
        }

        private void Cancel()
        {
            this.OnIsOpenChanged(false);
        }

        private void OnIsOpenChanged(bool value)
        {
            this.IsOpen = value;
            this.IsOpenChanged.InvokeAsync(value);
        }

        #endregion
    }
}
