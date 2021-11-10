using Microsoft.AspNetCore.Components;
using Nexus.Services;
using Nexus.ViewModels;
using System;

namespace Nexus.Shared
{
    public partial class SettingsContentBox : IDisposable
    {
        #region Fields

        private EventHandler _handler;

        #endregion

        #region Properties

        [Inject]
        private SettingsViewModel Settings { get; set; }

        [Inject]
        private JobEditor JobEditor { get; set; }

        #endregion

        #region Methods

        protected override void OnParametersSet()
        {
            _handler = (sender, e) => this.InvokeAsync(() => this.StateHasChanged());
            this.JobEditor.Changed += _handler;

            base.OnParametersSet();
        }

        public void Dipose()
        {
            if (_handler is not null)
                this.JobEditor.Changed -= _handler;
        }

        #endregion

        #region Commands

        public void UpdateJobEditor()
        {
            this.JobEditor.UpdateAsync().Wait();
            this.InvokeAsync(() => this.StateHasChanged());
        }

        #endregion
    }
}
