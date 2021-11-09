using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nexus.Core
{
    internal static class JsRuntimeExtensions
    {
        #region Methods

        public static ValueTask WriteToClipboard(this IJSRuntime jsRuntime, string text)
        {
            return jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", text);
        }

        public static ValueTask BlobSaveAs(this IJSRuntime jsRuntime, string fileName, byte[] data)
        {
            return jsRuntime.InvokeVoidAsync("BlobSaveAs", fileName, Convert.ToBase64String(data));
        }

        public static ValueTask FileSaveAs(this IJSRuntime jsRuntime, string fileName, string href)
        {
            return jsRuntime.InvokeVoidAsync("FileSaveAs", fileName, href);
        }

        public static ValueTask<int> GetBrowserTimeZoneOffset(this IJSRuntime jsRuntime, DateTime value)
        {
            return jsRuntime.InvokeAsync<int>("GetBrowserTimeZoneOffset", value);
        }

        public static ValueTask UpdateChartAsync(this IJSRuntime jsRuntime, UserState userState, List<ChartEntry> chartEntries, DateTime begin, DateTime end, int count, double dt, bool beginAtZero)
        {
            var userStateRef = DotNetObjectReference.Create(userState);
            return jsRuntime.InvokeVoidAsync("UpdateChart", userStateRef, chartEntries, begin, end, count, dt, beginAtZero);
        }

        #endregion
    }
}
