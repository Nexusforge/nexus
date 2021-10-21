using Microsoft.AspNetCore.Components;
using Nexus.Core;

namespace Nexus.Shared
{
    public partial class LogBookModal
    {
        #region Properties

        [Inject]
        private UserState UserState { get; set; }

        [Parameter]
        public bool IsOpen { get; set; }

        [Parameter]
        public EventCallback<bool> IsOpenChanged { get; set; }

        #endregion

        #region Methods

        private void OnIsOpenChanged(bool value)
        {
            this.IsOpen = value;
            this.IsOpenChanged.InvokeAsync(value);
        }

        #endregion
    }
}
