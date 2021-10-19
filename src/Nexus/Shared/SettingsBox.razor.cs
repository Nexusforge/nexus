using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Nexus.Core;
using Nexus.Services;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Nexus.Shared
{
    public partial class SettingsBox
    {
        #region Constructors

        public SettingsBox()
        {
            this.PropertyChanged = (sender, e) =>
            {
                if (e.PropertyName == nameof(UserState.ExportParameters))
                {
                    this.InvokeAsync(this.StateHasChanged);
                }
                // for workaround
                else if (e.PropertyName == nameof(UserState.DateTimeBegin))
                {
                    this.InvokeAsync(this.StateHasChanged);
                }
                // for workaround
                else if (e.PropertyName == nameof(UserState.DateTimeEnd))
                {
                    this.InvokeAsync(this.StateHasChanged);
                }
                else if (e.PropertyName == nameof(UserState.SamplePeriod))
                {
                    this.InvokeAsync(this.StateHasChanged);
                }
                else if (e.PropertyName == nameof(UserState.SelectedRepresentations))
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

        [Inject]
        public IJSRuntime JsRuntime { get; set; }

        #endregion

        #region Methods

        private async Task OnSaveExportSettingsAsync()
        {
			var configuration = this.UserState.ExportParameters;
			var jsonString = JsonSerializer.Serialize(configuration, new JsonSerializerOptions() { WriteIndented = true });
			await this.JsRuntime.BlobSaveAs("export.json", Encoding.UTF8.GetBytes(jsonString));
		}

        private async Task OnLoadExportSettingsAsync(InputFileChangeEventArgs e)
        {
            var file = e.File;

            if (file != null)
            {
                using var utf8json = file.OpenReadStream();
                var exportParameters = await JsonSerializer.DeserializeAsync<ExportParameters>(utf8json);
                exportParameters = exportParameters.UpdateVersion();
                this.UserState.SetExportParameters(exportParameters);
            }
        }

		#endregion
	}
}
