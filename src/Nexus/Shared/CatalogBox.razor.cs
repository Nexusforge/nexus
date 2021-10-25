namespace Nexus.Shared
{
    public partial class CatalogBox
    {
		#region Constructors

		public CatalogBox()
		{
			this.PropertyChanged = (sender, e) =>
			{
				if (e.PropertyName == nameof(UserState.ClientState))
				{
					this.InvokeAsync(this.StateHasChanged);
				}
				else if (e.PropertyName == nameof(AppState.CatalogState) ||
				        (e.PropertyName == nameof(AppState.IsCatalogStateUpdating) && !this.AppState.IsCatalogStateUpdating))
				{
					this.InvokeAsync(this.StateHasChanged);
				}
			};
		}

		#endregion

		#region Properties

		public bool AttachmentsDialogIsOpen { get; set; }

		#endregion

		#region Methods

		private void OpenAttachmentsDialog()
		{
			this.AttachmentsDialogIsOpen = true;
		}

		#endregion
	}
}
