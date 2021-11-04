using Nexus.DataModel;
using System;
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

		public DateTime Begin { get; private set; }

		public DateTime End { get; private set; }

		public ResourceCatalog Catalog { get; private set; }

		public bool AttachmentsDialogIsOpen { get; set; }

        #endregion

        #region Methods

        protected override async Task OnInitializedAsync()
        {
			this.Begin = await this.UserState.CatalogContainer.GetCatalogBeginAsync(CancellationToken.None);
			this.End = await this.UserState.CatalogContainer.GetCatalogEndAsync(CancellationToken.None);
			this.Catalog = await this.UserState.CatalogContainer.GetCatalogAsync(CancellationToken.None);
		}

        private void OpenAttachmentsDialog()
		{
			this.AttachmentsDialogIsOpen = true;
		}

		#endregion
	}
}
