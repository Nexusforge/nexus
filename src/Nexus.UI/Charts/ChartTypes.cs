﻿using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using SkiaSharp;

namespace Nexus.UI.Charts
{
    public record AvailabilityData(
        DateTime Begin,
        DateTime End,
        TimeSpan Step,
        IList<double> Data
    );

    public record LineSeriesData(
        DateTime Begin,
        DateTime End,
        IList<LineSeries> Series
    );

    public record LineSeries(
        string Name,
        string Unit,
        TimeSpan SamplePeriod,
        double[] Data)
    {
        public bool Show { get; set; } = true;
        internal string Id { get; } = Guid.NewGuid().ToString();
        internal SKColor Color { get; set; }
    }

    internal record struct ZoomInfo(
        Memory<double> Data,
        SKRect DataBox);

    internal record struct Position(
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

        /* The standard tick label format */
        string FastTickLabelFormat,

        /* Ticks where the TriggerPeriod changes will have a slow tick label attached */
        TriggerPeriod SlowTickTrigger,

        /* The slow tick format (row 1) */
        string? SlowTickLabelFormat1,

        /* The slow tick format (row 2) */
        string? SlowTickLabelFormat2,
        
        /* The cursor label format*/
        string CursorLabelFormat);

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
