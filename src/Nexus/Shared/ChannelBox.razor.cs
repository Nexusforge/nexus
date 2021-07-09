﻿using MatBlazor;

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
					this.InvokeAsync(() =>
					{
                        _searchIcon = string.IsNullOrWhiteSpace(this.UserState.SearchString) ? MatIconNames.Search : MatIconNames.Close;
						this.StateHasChanged();
					});
				}
				else if (e.PropertyName == nameof(UserState.CatalogContainer))
				{
					this.InvokeAsync(() =>
					{
						this.GroupPage = 0;
						this.StateHasChanged();
					});
				}
				else if (e.PropertyName == nameof(UserState.GroupedResourcesEntry))
				{
					this.ResourcePage = 0;
				}
				else if (e.PropertyName == nameof(AppState.IsDatabaseInitialized) ||
						(e.PropertyName == nameof(AppState.IsDatabaseUpdating) && !this.AppState.IsDatabaseUpdating))
				{
					this.InvokeAsync(this.StateHasChanged);
				}
			};
		}

		#endregion

		#region Properties

		public bool LicenseDialogIsOpen { get; set; }

		public int GroupPageSize { get; set; } = 15;

		public int GroupPage { get; set; } = 0;

		public int ResourcePageSize { get; set; } = 9;

		public int ResourcePage { get; set; } = 0;

		#endregion

		#region Methods

		private void OpenLicenseDialog()
		{
			this.LicenseDialogIsOpen = true;
		}

		#endregion
	}
}
