using Microsoft.AspNetCore.Components;
using Nexus.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Shared
{
    public partial class CodeDefinitionCatalogRequestModal
    {
        #region Records

        public record CatalogState()
        {
            public string Id { get; set; }
            public bool IsSelected { get; set; }
        }

        #endregion

        #region Properties

        [Inject]
        private UserState UserState { get; set; }

        [Parameter]
        public bool IsOpen { get; set; }

        [Parameter]
        public EventCallback<bool> IsOpenChanged { get; set; }

        [Parameter]
        public Action<List<string>> OnCatalogIdsSelected { get; set; }

        [Parameter]
        public List<string> SelectedCatalogIds { get; set; }

        private List<CatalogState> CatalogContainerStates { get; set; }

        #endregion

        #region Methods

        protected override void OnParametersSet()
        {
            var accessibleCatalogs = this.UserState.CatalogContainersInfo.Accessible;

            this.CatalogContainerStates = accessibleCatalogs.Select(catalogContainer =>
            {
                var isSelected = this.SelectedCatalogIds.Contains(catalogContainer.Id);
                return new CatalogState() 
                { 
                    Id = catalogContainer.Id,
                    IsSelected = isSelected
                };
            }).ToList();

            base.OnParametersSet();
        }

        private void OK()
        {
            this.OnIsOpenChanged(false);

            var newSelectedCatalogIds = this.CatalogContainerStates
                .Where(state => state.IsSelected)
                .Select(state => state.Id).ToList();

            this.OnCatalogIdsSelected?.Invoke(newSelectedCatalogIds);
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
