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
        private List<DatasetViewModel> _filteredDatasets;

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
            this.UpdateFilteredDatasets();
            this.UserState.PropertyChanged += this.OnUserStatePropertyChanged;

            return base.OnParametersSetAsync();
        }

        private void OnClick()
        {
            this.IsExpanded = !this.IsExpanded;
        }

        private void UpdateFilteredDatasets()
        {
            if (string.IsNullOrWhiteSpace(this.UserState.SampleRate))
                _filteredDatasets = new List<DatasetViewModel>();
            else
                _filteredDatasets = this.Resource.Datasets.Where(dataset => dataset.Name.Contains(this.UserState.SampleRate)).ToList();
        }

        private List<string> GetSampleRates()
        {
            return this.Resource.Datasets
                .Select(dataset => dataset.Name.Split('_')[0])
                .Distinct()
                .Where(sampleRate => sampleRate != this.UserState.SampleRate)
                .OrderBy(x => x, new SampleRateStringComparer()).ToList();
        }

        #endregion

        #region Callbacks

        private void OnUserStatePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UserState.SampleRate))
            {
                this.InvokeAsync(() =>
                {
                    this.UpdateFilteredDatasets();
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
