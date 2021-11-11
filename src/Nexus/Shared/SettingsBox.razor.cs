using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Nexus.Core;
using Nexus.Services;
using Nexus.Utilities;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
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
                    _ = this.InvokeAsync(this.StateHasChanged);
                }
                // for workaround
                else if (e.PropertyName == nameof(UserState.DateTimeBegin))
                {
                    _ = this.InvokeAsync(this.StateHasChanged);
                }
                // for workaround
                else if (e.PropertyName == nameof(UserState.DateTimeEnd))
                {
                    _ = this.InvokeAsync(this.StateHasChanged);
                }
                else if (e.PropertyName == nameof(UserState.SamplePeriod))
                {
                    _ = this.InvokeAsync(this.StateHasChanged);
                }
                else if (e.PropertyName == nameof(UserState.SelectedRepresentations))
                {
                    _ = this.InvokeAsync(this.StateHasChanged);
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
        private IJSRuntime JsRuntime { get; set; }

        #endregion

        #region Methods

        private async Task OnSaveExportSettingsAsync()
        {
			var configuration = this.UserState.ExportParameters;
			var jsonString = JsonSerializerHelper.SerializeIntended(configuration);
			await this.JsRuntime.BlobSaveAs("export.json", Encoding.UTF8.GetBytes(jsonString));
		}

        private async Task OnLoadExportSettingsAsync(InputFileChangeEventArgs e)
        {
            var file = e.File;

            if (file is not null)
            {
                using var streamReader = new StreamReader(file.OpenReadStream());
                var jsonString = await streamReader.ReadToEndAsync();
                var exportParameters = JsonSerializer.Deserialize<ExportParameters>(jsonString);
                exportParameters = exportParameters.UpdateVersion();
                await this.UserState.SetExportParametersAsync(exportParameters, CancellationToken.None);
            }
        }

		#endregion
	}
}
