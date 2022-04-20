using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using SkiaSharp;

namespace Nexus.UI.Shared
{
    public record LineSeries(
        string Name,
        string Unit,
        DateTime Begin,
        TimeSpan SamplePeriod,
        double[] Data)
    {
        internal string Id { get; } = Guid.NewGuid().ToString();
        internal SKColor Color { get; set; }
        internal ZoomInfo ZoomInfo { get; set; }
    }

    internal record struct ZoomInfo(
        float IndexLeft,
        float IndexRight,
        Memory<double> Data,
        SKRect DataBox);

    public record struct Position(
        float X,
        float Y);

    internal record AxisInfo(
        string Unit,
        float OriginalMin,
        float OriginalMax)
    {
        public float Min { get; set; }
        public float Max { get; set; }
    };

    internal record TimeAxisConfig(

        /* The tick interval */
        TimeSpan TickInterval,

        /* Ticks where the TriggerPeriod changes will have a slow tick label attached */
        TriggerPeriod Trigger,

        /* The standard tick label format */
        string FastTickLabelFormat,

        /* The slow tick format */
        string SlowTickLabelFormat);

    internal enum TriggerPeriod
    {
        Second,
        Minute,
        Hour,
        Day,
        Month,
        Year
    }

    // https://stackoverflow.com/questions/63265941/blazor-3-1-nested-onmouseover-events
    [EventHandler("onmouseleave", typeof(MouseEventArgs), enableStopPropagation: true, enablePreventDefault: true)]
    public static class EventHandlers
    {
    }
}
