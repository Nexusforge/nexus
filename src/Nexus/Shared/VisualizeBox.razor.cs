using MatBlazor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Nexus.Core;
using Nexus.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nexus.Shared
{
    public partial class VisualizeBox
    {
		#region Fields

		private bool _showWarning;

		#endregion

		#region Constructors

		public VisualizeBox()
		{
			this.PropertyChanged = async (sender, e) =>
			{
				_showWarning = false;

				if (this.UserState.IsSizeLimitExceeded())
				{
					_showWarning = true;
					await this.InvokeAsync(this.StateHasChanged);
				}
				else if (!this.UserState.CanVisualize())
				{
					this.UserState.ClientState = ClientState.Normal;
				}
				else
				{
					if (e.PropertyName == nameof(UserState.ExportParameters))
					{
						await this.UpdateChartAsync();
					}
					else if (e.PropertyName == nameof(UserState.DateTimeBegin))
					{
						await this.UpdateChartAsync();
					}
					else if (e.PropertyName == nameof(UserState.DateTimeEnd))
					{
						await this.UpdateChartAsync();
					}
					else if (e.PropertyName == nameof(UserState.SelectedRepresentations))
					{
						await this.UpdateChartAsync();
					}
					else if (e.PropertyName == nameof(UserState.VisualizeBeginAtZero))
					{
						await this.UpdateChartAsync();
					}
					else if (e.PropertyName == nameof(UserState.VisualizeProgress))
					{
						await this.InvokeAsync(this.StateHasChanged);
					}
				}
			};
		}

		#endregion

		#region Properties

		[Inject]
		private ToasterService ToasterService { get; set; }

		[Inject]
		private IJSRuntime JsRuntime { get; set; }

		#endregion

		#region Methods

		protected override void OnInitialized()
		{
			if (this.UserState.IsSizeLimitExceeded())
				_showWarning = true;

			base.OnInitialized();
		}

		protected override async Task OnAfterRenderAsync(bool firstRender)
		{
			if (firstRender)
				await this.UpdateChartAsync();

			await base.OnAfterRenderAsync(firstRender);
		}

		private List<ChartEntry> BuildChartEntriesAsync()
		{
			var chartEntries = new List<ChartEntry>();

			foreach (var representation in this.UserState.SelectedRepresentations.ToList())
			{
				var name = representation.Resource.Id;
				var representationNameParts = representation.Id.Split('_');

				if (representationNameParts.Count() == 3)
					name += $" ({representationNameParts[2]})";

				var path = representation.GetPath();

				chartEntries.Add(new ChartEntry(name, path, representation.Resource.Unit));
			}

			return chartEntries;
		}

		private async Task UpdateChartAsync()
		{
            try
            {
                var chartEntries = this.BuildChartEntriesAsync();
                var begin = this.UserState.DateTimeBegin;
                var end = this.UserState.DateTimeEnd;
                var dt = this.UserState.SamplePeriod.TotalSeconds;
                var count = (int)((end - begin).Ticks / this.UserState.SamplePeriod.Ticks);

                await this.InvokeAsync(this.StateHasChanged);
                await this.JsRuntime.UpdateChartAsync(this.UserState, chartEntries, begin, end, count, dt, this.UserState.VisualizeBeginAtZero);
            }
            catch (UnauthorizedAccessException ex)
            {
                this.UserState.Logger.LogDebug(ex, "Unauthorized");
                this.ToasterService.ShowError(message: "Unauthorized", icon: MatIconNames.Lock);
                this.UserState.ClientState = ClientState.Normal;
            }
            catch (Exception ex)
            {
                this.UserState.Logger.LogError(ex, "Stream data failed");
                this.ToasterService.ShowError(message: "Unable to stream data", icon: MatIconNames.Error_outline);
                this.UserState.ClientState = ClientState.Normal;
            }
        }

		#endregion
	}
}
