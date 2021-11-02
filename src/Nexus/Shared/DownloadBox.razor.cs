using MatBlazor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Nexus.Core;
using Nexus.Services;
using Nexus.Utilities;
using Nexus.ViewModels;
using System;
using System.Threading.Tasks;

namespace Nexus.Shared
{
    public partial class DownloadBox
    {
		#region Fields

		private bool _showCopyButton = true;

		#endregion

		#region Constructors

		public DownloadBox()
        {
			this.PropertyChanged = (sender, e) =>
			{
                switch (e.PropertyName)
                {
					case nameof(UserState.ExportParameters):
					case nameof(UserState.DateTimeBegin):
					case nameof(UserState.DateTimeEnd):
					case nameof(UserState.FilePeriod):
					case nameof(UserState.SamplePeriod):
					case nameof(UserState.SelectedRepresentations):

						this.InvokeAsync(this.StateHasChanged);
						break;

					default:
                        break;
                }
			};
		}

		#endregion

		#region Properties - Injected

		[Inject]
		private IJSRuntime JsRuntime { get; set; }

		[Inject]
		private ToasterService ToasterService { get; set; }

        #endregion

        #region Commands

		private void CopyPath(RepresentationViewModel representation)
        {
			this.JsRuntime.WriteToClipboard(representation.GetPath());
        }

        #endregion

        #region Methods

        private async Task DownloadAsync()
        {
            try
            {
				await this.UserState.DownloadAsync();
			}
            catch (Exception ex)
            {
				this.UserState.Logger.LogError(ex, "Download data failed.");
				this.ToasterService.ShowError(message: "Unable to download data.", icon: MatIconNames.Error_outline);
			}
        }

		private string GetDownloadLabel()
		{
			var byteCount = this.UserState.GetByteCount();

			if (byteCount > 0)
				return $"Download ({NexusUtilities.FormatByteCount(byteCount)})";
			else
				return $"Download";
		}

		#endregion
	}
}
