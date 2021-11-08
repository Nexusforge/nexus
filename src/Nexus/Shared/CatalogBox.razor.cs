using Nexus.Services;
using System.Threading;
using System.Threading.Tasks;

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

		private CatalogInfo CatalogInfo { get; set; }

		private bool AttachmentsDialogIsOpen { get; set; }

        #endregion

        #region Methods

        protected override async Task OnInitializedAsync()
        {
			this.CatalogInfo = await this.UserState.CatalogContainer.GetCatalogInfoAsync(CancellationToken.None);
		}

        private void OpenAttachmentsDialog()
		{
			this.AttachmentsDialogIsOpen = true;
		}

		#endregion
	}
}
