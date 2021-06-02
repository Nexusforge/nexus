using Microsoft.AspNetCore.Components;
using Nexus.ViewModels;
using System;
using System.Collections.Generic;

namespace Nexus.Shared
{
    public partial class CodeDefinitionSettingsBox
    {
        #region Properties

        [Parameter]
        public CodeDefinitionViewModel CodeDefinition { get; set; }

        [Parameter]
        public Action OnSave { get; set; }

        private bool CodeDefinitionCatalogRequestDialogIsOpen { get; set; }

        #endregion

        #region Commands

        private void SelectCatalogIds(List<string> catalogIds)
        {
            this.UserState.CodeDefinition.RequestedCatalogIds = catalogIds;
        }

        public void OpenCodeDefinitionCatalogRequestDialog()
        {
            this.CodeDefinitionCatalogRequestDialogIsOpen = true;
        }

        #endregion

        #region Methods

        private void HandleValidSubmit()
        {
            this.OnSave?.Invoke();
        }

        #endregion
    }
}
