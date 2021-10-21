using Microsoft.AspNetCore.Components;
using Nexus.Core;
using Nexus.Services;
using System.Threading.Tasks;

namespace Nexus.Shared
{
    public partial class JobsSettings
    {
        #region Constructors

		public JobsSettings()
        {
			this.PropertyChanged = (sender, e) =>
			{
                if (e.PropertyName == "Jobs")
                {
                    this.InvokeAsync(() =>
                    {
                        this.StateHasChanged();
                    });
                }
            };
		}

        #endregion

        #region Properties

        [Inject]
		private JobService<ExportJob> ExportJobsService { get; set; }

		[Inject]
		private JobService<AggregationJob> AggregationJobsService { get; set; }

		#endregion

		#region Methods

		protected override Task OnParametersSetAsync()
		{
			this.ExportJobsService.PropertyChanged += this.PropertyChanged;
			this.AggregationJobsService.PropertyChanged += this.PropertyChanged;
			return base.OnParametersSetAsync();
		}

		#endregion

		#region IDisposable Support

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				this.ExportJobsService.PropertyChanged -= this.PropertyChanged;
				this.AggregationJobsService.PropertyChanged -= this.PropertyChanged;
				base.Dispose(disposing);
			}
		}

		#endregion
	}
}
