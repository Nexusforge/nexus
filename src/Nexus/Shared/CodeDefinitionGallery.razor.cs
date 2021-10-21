using Microsoft.AspNetCore.Components;
using Nexus.Core;
using Nexus.Services;
using Nexus.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nexus.Shared
{
    public partial class CodeDefinitionGallery
    {
        #region Properties

        [Inject]
        private AppState AppState { get; set; }

        [Inject]
        private IUserManagerWrapper UserManagerWrapper { get; set; }

        [Parameter]
        public bool IsOpen { get; set; }

        [Parameter]
        public EventCallback<bool> IsOpenChanged { get; set; }

        //[Parameter]
        private Action<CodeDefinitionViewModel> OnCodeDefinitionSelected { get; set; }

        private Dictionary<string, List<CodeDefinitionViewModel>> OwnerToCodeDefinitionsMap { get; set; }

        #endregion

        #region Methods

        public override Task SetParametersAsync(ParameterView parameters)
        {
            OnCodeDefinitionSelected = parameters.GetValueOrDefault<Action<CodeDefinitionViewModel>>(nameof(OnCodeDefinitionSelected));

            return base.SetParametersAsync(parameters);
        }

        protected override async Task OnParametersSetAsync()
        {
            var owners = this.AppState.FilterSettings.CodeDefinitions
                   .Where(current => current.IsEnabled)
                   .Select(current => current.Owner)
                   .Distinct()
                   .ToList();

            this.OwnerToCodeDefinitionsMap = new Dictionary<string, List<CodeDefinitionViewModel>>();

            foreach (var owner in owners)
            {
                this.OwnerToCodeDefinitionsMap[owner] = await this.GetCodeDefinitionsForOwnerAsync(owner);
            }

            await base.OnParametersSetAsync();
        }

        private async Task<List<CodeDefinitionViewModel>> GetCodeDefinitionsForOwnerAsync(string owner)
        {
            var user = await this.UserManagerWrapper.GetClaimsPrincipalAsync(owner);

            return this.AppState
                .FilterSettings
                .CodeDefinitions
                .Where(current => current.Owner == owner && current.IsEnabled)
                .OrderBy(current => current.CodeType)
                .Select(current => new CodeDefinitionViewModel(current))
                .ToList();
        }

        private void Accept(CodeDefinitionViewModel codeDefinition)
        {
            this.OnIsOpenChanged(false);
            this.OnCodeDefinitionSelected?.Invoke(codeDefinition);
        }

        private void Cancel()
        {
            this.OnIsOpenChanged(false);
        }

        private void OnIsOpenChanged(bool value)
        {
            this.IsOpen = value;
            this.IsOpenChanged.InvokeAsync(value);
        }

        #endregion
    }
}
