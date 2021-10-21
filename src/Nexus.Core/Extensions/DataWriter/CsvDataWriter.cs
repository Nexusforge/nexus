using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    [ExtensionIdentification("Nexus.Builtin.Csv", "Nexus CSV Writer", "Writes data into CSV files.")]
    internal class CsvDataWriter : IDataWriter
    {
        #region "Fields"

        private double _unixStart;
        private double _excelStart;
        private DateTime _unixEpoch;
        private DateTime _lastFileBegin;
        private TimeSpan _lastSamplePeriod;
        private NumberFormatInfo _nfi;

        #endregion

        #region "Constructors"

        public CsvDataWriter()
        {
            _unixEpoch = new DateTime(1970, 01, 01);

            _nfi = new NumberFormatInfo()
            {
                NumberDecimalSeparator = ".",
                NumberGroupSeparator = string.Empty
            };
        }

        #endregion

        #region Properties

        private DataWriterContext Context { get; set; }

        #endregion

        #region "Methods"

        public Task SetContextAsync(DataWriterContext context, CancellationToken cancellationToken)
        {
            this.Context = context;
            return Task.CompletedTask;
        }

        public Task OpenAsync(DateTime fileBegin, TimeSpan samplePeriod, CatalogItem[] catalogItems, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                _lastFileBegin = fileBegin;
                _lastSamplePeriod = samplePeriod;
                _unixStart = (fileBegin - _unixEpoch).TotalSeconds;
                _excelStart = fileBegin.ToOADate();

                foreach (var catalogItemGroup in catalogItems.GroupBy(catalogItem => catalogItem.Catalog))
                {
                    var catalog = catalogItemGroup.Key;
                    var physicalId = catalog.Id.TrimStart('/').Replace('/', '_');
                    var root = this.Context.ResourceLocator.ToPath();
                    var filePath = Path.Combine(root, $"{physicalId}_{fileBegin.ToISO8601()}_{samplePeriod.ToUnitString()}.csv");

                    if (!File.Exists(filePath))
                    {
                        using var streamWriter = new StreamWriter(File.Open(filePath, FileMode.Append, FileAccess.Write), Encoding.UTF8);

                        // comment
                        streamWriter.WriteLine($"# format_version=1;");
                        streamWriter.WriteLine($"# system_name=Nexus;");
                        streamWriter.WriteLine($"# date_time={fileBegin.ToISO8601()};");
                        streamWriter.WriteLine($"# sample_period={samplePeriod.ToUnitString()};");
                        streamWriter.WriteLine($"# catalog_id={catalog.Id};");

                        foreach (var entry in catalog.Properties)
                        {
                            streamWriter.WriteLine($"# {entry.Key}={entry.Value};");
                        }

                        /* resource name */
                        var rowIndexFormat = this.Context.Configuration.TryGetValue("RowIndexFormat", out var value)
                            ? value
                            : "Index";

                        switch (rowIndexFormat)
                        {
                            case "Index":
                                streamWriter.Write("index;");
                                break;

                            case "Unix":
                                streamWriter.Write("Unix time;");
                                break;

                            case "Excel":
                                streamWriter.Write("Excel time;");
                                break;

                            default:
                                throw new NotSupportedException($"The row index format '{rowIndexFormat}' is not supported.");
                        }

                        foreach (var catalogItem in catalogItemGroup)
                        {
                            streamWriter.Write($"{catalogItem.Resource.Id};");
                        }

                        streamWriter.WriteLine();

                        /* representation name */
                        streamWriter.Write("-;");

                        foreach (var catalogItem in catalogItemGroup)
                        {
                            streamWriter.Write($"{catalogItem.Representation.Id};");
                        }

                        streamWriter.WriteLine();

                        /* unit */
                        streamWriter.Write("-;");

                        foreach (var catalogItem in catalogItemGroup)
                        {
                            if (catalogItem.Resource.Properties.TryGetValue("Unit", out var unit))
                                streamWriter.Write($"{unit};");
                            else
                                streamWriter.Write(";");
                        }

                        streamWriter.WriteLine();
                    }
                }
            });
        }

        public Task WriteAsync(TimeSpan fileOffset, WriteRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var offset = fileOffset.Ticks / _lastSamplePeriod.Ticks;

                foreach (var requestGroup in requests.GroupBy(request => request.CatalogItem.Catalog))
                {
                    var catalog = requestGroup.Key;
                    var requestGroupArray = requestGroup.ToArray();
                    var physicalId = catalog.Id.TrimStart('/').Replace('/', '_');
                    var root = this.Context.ResourceLocator.ToPath();
                    var filePath = Path.Combine(root, $"{physicalId}_{_lastFileBegin.ToISO8601()}_{_lastSamplePeriod.ToUnitString()}.csv");

                    var rowIndexFormat = this.Context.Configuration.TryGetValue("RowIndexFormat", out var value1)
                        ? value1
                        : "Index";

                    var significantFigures = uint.Parse(this.Context.Configuration.TryGetValue("SignificantFigures", out var value2)
                        ? value2
                        : "4");

                    using var streamWriter = new StreamWriter(File.Open(filePath, FileMode.Append, FileAccess.Write), Encoding.UTF8);

                    var unixStart = _unixStart + fileOffset.TotalSeconds;
                    var unixScalingFactor = (double)_lastSamplePeriod.Ticks / TimeSpan.FromSeconds(1).Ticks;

                    var excelStart = _excelStart + fileOffset.TotalDays;
                    var excelScalingFactor = (double)_lastSamplePeriod.Ticks / TimeSpan.FromDays(1).Ticks;

                    var length = requestGroupArray.First().Data.Length;

                    for (int rowIndex = 0; rowIndex < length; rowIndex++)
                    {
                        switch (rowIndexFormat)
                        {
                            case "Index":
                                streamWriter.Write($"{string.Format(_nfi, "{0:N0}", offset + rowIndex)};");
                                break;

                            case "Unix":
                                streamWriter.Write($"{string.Format(_nfi, "{0:N5}", unixStart + rowIndex * unixScalingFactor)};");
                                break;

                            case "Excel":
                                streamWriter.Write($"{string.Format(_nfi, "{0:N9}", excelStart + rowIndex * excelScalingFactor)};");
                                break;

                            default:
                                throw new NotSupportedException($"The row index format '{rowIndexFormat}' is not supported.");
                        }

                        for (int i = 0; i < requestGroupArray.Length; i++)
                        {
                            var value = requestGroupArray[i].Data.Span[rowIndex];
                            streamWriter.Write($"{string.Format(_nfi, $"{{0:G{significantFigures}}}", value)};");
                        }

                        streamWriter.WriteLine();
                    }
                }
            });
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        #endregion
    }
}
