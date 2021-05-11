﻿using Microsoft.AspNetCore.Components;
using Nexus.Core;
using System;
using System.IO;
using System.Text.Json;

namespace Nexus.Shared
{
    public partial class PresetsModal
    {
        #region Properties

        [Inject]
        public UserState UserState { get; set; }

        [Parameter]
        public bool IsOpen { get; set; }

        [Parameter]
        public EventCallback<bool> IsOpenChanged { get; set; }

        #endregion

        #region Methods

        private string GetFileName(string filePath)
        {
            return Path.GetFileName(filePath);
        }

        private void LoadPreset(string filePath)
        {
            var jsonString = File.ReadAllText(filePath);
            var exportParameters = JsonSerializer.Deserialize<ExportParameters>(jsonString);

            var timeSpan = exportParameters.End - exportParameters.Begin;
            var dt = exportParameters.End;
            var now = DateTime.UtcNow.AddDays(-1);

            exportParameters.End = new DateTime(now.Year, now.Month, now.Day, dt.Hour, dt.Minute, dt.Second, DateTimeKind.Utc);
            exportParameters.Begin = exportParameters.End - timeSpan;
            exportParameters = exportParameters.UpdateVersion();

            this.OnIsOpenChanged(false);
            this.UserState.SetExportParameters(exportParameters);
        }

        private void OnIsOpenChanged(bool value)
        {
            this.IsOpen = value;
            this.IsOpenChanged.InvokeAsync(value);
        }

        #endregion
    }
}
