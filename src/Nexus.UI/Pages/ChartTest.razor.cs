using Nexus.UI.Services;
using SkiaSharp;
using SkiaSharp.Views.Blazor;

namespace Nexus.UI.Pages
{
    internal record LineDataset(
        string Name,
        string Unit,
        DateTime Begin,
        TimeSpan SamplePeriod,
        double[] Data);

    internal record AxisInfo(
        string Unit,
        double Min,
        double Max);

    public partial class ChartTest
    {
        private const float TICK_MARGIN_LEFT = 5;
        private const float TICK_WIDTH = 10;
        private const float AXIS_MARGIN_RIGHT = 5;
        private const int MAX_TICK_COUNT = 15;

        private int[] _factors = new int[] { 2, 5 };

        private List<LineDataset> _lineDatasets;

        public ChartTest()
        {
            var random = new Random();

            _lineDatasets = new List<LineDataset>()
            {
                new LineDataset(
                    "Temperature",
                    "°C",
                    new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                    TimeSpan.FromSeconds(1),
                    Enumerable.Range(0, 60).Select(value => random.NextDouble() * 10 - 5).ToArray()),

                new LineDataset(
                    "Wind speed",
                    "m/s",
                    new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                    TimeSpan.FromMilliseconds(10),
                    Enumerable.Range(0, 60*100).Select(value => random.NextDouble() * 30).ToArray()),

                new LineDataset(
                    "Pressure",
                    "mbar",
                    new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                    TimeSpan.FromSeconds(1),
                    Enumerable.Range(0, 60).Select(value => random.NextDouble() * 100 + 1000).ToArray())
            };
        }

        // https://github.com/mono/SkiaSharp/issues/1902

        private SKPaint _axisLabelPaint = new SKPaint 
        { 
            Typeface = TypeFaceService.GetTTF("Courier New Bold"),
            IsAntialias = true
        };

        private SKPaint _axisTickPaint = new SKPaint
        {
            Typeface = TypeFaceService.GetTTF("Courier New Bold"),
            Color = SKColors.LightGray,
            IsAntialias = true
        };

        private void PaintSurface(SKPaintGLSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var surfaceSize = e.BackendRenderTarget.Size;
            var axesMap = DrawYAxes(canvas, yMin: 40, yMax: Math.Max(40, surfaceSize.Height), _lineDatasets);
        }

        private Dictionary<AxisInfo, LineDataset[]> DrawYAxes(SKCanvas canvas, int yMin, int yMax, List<LineDataset> lineDatasets)
        {
            var axesMap = lineDatasets
                .GroupBy(lineDataset => lineDataset.Unit)
                .ToDictionary(group => GetAxisInfo(group.Key, group), group => group.ToArray());

            var currentOffset = 0.0f;
            var canvasRange = yMax - yMin;
            var widthPerCharacter = _axisLabelPaint.MeasureText(" ");

            foreach (var axesEntry in axesMap)
            {
                var axisInfo = axesEntry.Key;

                /* get ticks */
                var ticks = GetTicks(axisInfo.Min, axisInfo.Max);
                var tickMin = ticks[0];
                var tickMax = ticks[^1];
                var tickRange = tickMax - tickMin;

                /* get labels */
                var maxChars = 0;

                var labels = ticks
                    .Select(tick =>
                    {
                        var engineeringTick = $"{ConvertToEngineering(tick)} {axisInfo.Unit}";
                        maxChars = Math.Max(maxChars, engineeringTick.Length);
                        return engineeringTick;
                    })
                    .ToArray();

                /* draw labels and ticks */
                var textWidth = widthPerCharacter * maxChars;

                for (int i = 0; i < ticks.Length; i++)
                {
                    var tick = ticks[i];
                    var label = labels[i];
                    var scaleFactor = canvasRange / tickRange;
                    var localOffset = maxChars - label.Length;
                    var x = currentOffset + localOffset * widthPerCharacter;
                    var y = yMax - (tick - tickMin) * scaleFactor;

                    canvas.DrawText(label, new SKPoint(x, y), _axisLabelPaint);

                    var tickX = currentOffset + textWidth + TICK_MARGIN_LEFT;
                    canvas.DrawLine(tickX, y - 4, tickX + TICK_WIDTH, y - 4, _axisTickPaint);
                }

                /* update offset */
                currentOffset += textWidth + TICK_MARGIN_LEFT + TICK_WIDTH + AXIS_MARGIN_RIGHT;
            }

            return axesMap;
        }

        private float[] GetTicks(double min, double max)
        {
            /* range and position of first significant digit */
            var originalRange = max - min;
            var significants = (int)Math.Round(Math.Log10(originalRange));

            /* get limits */
            var min_limit = RoundDown(min, decimalPlaces: -significants);
            var max_limit = RoundUp(max, decimalPlaces: -significants);
            var range = max_limit - min_limit;

            /* get tick step and count */
            var step = Math.Pow(10, significants - 1);
            var tickCount = (int)Math.Ceiling((range / step) + 1);

            /* ensure there are not too many ticks */
            if (tickCount > MAX_TICK_COUNT)
            {
                var originalStep = step;
                var originalTickCount = tickCount;

                for (int i = 0; i < _factors.Length; i++)
                {
                    var factor = _factors[i];

                    tickCount = (int)Math.Ceiling(originalTickCount / (double)factor);
                    step = originalStep * factor;

                    if (tickCount <= MAX_TICK_COUNT)
                        break;
                }
            }

            if (tickCount > MAX_TICK_COUNT)
                throw new Exception("Unable to calculate Y-axis ticks.");

            /* calculate actual steps */
            return Enumerable
                .Range(0, tickCount)
                .Select(tickNumber => (float)(min_limit + tickNumber * step))
                .ToArray();
        }

        private AxisInfo GetAxisInfo(string unit, IEnumerable<LineDataset> lineDatasets)
        {
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;

            foreach (var lineDataset in lineDatasets)
            {
                var data = lineDataset.Data;
                var length = data.Length;

                for (int i = 0; i < length; i++)
                {
                    var value = data[i];

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

        public static string ConvertToEngineering(double value)
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

            return value.ToString(pattern);
        }

        private double RoundDown(double number, int decimalPlaces)
        {
            return Math.Floor(number * Math.Pow(10, decimalPlaces)) / Math.Pow(10, decimalPlaces);
        }

        private double RoundUp(double number, int decimalPlaces)
        {
            return Math.Ceiling(number * Math.Pow(10, decimalPlaces)) / Math.Pow(10, decimalPlaces);
        }
    }
}
