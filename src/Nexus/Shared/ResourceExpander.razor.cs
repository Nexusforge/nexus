using Microsoft.AspNetCore.Components;
using Nexus.Core;
using Nexus.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Nexus.Shared
{
    public partial class ResourceExpander : IDisposable
    {
        #region Fields

        private bool _showAdditionalInfo;
        private IEnumerable<RepresentationViewModel> _filteredRepresentations;

        #endregion

        #region Properties

        [Inject]
        private UserState UserState { get; set; }

        //[Parameter]
        private ResourceViewModel Resource { get; set; }

        #endregion

        #region Methods

        public void Dispose()
        {
            this.UserState.PropertyChanged -= this.OnUserStatePropertyChanged;
        }

        // Workaround to avoid making "Resource" property public (type ResourceViewModel should stay internal)
        public override Task SetParametersAsync(ParameterView parameters)
        {
            this.Resource = parameters.GetValueOrDefault<ResourceViewModel>(nameof(this.Resource));
            this.UserState.PropertyChanged += this.OnUserStatePropertyChanged;

            this.UpdateFilteredRepresentations();
            this.StateHasChanged();

            return Task.CompletedTask;
        }

        private void UpdateFilteredRepresentations()
        {
            _filteredRepresentations = this.Resource.Representations
                .Where(representation => representation.SamplePeriod == this.UserState.SamplePeriod)
                .ToList();
        }

        private IEnumerable<TimeSpan> GetSamplePeriods()
        {
            return this.Resource.Representations
                .Select(representation => representation.SamplePeriod)
                .Distinct()
                .Where(samplePeriod => samplePeriod != this.UserState.SamplePeriod);
        }

        #endregion

        #region Callbacks

        private void OnUserStatePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UserState.SamplePeriod))
            {
                this.InvokeAsync(() =>
                {
                    this.UpdateFilteredRepresentations();
                    this.StateHasChanged();
                });
            }

            else if (e.PropertyName == nameof(UserState.IsEditEnabled))
            {
                this.InvokeAsync(() => { this.StateHasChanged(); });
            }
        }

        #endregion
    }
}
