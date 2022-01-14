using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Writers
{
    [DataWriterFormatName("Comma-separated (*.csv)")]
    [DataWriterSelectOption("RowIndexFormat", "Row index format", "Excel", new string[] { "Excel", "Index", "Unix", "ISO 8601" }, new string[] { "Excel time", "Index-based", "Unix time" })]
    [DataWriterIntegerNumberInputOption("SignificantFigures", "Significant figures", 4, 0, int.MaxValue)]
    [ExtensionDescription("Writes data into CSV files.")]
    internal class Csv : IDataWriter
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

        public Csv()
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

        private DataWriterContext Context { get; set; } = null!;

        #endregion

        #region "Methods"

        public Task SetContextAsync(DataWriterContext context, CancellationToken cancellationToken)
        {
            this.Context = context;
            return Task.CompletedTask;
        }

        public Task OpenAsync(
            DateTime fileBegin,
            TimeSpan filePeriod,
            TimeSpan samplePeriod, 
            CatalogItem[] catalogItems, 
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                _lastFileBegin = fileBegin;
                _lastSamplePeriod = samplePeriod;
                _unixStart = (fileBegin - _unixEpoch).TotalSeconds;
                _excelStart = fileBegin.ToOADate();

                foreach (var catalogItemGroup in catalogItems.GroupBy(catalogItem => catalogItem.Catalog))
                {
                    cancellationToken.ThrowIfCancellationRequested();

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
#warning use .ToString("o") instead?
                        streamWriter.WriteLine($"# date_time={fileBegin.ToISO8601()};");
                        streamWriter.WriteLine($"# sample_period={samplePeriod.ToUnitString()};");
                        streamWriter.WriteLine($"# catalog_id={catalog.Id};");

                        if (catalog.Properties is not null)
                        {
                            foreach (var entry in catalog.Properties)
                            {
                                streamWriter.WriteLine($"# {entry.Key}={entry.Value};");
                            }
                        }

                        /* resource name */
                        var rowIndexFormat = this.Context.Configuration.GetValueOrDefault("RowIndexFormat", "Index");

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

                            case "ISO 8601":
                                streamWriter.Write("ISO 8601 time;");
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
                            if (catalogItem.Resource.Properties is not null && catalogItem.Resource.Properties.TryGetValue("Unit", out var unit))
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

                var requestGroups = requests
                    .GroupBy(request => request.CatalogItem.Catalog)
                    .ToList();

                var groupIndex = 0;
                var consumedLength = 0UL;

                foreach (var requestGroup in requestGroups)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var catalog = requestGroup.Key;
                    var writeRequests = requestGroup.ToArray();
                    var physicalId = catalog.Id.TrimStart('/').Replace('/', '_');
                    var root = Context.ResourceLocator.ToPath();
                    var filePath = Path.Combine(root, $"{physicalId}_{_lastFileBegin.ToISO8601()}_{_lastSamplePeriod.ToUnitString()}.csv");
                    var rowIndexFormat = Context.Configuration.GetValueOrDefault("RowIndexFormat", "Index");
                    var significantFigures = uint.Parse(Context.Configuration.GetValueOrDefault("SignificantFigures", "4"));

                    using var streamWriter = new StreamWriter(File.Open(filePath, FileMode.Append, FileAccess.Write), Encoding.UTF8);

                    var dateTimeStart = _lastFileBegin + fileOffset;

                    var unixStart = _unixStart + fileOffset.TotalSeconds;
                    var unixScalingFactor = (double)_lastSamplePeriod.Ticks / TimeSpan.FromSeconds(1).Ticks;

                    var excelStart = _excelStart + fileOffset.TotalDays;
                    var excelScalingFactor = (double)_lastSamplePeriod.Ticks / TimeSpan.FromDays(1).Ticks;

                    var rowLength = writeRequests.First().Data.Length;

                    for (int rowIndex = 0; rowIndex < rowLength; rowIndex++)
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

                            case "ISO 8601":
                                streamWriter.Write($"{(dateTimeStart + (rowIndex * _lastSamplePeriod)).ToString("o")};");
                                break;

                            default:
                                throw new NotSupportedException($"The row index format '{rowIndexFormat}' is not supported.");
                        }

                        for (int i = 0; i < writeRequests.Length; i++)
                        {
                            var value = writeRequests[i].Data.Span[rowIndex];
                            streamWriter.Write($"{string.Format(_nfi, $"{{0:G{significantFigures}}}", value)};");
                        }

                        streamWriter.WriteLine();

                        consumedLength += (ulong)writeRequests.Length;

                        if (consumedLength >= 10000)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            progress.Report((groupIndex + rowIndex / (double)rowLength) / requestGroups.Count);
                            consumedLength = 0;
                        }
                    }

                    groupIndex++;
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
