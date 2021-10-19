using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Nexus.Core;

namespace Nexus.Shared
{
    public partial class NavMenu
    {
		[Inject]
		public IOptions<GeneralOptions> GeneralOptions { get; set; }

        public NavMenu()
        {
			this.PropertyChanged = (sender, e) =>
			{
				if (e.PropertyName == nameof(AppState.IsCatalogStateUpdating))
				{
					this.InvokeAsync(this.StateHasChanged);
				}
			};
		}
    }
}
