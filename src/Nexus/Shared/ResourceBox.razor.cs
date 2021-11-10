using MatBlazor;
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
    public partial class ResourceBox
    {
		#region Fields

		private string _searchIcon = MatIconNames.Search;

		#endregion

		#region Constructors

		public ResourceBox()
		{
			this.PropertyChanged = (sender, e) =>
			{
				if (e.PropertyName == nameof(UserState.SearchString))
				{
					_ = this.InvokeAsync(() =>
					{
                        _searchIcon = string.IsNullOrWhiteSpace(this.UserState.SearchString) ? MatIconNames.Search : MatIconNames.Close;
						this.StateHasChanged();
					});
				}
				else if (e.PropertyName == nameof(UserState.CatalogContainer))
				{
					_ = this.InvokeAsync(() =>
					{
						this.GroupPage = 0;
						this.StateHasChanged();
					});
				}
				else if (e.PropertyName == nameof(UserState.GroupedResourcesEntry))
				{
					this.ResourcePage = 0;
				}
				else if (e.PropertyName == nameof(AppState.CatalogState) ||
						(e.PropertyName == nameof(AppState.IsCatalogStateUpdating) && !this.AppState.IsCatalogStateUpdating))
				{
					_ = this.InvokeAsync(this.StateHasChanged);
				}
			};
		}

		#endregion

		#region Properties

		[Inject]
		private IDatabaseManager DatabaseManager { get; set; }

		[Inject]
		public AuthenticationStateProvider AuthenticationStateProvider { get; set; }

		[Inject]
		public ToasterService ToasterService { get; set; }

		[Inject]
		public UserManager<IdentityUser> UserManager { get; set; }

		public int GroupPageSize { get; set; } = 15;

		public int GroupPage { get; set; } = 0;

		public int ResourcePageSize { get; set; } = 9;

		public int ResourcePage { get; set; } = 0;

		#endregion

		#region Methods

		private async Task AcceptLicenseAsync()
		{
			var authenticationState = await this.AuthenticationStateProvider.GetAuthenticationStateAsync();
			var principal = authenticationState.User;
			var claimType = Claims.CAN_ACCESS_CATALOG;

			if (principal.Identity.IsAuthenticated)
			{
				var user = await this.UserManager.GetUserAsync(principal);
				var claims = await this.UserManager.GetClaimsAsync(user);
				var claim = claims.FirstOrDefault(claim => claim.Type == claimType);
				var projectId = this.UserState.CatalogContainer.Id;

				if (claim == null)
				{
					var newValue = projectId;
					claim = new Claim(claimType, newValue);
					await this.UserManager.AddClaimAsync(user, claim);
				}
				else if (!claim.Value.Split(';').Contains(projectId))
				{
					var newValue = claim is not null
						? string.Join(';', claim.Value, projectId)
						: projectId;
					var newClaim = new Claim(claimType, newValue);
					await this.UserManager.ReplaceClaimAsync(user, claim, newClaim);
				}
			}

			this.ToasterService.ShowSuccess(message: "Please log out and log in again for the changes to take effect.", icon: MatIconNames.Lock_open);
		}

		#endregion
	}
}
