using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// https://www.w3.org/TR/tabular-data-primer/
// https://www.w3.org/ns/csvw
// https://www.w3.org/TR/2015/REC-tabular-data-model-20151217

namespace Nexus.Writers
{
    [DataWriterFormatName("CSV on the Web (*.csv)")]
    [DataWriterSelectOption("RowIndexFormat", "Row index format", "Excel", new string[] { "Excel", "Index", "Unix", "ISO 8601" }, new string[] { "Excel time", "Index-based", "Unix time" })]
    [DataWriterIntegerNumberInputOption("SignificantFigures", "Significant figures", 4, 0, int.MaxValue)]
    [ExtensionDescription("Writes data into CSV on the Web files.")]
    internal class Csvw : IDataWriter
    {
        #region "Fields"

        private double _unixStart;
        private double _excelStart;
        private DateTime _unixEpoch;
        private DateTime _lastFileBegin;
        private TimeSpan _lastSamplePeriod;
        private NumberFormatInfo _nfi;
        private JsonSerializerOptions _options; 

        #endregion

        #region "Constructors"

        public Csvw()
        {
            _unixEpoch = new DateTime(1970, 01, 01);

            _nfi = new NumberFormatInfo()
            {
                NumberDecimalSeparator = ".",
                NumberGroupSeparator = string.Empty
            };

            _options = new JsonSerializerOptions()
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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

        public async Task OpenAsync(
            DateTime fileBegin,
            TimeSpan filePeriod,
            TimeSpan samplePeriod, 
            CatalogItem[] catalogItems, 
            CancellationToken cancellationToken)
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
                var fileName = $"{physicalId}_{fileBegin.ToISO8601()}_{samplePeriod.ToUnitString()}";
                var filePath = Path.Combine(root, fileName);

                /* resource name */
                var rowIndexFormat = this.Context.Configuration.GetValueOrDefault("RowIndexFormat", "Index");

                var timestampColumnTitle = rowIndexFormat switch
                {
                    "Index" => "index",
                    "Unix" => "Unix time",
                    "Excel" => "Excel time",
                    "ISO 8601" => "ISO 8601 time",
                    _ => throw new NotSupportedException($"The row index format '{rowIndexFormat}' is not supported.")
                };

                /* metadata */
                var metaDataFilePath = $"{filePath}.csv-metadata.json";

                if (!File.Exists(metaDataFilePath))
                {
                    var timestampColumn = new Column(
                        Titles: timestampColumnTitle,
                        DataType: "dateTime",
                        Properties: default);

                    var columns = new[] { timestampColumn }.Concat(catalogItemGroup.Select(catalogItem =>
                    {
                        string? unit = default;

                        catalogItem.Resource.Properties?
                            .TryGetValue(DataModelExtensions.Unit, out unit);
                        
                        return new Column(
                            Titles: $"{catalogItem.Resource.Id} ({catalogItem.Representation.Id})", 
                            DataType: "number",
                            Properties: catalogItem.Resource.Properties);
                    })).ToArray();

                    var dialect = new Dialect("#", SkipRows: 3, Header: true, HeaderRowCount: 3);
                    var tableSchema = new TableSchema(columns, catalog.Properties);
                    var metadata = new CsvMetadata("http://www.w3.org/ns/csvw", $"{fileName}.csv", dialect, tableSchema);

                    var jsonString = JsonSerializer.Serialize(metadata, _options);
                    File.WriteAllText(metaDataFilePath, jsonString);
                }

                /* data */
                var dataFilePath = $"{filePath}.csv";

                if (!File.Exists(dataFilePath))
                {
                    var stringBuilder = new StringBuilder();

                    using var streamWriter = new StreamWriter(File.Open(dataFilePath, FileMode.Append, FileAccess.Write), Encoding.UTF8);

                    /* header values */
#warning use .ToString("o") instead?
                    await streamWriter.WriteLineAsync($"# date_time={fileBegin.ToISO8601()}");
                    await streamWriter.WriteLineAsync($"# sample_period={samplePeriod.ToUnitString()}");
                    await streamWriter.WriteLineAsync($"# catalog_id={catalog.Id}");

                    /* resource id */
                    stringBuilder.Append($"{timestampColumnTitle},");

                    foreach (var catalogItem in catalogItemGroup)
                    {
                        stringBuilder.Append($"{catalogItem.Resource.Id},");
                    }

                    stringBuilder.Remove(stringBuilder.Length - 1, 1);
                    stringBuilder.AppendLine();

                    /* representation id */
                    stringBuilder.Append("-;");

                    foreach (var catalogItem in catalogItemGroup)
                    {
                        stringBuilder.Append($"{catalogItem.Representation.Id},");
                    }

                    stringBuilder.Remove(stringBuilder.Length - 1, 1);
                    stringBuilder.AppendLine();

                    /* unit */
                    stringBuilder.Append("-,");

                    foreach (var catalogItem in catalogItemGroup)
                    {
                        if (catalogItem.Resource.Properties is not null && catalogItem.Resource.Properties.TryGetValue("Unit", out var unit))
                            stringBuilder.Append($"{unit},");

                        else
                            stringBuilder.Append(",");
                    }

                    stringBuilder.Remove(stringBuilder.Length - 1, 1);
                    stringBuilder.AppendLine();

                    await streamWriter.WriteAsync(stringBuilder);
                }
            }
        }

        public async Task WriteAsync(TimeSpan fileOffset, WriteRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var offset = fileOffset.Ticks / _lastSamplePeriod.Ticks;

            var requestGroups = requests
                .GroupBy(request => request.CatalogItem.Catalog)
                .ToList();

            var groupIndex = 0;
            var consumedLength = 0UL;
            var stringBuilder = new StringBuilder();

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
                    stringBuilder.Clear();

                    switch (rowIndexFormat)
                    {
                        case "Index":
                            stringBuilder.Append($"{string.Format(_nfi, "{0:N0}", offset + rowIndex)},");
                            break;

                        case "Unix":
                            stringBuilder.Append($"{string.Format(_nfi, "{0:N5}", unixStart + rowIndex * unixScalingFactor)},");
                            break;

                        case "Excel":
                            stringBuilder.Append($"{string.Format(_nfi, "{0:N9}", excelStart + rowIndex * excelScalingFactor)},");
                            break;

                        case "ISO 8601":
                            stringBuilder.Append($"{(dateTimeStart + (rowIndex * _lastSamplePeriod)).ToString("o")},");
                            break;

                        default:
                            throw new NotSupportedException($"The row index format '{rowIndexFormat}' is not supported.");
                    }

                    for (int i = 0; i < writeRequests.Length; i++)
                    {
                        var value = writeRequests[i].Data.Span[rowIndex];
                        stringBuilder.Append($"{string.Format(_nfi, $"{{0:G{significantFigures}}}", value)},");
                    }

                    stringBuilder.Remove(stringBuilder.Length - 1, 1);
                    stringBuilder.AppendLine();

                    await streamWriter.WriteAsync(stringBuilder);

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
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        #endregion
    }
}
