﻿using MatBlazor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Nexus.Core;
using Nexus.Services;
using Nexus.ViewModels;
using Nexus.Core;
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
				if (e.PropertyName == nameof(UserState.ExportParameters))
				{
					this.InvokeAsync(this.StateHasChanged);
				}
				else if (e.PropertyName == nameof(UserState.DateTimeBegin))
				{
					this.InvokeAsync(this.StateHasChanged);
				}
				else if (e.PropertyName == nameof(UserState.DateTimeEnd))
				{
					this.InvokeAsync(this.StateHasChanged);
				}
				else if (e.PropertyName == nameof(UserState.FileGranularity))
				{
					this.InvokeAsync(this.StateHasChanged);
				}
				else if (e.PropertyName == nameof(UserState.SelectedDatasets))
				{
					this.InvokeAsync(this.StateHasChanged);
				}
			};
		}

		#endregion

		#region Properties - Injected

		[Inject]
		public IJSRuntime JsRuntime { get; set; }

		[Inject]
		public ToasterService ToasterService { get; set; }

        #endregion

        #region Commands

		private void CopyPath(DatasetViewModel dataset)
        {
			this.JsRuntime.WriteToClipboard($"{dataset.Parent.Parent.Id}/{dataset.Parent.Name}/{dataset.Model.Id}");
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
				this.UserState.Logger.LogError(ex.GetFullMessage());
				this.ToasterService.ShowError(message: "Unable to download data.", icon: MatIconNames.Error_outline);
			}
        }

		private string GetDownloadLabel()
		{
			var byteCount = this.UserState.GetByteCount();

			if (byteCount > 0)
				return $"Download ({Utilities.FormatByteCount(byteCount)})";
			else
				return $"Download";
		}

		#endregion
	}
}
