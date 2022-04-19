using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Nexus.UI.Services;
using SkiaSharp;
using SkiaSharp.Views.Blazor;
using System.Globalization;

namespace Nexus.UI.Shared
{
    public record LineSeries(
        string Name,
        string Unit,
        DateTime Begin,
        TimeSpan SamplePeriod,
        double[] Data)
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public SKColor Color { get; set; }
    }

    internal record struct RelativePosition(
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

    public partial class Chart
    {
        #region Fields

        private Dictionary<AxisInfo, LineSeries[]> _axesMap = null!;

        /* Common */
        private const float TICK_SIZE = 10;

        /* Y-Axis */
        private const float Y_PADDING_TOP = 20;
        private const float Y_PADDING_Bottom = 40;
        private const float Y_UNIT_OFFSET = 30;
        private const float TICK_MARGIN_LEFT = 5;

        private const float AXIS_MARGIN_RIGHT = 5;
        private const float HALF_LINE_HEIGHT = 3.5f;

        private int[] _factors = new int[] { 2, 5, 10, 20, 50 };

        /* Time-Axis */
        private const float TIME_AXIS_MARGIN_TOP = 15;
        private const float TIME_FAST_LABEL_OFFSET = 15;
        private TimeAxisConfig[] _timeAxisConfigs;

        /* Others */
        private SKColor[] _colors;

        #endregion

        #region Constructors

        public Chart()
        {
            _timeAxisConfigs = new[]
            {
                /* nanoseconds */
                new TimeAxisConfig(TimeSpan.FromSeconds(100e-9), TriggerPeriod.Second, ".fffffff", "yyyy-MM-dd HH:mm.ss"),

                /* microseconds */
                new TimeAxisConfig(TimeSpan.FromSeconds(1e-6), TriggerPeriod.Second, ".ffffff", "yyyy-MM-ddTHH:mm.ss"),
                new TimeAxisConfig(TimeSpan.FromSeconds(5e-6), TriggerPeriod.Second, ".ffffff", "yyyy-MM-ddTHH:mm.ss"),
                new TimeAxisConfig(TimeSpan.FromSeconds(10e-6), TriggerPeriod.Second, ".ffffff", "yyyy-MM-ddTHH:mm.ss"),
                new TimeAxisConfig(TimeSpan.FromSeconds(50e-6), TriggerPeriod.Second, ".ffffff", "yyyy-MM-ddTHH:mm.ss"),
                new TimeAxisConfig(TimeSpan.FromSeconds(100e-6), TriggerPeriod.Second, ".ffffff", "yyyy-MM-ddTHH:mm.ss"),

                /* milliseconds */
                new TimeAxisConfig(TimeSpan.FromSeconds(1e-3), TriggerPeriod.Minute, ":ss.fff", "yyyy-MM-ddTHH:mm"),
                new TimeAxisConfig(TimeSpan.FromSeconds(5e-3), TriggerPeriod.Minute, ":ss.fff", "yyyy-MM-ddTHH:mm"),
                new TimeAxisConfig(TimeSpan.FromSeconds(10e-3), TriggerPeriod.Minute, ":ss.fff", "yyyy-MM-ddTHH:mm"),
                new TimeAxisConfig(TimeSpan.FromSeconds(50e-3), TriggerPeriod.Minute, ":ss.fff", "yyyy-MM-ddTHH:mm"),
                new TimeAxisConfig(TimeSpan.FromSeconds(100e-3), TriggerPeriod.Minute, ":ss.fff", "yyyy-MM-ddTHH:mm"),

                /* seconds */
                new TimeAxisConfig(TimeSpan.FromSeconds(1), TriggerPeriod.Hour, "mm:ss", "yyyy-MM-ddTHH"),
                new TimeAxisConfig(TimeSpan.FromSeconds(5), TriggerPeriod.Hour, "mm:ss", "yyyy-MM-ddTHH"),
                new TimeAxisConfig(TimeSpan.FromSeconds(10), TriggerPeriod.Hour, "mm:ss", "yyyy-MM-ddTHH"),
                new TimeAxisConfig(TimeSpan.FromSeconds(30), TriggerPeriod.Hour, "mm:ss", "yyyy-MM-ddTHH"),

                /* minutes */
                new TimeAxisConfig(TimeSpan.FromMinutes(1), TriggerPeriod.Day, "HH:mm", "yyyy-MM-dd"),
                new TimeAxisConfig(TimeSpan.FromMinutes(5), TriggerPeriod.Day, "HH:mm", "yyyy-MM-dd"),
                new TimeAxisConfig(TimeSpan.FromMinutes(10), TriggerPeriod.Day, "HH:mm", "yyyy-MM-dd"),
                new TimeAxisConfig(TimeSpan.FromMinutes(30), TriggerPeriod.Day, "HH:mm", "yyyy-MM-dd"),

                /* hours */
                new TimeAxisConfig(TimeSpan.FromHours(1), TriggerPeriod.Day, "HH", "yyyy-MM-dd"),
                new TimeAxisConfig(TimeSpan.FromHours(3), TriggerPeriod.Day, "HH", "yyyy-MM-dd"),
                new TimeAxisConfig(TimeSpan.FromHours(6), TriggerPeriod.Day, "HH", "yyyy-MM-dd"),
                new TimeAxisConfig(TimeSpan.FromHours(12), TriggerPeriod.Day, "HH", "yyyy-MM-dd"),

                /* days */
                new TimeAxisConfig(TimeSpan.FromDays(1), TriggerPeriod.Month, "dd", "yyyy-MM"),
                new TimeAxisConfig(TimeSpan.FromDays(10), TriggerPeriod.Month, "dd", "yyyy-MM"),
                new TimeAxisConfig(TimeSpan.FromDays(30), TriggerPeriod.Month, "dd", "yyyy-MM"),
                new TimeAxisConfig(TimeSpan.FromDays(90), TriggerPeriod.Month, "dd", "yyyy-MM"),

                /* years */
                new TimeAxisConfig(TimeSpan.FromDays(365), TriggerPeriod.Year, "yyyy", ""),
            };

            _colors = new[] {
                new SKColor(0, 114, 189),
                new SKColor(217, 83, 25),
                new SKColor(237, 177, 32),
                new SKColor(126, 47, 142),
                new SKColor(119, 172, 48),
                new SKColor(77, 190, 238),
                new SKColor(162, 20, 47)
            };
        }

        #endregion

        #region Properties

        [Inject]
        public TypeFaceService TypeFaceService { get; set; }

        [Inject]
        public IJSInProcessRuntime JSInProcessRuntime { get; set; }

        [Parameter]
        public DateTime Begin { get; set; }

        [Parameter]
        public DateTime End { get; set; }

        [Parameter]
        public LineSeries[] LineSeries { get; set; }

        #endregion

        #region Events

        protected override void OnInitialized()
        {
            for (int i = 0; i < LineSeries.Length; i++)
            {
                var color = _colors[i % _colors.Length];
                LineSeries[i].Color = color;
            }

            _axesMap = LineSeries
                .GroupBy(lineSeries => lineSeries.Unit)
                .ToDictionary(group => GetAxisInfo(group.Key, group), group => group.ToArray());
        }

        private void OnMouseMove(MouseEventArgs e)
        {
            var relativePosition = JSInProcessRuntime.Invoke<RelativePosition>("nexus.chart.toRelative", e.ClientX, e.ClientY);

            JSInProcessRuntime.InvokeVoid("nexus.chart.translate", "crosshairs-x", 0, relativePosition.Y);
            JSInProcessRuntime.InvokeVoid("nexus.chart.translate", "crosshairs-y", relativePosition.X, 0);

            foreach (var axesEntry in _axesMap)
            {
                var axisInfo = axesEntry.Key;
                var lineSeries = axesEntry.Value;

                foreach (var series in lineSeries)
                {
                    var position = (End - Begin - series.SamplePeriod).Ticks * relativePosition.X / series.SamplePeriod.Ticks;
                    var snappedPosition = (int)Math.Round(position, MidpointRounding.AwayFromZero);

                    var x = snappedPosition / ((float)series.Data.Length - 1);
                    var y = ((float)series.Data[snappedPosition] - axisInfo.Min) / (axisInfo.Max - axisInfo.Min);

                    if (float.IsFinite(x) && float.IsFinite(y))
                        JSInProcessRuntime.InvokeVoid("nexus.chart.translate", $"pointer_{series.Id}", x, 1 - y);

                    else
                        JSInProcessRuntime.InvokeVoid("nexus.chart.hide", $"pointer_{series.Id}");
                }   
            }
        }

        private void OnMouseLeave(MouseEventArgs e)
        {
            JSInProcessRuntime.InvokeVoid("nexus.chart.hide", "crosshairs-x");
            JSInProcessRuntime.InvokeVoid("nexus.chart.hide", "crosshairs-y");

            foreach (var series in LineSeries)
            {
                JSInProcessRuntime.InvokeVoid("nexus.chart.hide", $"pointer_{series.Id}");
            }
        }

        #endregion

        #region Draw

        private void PaintSurface(SKPaintGLSurfaceEventArgs e)
        {
            /* sizes */
            var canvas = e.Surface.Canvas;
            var surfaceSize = e.BackendRenderTarget.Size;

            /* axes info */
            var yMin = Y_PADDING_TOP;
            var yMax = surfaceSize.Height - Y_PADDING_Bottom;
            var xMin = 0.0f;
            var xMax = surfaceSize.Width;

            /* y-axis */
            xMin = DrawYAxes(canvas, yMin, yMax, _axesMap);
            yMin += Y_UNIT_OFFSET;

            /* time-axis */
            DrawTimeAxis(canvas, xMin, yMin, xMax, yMax, Begin, End);

            /* series */
            var dataArea = new SKRect(xMin, yMin, xMax, yMax);

            using (var canvasRestore = new SKAutoCanvasRestore(canvas))
            {
                canvas.ClipRect(dataArea);
                DrawSeries(canvas, xMin, yMin, xMax, yMax, _axesMap);
            }

            /* overlay */
            JSInProcessRuntime.InvokeVoid(
                "nexus.chart.resize",
                dataArea.Left / surfaceSize.Width,
                dataArea.Top / surfaceSize.Height,
                dataArea.Right / surfaceSize.Width,
                dataArea.Bottom / surfaceSize.Height);
        }

        private AxisInfo GetAxisInfo(string unit, IEnumerable<LineSeries> lineDatasets)
        {
            var min = float.PositiveInfinity;
            var max = float.NegativeInfinity;

            foreach (var lineDataset in lineDatasets)
            {
                var data = lineDataset.Data;
                var length = data.Length;

                for (int i = 0; i < length; i++)
                {
                    var value = (float)data[i];

                    if (!double.IsNaN(value))
                    {
                        if (value < min)
                            min = value;

                        if (value > max)
                            max = value;
                    }
                }
            }

            if (min == double.PositiveInfinity || max == double.NegativeInfinity)
            {
                min = 0;
                max = 0;
            }

            return new AxisInfo(unit, min, max);
        }

        #endregion

        #region Y axis

        private float DrawYAxes(SKCanvas canvas, float yMin, float yMax, Dictionary<AxisInfo, LineSeries[]> axesMap)
        {
            using var axisLabelPaint = new SKPaint
            {
                Typeface = TypeFaceService.GetTTF("Courier New Bold"),
                IsAntialias = true
            };

            using var axisTickPaint = new SKPaint
            {
                Color = SKColors.LightGray,
                IsAntialias = true
            };

            var currentOffset = 0.0f;
            var canvasRange = yMax - yMin;
            var maxTickCount = (int)Math.Round(canvasRange / 50, MidpointRounding.AwayFromZero);
            var widthPerCharacter = axisLabelPaint.MeasureText(" ");

            foreach (var axesEntry in axesMap)
            {
                var axisInfo = axesEntry.Key;

                /* get ticks */
                var ticks = GetYTicks(axisInfo.OriginalMin, axisInfo.OriginalMax, maxTickCount);
                axisInfo.Min = ticks[0];
                axisInfo.Max = ticks[^1];

                var tickRange = axisInfo.Max - axisInfo.Min;

                /* get labels */
                var maxChars = axisInfo.Unit.Length;

                var labels = ticks
                    .Select(tick =>
                    {
                        var engineeringTick = ToEngineering(tick);
                        maxChars = Math.Max(maxChars, engineeringTick.Length);
                        return engineeringTick;
                    })
                    .ToArray();

                /* draw unit */
                var localUnitOffset = maxChars - axisInfo.Unit.Length;
                var xUnit = currentOffset + localUnitOffset * widthPerCharacter;
                var yUnit = yMin;
                canvas.DrawText(axisInfo.Unit, new SKPoint(xUnit, yUnit), axisLabelPaint);

                /* draw labels and ticks */
                var textWidth = widthPerCharacter * maxChars;

                for (int i = 0; i < ticks.Length; i++)
                {
                    var tick = ticks[i];
                    var label = labels[i];
                    var scaleFactor = (canvasRange - Y_UNIT_OFFSET) / tickRange;
                    var localLabelOffset = maxChars - label.Length;
                    var x = currentOffset + localLabelOffset * widthPerCharacter;
                    var y = yMax - (tick - axisInfo.Min) * scaleFactor;

                    canvas.DrawText(label, new SKPoint(x, y + HALF_LINE_HEIGHT), axisLabelPaint);

                    var tickX = currentOffset + textWidth + TICK_MARGIN_LEFT;
                    canvas.DrawLine(tickX, y, tickX + TICK_SIZE, y, axisTickPaint);
                }

                /* update offset */
                currentOffset += textWidth + TICK_MARGIN_LEFT + TICK_SIZE + AXIS_MARGIN_RIGHT;
            }

            return currentOffset - AXIS_MARGIN_RIGHT;
        }

        private float[] GetYTicks(double min, double max, int maxTickCount)
        {
            /* There are a minimum of 10 ticks and a maximum of 40 ticks with the following approach:
             * 
             *          Min   Max   Range   Significant   Min-Rounded   Max-Rounded  Start Step_1  ...   End  Count  
             *          
             *   Min      0    32      32             2             0           100      0     10  ...   100     10
             *          968  1000      32             2           900          1000    900    910  ...  1000     10
             * 
             *   Max     0     31      31             1             0            40      0      1  ...    40     40
             *         969   1000      31             1           960          1000    960    961  ...  1000     40
             */

            /* range and position of first significant digit */
            var originalRange = max - min;
            var significant = (int)Math.Round(Math.Log10(originalRange), MidpointRounding.AwayFromZero);

            /* get limits */
            var min_limit = RoundDown(min, decimalPlaces: -significant);
            var max_limit = RoundUp(max, decimalPlaces: -significant);
            var range = max_limit - min_limit;

            /* get tick step and count */
            var step = Math.Pow(10, significant - 1);
            var tickCount = (int)Math.Ceiling((range / step) + 1);

            /* ensure there are not too many ticks */
            if (tickCount > maxTickCount)
            {
                var originalStep = step;
                var originalTickCount = tickCount;

                for (int i = 0; i < _factors.Length; i++)
                {
                    var factor = _factors[i];

                    tickCount = (int)Math.Ceiling(originalTickCount / (double)factor);
                    step = originalStep * factor;

                    if (tickCount <= maxTickCount)
                        break;
                }
            }

            if (tickCount > maxTickCount)
                throw new Exception("Unable to calculate Y-axis ticks.");

            /* calculate actual steps */
            return Enumerable
                .Range(0, tickCount)
                .Select(tickNumber => (float)(min_limit + tickNumber * step))
                .ToArray();
        }

        #endregion

        #region Time axis

        private void DrawTimeAxis(SKCanvas canvas, float xMin, float yMin, float xMax, float yMax, DateTime begin, DateTime end)
        {
            using var axisLabelPaint = new SKPaint
            {
                Typeface = TypeFaceService.GetTTF("Courier New Bold"),
                TextAlign = SKTextAlign.Center,
                IsAntialias = true
            };

            using var axisTickPaint = new SKPaint
            {
                Color = SKColors.LightGray,
                IsAntialias = true
            };

            var canvasRange = xMax - xMin;
            var maxTickCount = (int)Math.Round(canvasRange / 130, MidpointRounding.AwayFromZero);
            var (config, ticks) = GetTimeTicks(begin, end, maxTickCount);
            var timeRange = (end - begin).Ticks;
            var scalingFactor = canvasRange / timeRange;
            var previousTick = DateTime.MinValue;

            foreach (var tick in ticks)
            {
                /* vertical line */
                var x = xMin + (tick - begin).Ticks * scalingFactor;
                canvas.DrawLine(x, yMin, x, yMax + TICK_SIZE, axisTickPaint);

                /* fast tick */
                var tickLabel = tick.ToString(config.FastTickLabelFormat, CultureInfo.InvariantCulture);
                canvas.DrawText(tickLabel, x, yMax + TICK_SIZE + TIME_AXIS_MARGIN_TOP, axisLabelPaint);

                /* slow tick */
                var addSlowTick = IsSlowTickRequired(previousTick, tick, config.Trigger);

                if (addSlowTick)
                {
                    var slowTickLabel = tick.ToString(config.SlowTickLabelFormat, CultureInfo.InvariantCulture);
                    canvas.DrawText(slowTickLabel, x, yMax + TICK_SIZE + TIME_AXIS_MARGIN_TOP + TIME_FAST_LABEL_OFFSET, axisLabelPaint);
                }

                /* */
                previousTick = tick;
            }
        }

        private (TimeAxisConfig, DateTime[]) GetTimeTicks(DateTime begin, DateTime end, int maxTickCount)
        {
            int GetTickCount(DateTime begin, DateTime end, TimeSpan tickInterval)
                => (int)Math.Ceiling((end - begin) / tickInterval);

            /* find TimeAxisConfig */
            TimeAxisConfig? selectedConfig = default;

            foreach (var config in _timeAxisConfigs)
            {
                var currentTickCount = GetTickCount(begin, end, config.TickInterval);

                if (currentTickCount <= maxTickCount)
                {
                    selectedConfig = config;
                    break;
                }
            }

            /* ensure TIME_MAX_TICK_COUNT is not exceeded */
            if (selectedConfig is null)
                selectedConfig = _timeAxisConfigs.Last();

            var tickInterval = selectedConfig.TickInterval;
            var tickCount = GetTickCount(begin, end, tickInterval);

            while (tickCount > maxTickCount)
            {
                tickInterval *= 2;
                tickCount = GetTickCount(begin, end, tickInterval);
            }

            /* calculate ticks */
            var firstTick = RoundUp(begin, tickInterval);

            var ticks = Enumerable
                .Range(0, tickCount)
                .Select(tickIndex => firstTick + tickIndex * tickInterval)
                .Where(tick => tick < end)
                .ToArray();

            return (selectedConfig, ticks);
        }

        private bool IsSlowTickRequired(DateTime previousTick, DateTime tick, TriggerPeriod trigger)
        {
            return trigger switch
            {
                TriggerPeriod.Second => previousTick.Date != tick.Date ||
                                        previousTick.Hour != tick.Hour ||
                                        previousTick.Minute != tick.Minute ||
                                        previousTick.Second != tick.Second
                                        ? true : false,

                TriggerPeriod.Minute => previousTick.Date != tick.Date ||
                                        previousTick.Hour != tick.Hour ||
                                        previousTick.Minute != tick.Minute
                                        ? true : false,

                TriggerPeriod.Hour => previousTick.Date != tick.Date ||
                                        previousTick.Hour != tick.Hour
                                        ? true : false,

                TriggerPeriod.Day => previousTick.Date != tick.Date
                                        ? true : false,

                TriggerPeriod.Month => previousTick.Year != tick.Year ||
                                        previousTick.Month != tick.Month
                                        ? true : false,

                TriggerPeriod.Year => previousTick.Year != tick.Year
                                        ? true : false,

                _ => throw new Exception("Unsupported trigger period."),
            };
        }

        #endregion

        #region Series

        private void DrawSeries(SKCanvas canvas, float xMin, float yMin, float xMax, float yMax, Dictionary<AxisInfo, LineSeries[]> axesMap)
        {
            var xCanvasRange = xMax - xMin;
            var yCanvasRange = yMax - yMin;
            var index = 0;

            /* for each axis */
            foreach (var axesEntry in axesMap)
            {
                var axisInfo = axesEntry.Key;
                var lineSeries = axesEntry.Value;

                /* get y scale factor */
                var tickRange = axisInfo.Max - axisInfo.Min;
                var yScaleFactor = yCanvasRange / tickRange;

                /* for each dataset */
                foreach (var series in lineSeries)
                {
                    var data = series.Data;
                    var firstTick = axisInfo.Min;

                    if (!data.Any())
                        continue;

                    /* get dx */
                    var timeRange = series.Data.Length * series.SamplePeriod;
                    var xScaleFactor = xCanvasRange / (timeRange.Ticks - series.SamplePeriod.Ticks);
                    var dx = series.SamplePeriod.Ticks * xScaleFactor;

                    /* draw */
                    DrawPath(canvas, axisInfo.Min, dx, xMin, yMax, yScaleFactor, data, series.Color);
                    //DrawCircles(canvas, axisInfo.Min, dx, xMin, yMax, yScaleFactor, data, series.Color); /* must be drawn after path because of circle fill color */
                    
                    index++;
                }
            }
        }

        private void DrawPath(
            SKCanvas canvas,
            float min,
            float dx,
            float xMin,
            float yMax,
            float yScaleFactor,
            double[] data,
            SKColor color)
        {
            using var strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = color,
                IsAntialias = true
            };

            using var fillPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = new SKColor(color.Red, color.Green, color.Blue, 0x19)
            };

            var consumed = 0;
            var firstTick = min;
            var length = data.Length;
            var zeroHeight = yMax - (0 - firstTick) * yScaleFactor;

            while (consumed < length)
            {
                /* create path */
                var stroke_path = new SKPath();
                var fill_path = new SKPath();
                var x = xMin + dx * consumed;

                stroke_path.MoveTo(x, zeroHeight);
                fill_path.MoveTo(x, zeroHeight);

                var lastValue = 0.0f;
                var currentX = x;

                for (int i = consumed; i < length; i++)
                {
                    var value = (float)data[i];

                    if (float.IsNaN(value))
                        break;

                    var y = yMax - (value - firstTick) * yScaleFactor;

                    stroke_path.LineTo(currentX, y);
                    fill_path.LineTo(currentX, y);

                    currentX += dx;
                    lastValue = value;
                    consumed++;
                }

                x = xMin + dx * consumed - dx;
                stroke_path.LineTo(x, zeroHeight);

                fill_path.LineTo(x, zeroHeight);
                fill_path.Close();

                /* draw path */
                canvas.DrawPath(stroke_path, strokePaint);
                canvas.DrawPath(fill_path, fillPaint);

                /* consume NaNs */
                for (int i = consumed; i < length; i++)
                {
                    var value = (float)data[i];

                    if (float.IsNaN(value))
                        consumed++;

                    else
                        break;
                }
            }
        }

        //private void DrawCircles(
        //    SKCanvas canvas, 
        //    float min,
        //    float dx,
        //    float xMin,
        //    float yMax,
        //    float yScaleFactor, 
        //    double[] data,
        //    SKColor color)
        //{
        //    using var circlePaintStroke = new SKPaint
        //    {
        //        Style = SKPaintStyle.Stroke,
        //        Color = color
        //    };

        //    using var circlePaintFill = new SKPaint
        //    {
        //        Style = SKPaintStyle.Fill,
        //        Color = SKColors.White
        //    };

        //    var consumed = 0;
        //    var firstTick = min;
        //    var length = data.Length;
        //    var lastValue = 0.0f;
        //    var currentX = xMin;

        //    for (int i = consumed; i < length; i++)
        //    {
        //        var value = (float)data[i];

        //        if (!float.IsNaN(value))
        //        {
        //            var y = yMax - (value - firstTick) * yScaleFactor;

        //            /* draw circle */
        //            canvas.DrawCircle(currentX, y, radius: 2, circlePaintFill);
        //            canvas.DrawCircle(currentX, y, radius: 2, circlePaintStroke);
        //        }

        //        currentX += dx;
        //        lastValue = value;
        //        consumed++;
        //    }
        //}

        #endregion

        #region Helpers

        private string ToEngineering(double value)
        {
            if (value == 0)
                return "0";

            if (Math.Abs(value) < 1000)
                return value.ToString("G4");

            var exponent = (int)Math.Floor(Math.Log10(Math.Abs(value)));

            var pattern = (exponent % 3) switch
            {
                +1 => "##.##e0",
                -2 => "##.##e0",
                +2 => "###.#e0",
                -1 => "###.#e0",
                _ => "#.###e0"
            };

            return value.ToString(pattern, CultureInfo.InvariantCulture);
        }

        private DateTime RoundUp(DateTime value, TimeSpan roundTo)
        {
            var modTicks = value.Ticks % roundTo.Ticks;

            var delta = modTicks == 0
                ? 0
                : roundTo.Ticks - modTicks;

            return new DateTime(value.Ticks + delta, value.Kind);
        }

        private double RoundDown(double number, int decimalPlaces)
        {
            return Math.Floor(number * Math.Pow(10, decimalPlaces)) / Math.Pow(10, decimalPlaces);
        }

        private double RoundUp(double number, int decimalPlaces)
        {
            return Math.Ceiling(number * Math.Pow(10, decimalPlaces)) / Math.Pow(10, decimalPlaces);
        }

        #endregion
    }
}
