using Nexus.UI.Shared;

namespace Nexus.UI.Pages
{
    public partial class ChartTest
    {
        private DateTime _begin;
        private DateTime _end;
        private LineSeries[] _lineSeries;

        public ChartTest()
        {
            _begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            _end = new DateTime(2020, 01, 01, 0, 1, 0, DateTimeKind.Utc);

            var random = new Random();

            _lineSeries = new LineSeries[]
            {
                new LineSeries(
                    "Wind speed",
                    "m/s",
                    _begin,
                    TimeSpan.FromMilliseconds(500),
                    Enumerable.Range(0, 60*2).Select(value => value / 4.0).ToArray()),

                new LineSeries(
                    "Temperature",
                    "°C",
                    _begin,
                    TimeSpan.FromSeconds(1),
                    Enumerable.Range(0, 60).Select(value => random.NextDouble() * 10 - 5).ToArray()),

                new LineSeries(
                    "Pressure",
                    "mbar",
                    _begin,
                    TimeSpan.FromSeconds(1),
                    Enumerable.Range(0, 60).Select(value => random.NextDouble() * 100 + 1000).ToArray())
            };

            _lineSeries[0].Data[0] = double.NaN;

            _lineSeries[0].Data[5] = double.NaN;
            _lineSeries[0].Data[6] = double.NaN;

            _lineSeries[0].Data[10] = double.NaN;
            _lineSeries[0].Data[11] = double.NaN;
            _lineSeries[0].Data[12] = double.NaN;

            _lineSeries[0].Data[15] = double.NaN;
            _lineSeries[0].Data[16] = double.NaN;
            _lineSeries[0].Data[17] = double.NaN;
            _lineSeries[0].Data[18] = double.NaN;
        }
    }
}
