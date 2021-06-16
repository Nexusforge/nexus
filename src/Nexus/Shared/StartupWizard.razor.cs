using Microsoft.AspNetCore.Components;
using Nexus.Core;

namespace Nexus.Shared
{
    public partial class StartupWizard
    {
        [Inject]
        public NexusOptions Options { get; set; }

        [Inject]
        public AppState AppState { get; set; }

        public string Error { get; set; }

        public void TryInitializeApp()
        {
            if (!this.AppState.TryInitializeApp(out var exception))
                this.Error = exception.GetFullMessage();
        }
    }
}
