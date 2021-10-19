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
        public UserState UserState { get; set; }

        [Parameter]
        public bool IsExpanded { get; set; }

        [Parameter]
        public ResourceViewModel Resource { get; set; }

        #endregion

        #region Methods

        public void Dispose()
        {
            this.UserState.PropertyChanged -= this.OnUserStatePropertyChanged;
        }

        protected override Task OnParametersSetAsync()
        {
            this.UpdateFilteredRepresentations();
            this.UserState.PropertyChanged += this.OnUserStatePropertyChanged;

            return base.OnParametersSetAsync();
        }

        private void OnClick()
        {
            this.IsExpanded = !this.IsExpanded;
        }

        private void UpdateFilteredRepresentations()
        {
            if (this.UserState.SamplePeriod == default)
                _filteredRepresentations = Enumerable.Empty<RepresentationViewModel>();

            else
                _filteredRepresentations = this.Resource.Representations
                    .Where(representation => representation.SamplePeriod == this.UserState.SamplePeriod)
                    .Select(representation => new RepresentationViewModel(this.Resource, representation));
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
