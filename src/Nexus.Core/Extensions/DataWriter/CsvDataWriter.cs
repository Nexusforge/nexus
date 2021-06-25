using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Nexus.Extensions
{
    public class CsvDataWriter : IDataWriter
    {
        #region "Fields"

        private decimal _unixStart;
        private decimal _excelStart;
        private DateTime _unixEpoch;
        private DateTime _excelEpoch;
        private DateTime _lastFileBegin;
        private NumberFormatInfo _nfi;

        #endregion

        #region "Constructors"

        public CsvDataWriter()
        {
            _unixEpoch = new DateTime(1970, 01, 01);
            _excelEpoch = new DateTime(1900, 01, 01);

            _nfi = new NumberFormatInfo()
            {
                NumberDecimalSeparator = ".",
                NumberGroupSeparator = string.Empty
            };
        }

        #endregion

        #region Properties

        public string TargetFolder { get; set; }

        public ILogger Logger { get; set; }

        public Dictionary<string, string> Configuration { get; set; }

        #endregion

        #region "Methods"

        public void Open(DateTime begin, Dictionary<Catalog, SampleRateContainer> catalogMap)
        {
            //_lastFileBegin = begin;
            //_unixStart = (decimal)(begin - _unixEpoch).TotalSeconds;
            //_excelStart = (decimal)begin.ToOADate();

            //foreach (var catalogEntry in catalogMap)
            //{
            //    var catalog = catalogEntry.Key;
            //    var sampleRate = catalogEntry.Value;
            //    var physicalId = catalog.Id.TrimStart('/').Replace('/', '_');
            //    var filePath = Path.Combine(this.TargetFolder, $"{physicalId}_{begin.ToISO8601()}_{sampleRate.ToUnitString(underscore: true)}.csv");

            //    if (!File.Exists(filePath))
            //    {
            //        using (StreamWriter streamWriter = new StreamWriter(File.Open(filePath, FileMode.Append, FileAccess.Write)))
            //        {
            //            // comment
            //            streamWriter.WriteLine($"# format_version=1;");
            //            streamWriter.WriteLine($"# system_name=Nexus;");
            //            streamWriter.WriteLine($"# date_time={begin.ToISO8601()};");
            //            streamWriter.WriteLine($"# sample_period={sampleRate.ToUnitString()};");
            //            streamWriter.WriteLine($"# catalog_id={catalog.Id};");

            //            foreach (var entry in catalog.Metadata)
            //            {
            //                streamWriter.WriteLine($"# {entry.Key}={entry.Value};");
            //            }

            //            var datasets = catalog.Channels.SelectMany(catalog => catalog.Datasets);

            //            /* channel name */
            //            var rowIndexFormat = this.Configuration.TryGetValue("RowIndexFormat", out var value)
            //                ? value 
            //                : "Index";

            //            switch (rowIndexFormat)
            //            {
            //                case "Index":
            //                    streamWriter.Write("index;");
            //                    break;

            //                case "Unix":
            //                    streamWriter.Write("Unix time;");
            //                    break;

            //                case "Excel":
            //                    streamWriter.Write("Excel time;");
            //                    break;

            //                default:
            //                    throw new NotSupportedException($"The row index format '{rowIndexFormat}' is not supported.");
            //            }

            //            foreach (var dataset in datasets)
            //            {
            //                streamWriter.Write($"{dataset.Channel.Name};");
            //            }

            //            streamWriter.WriteLine();

            //            /* dataset name */
            //            streamWriter.Write("-;");

            //            foreach (var dataset in datasets)
            //            {
            //                streamWriter.Write($"{dataset.Id};");
            //            }

            //            streamWriter.WriteLine();

            //            /* unit */
            //            streamWriter.Write("-;");

            //            foreach (var dataset in datasets)
            //            {
            //                streamWriter.Write($"{dataset.Channel.Unit};");
            //            }

            //            streamWriter.WriteLine();
            //        }
            //    }
            //}
        }

        public void Write(ulong fileOffset, ulong bufferOffset, ulong length, CatalogWriteInfo writeInfo)
        {
            //var sampleRate = writeInfo.SampleRate;
            //var physicalId = writeInfo.CatalogId.TrimStart('/').Replace('/', '_');
            //var filePath = Path.Combine(this.TargetFolder, $"{physicalId}_{_lastFileBegin.ToISO8601()}_{sampleRate.ToUnitString(underscore: true)}.csv");

            //var simpleBuffers = writeInfo.DatasetInfos..ChannelContextSet.Select(channelContext => channelContext.Buffer.ToSimpleBuffer()).ToList();

            //var rowIndexFormat = this.Configuration.TryGetValue("RowIndexFormat", out var value1)
            //    ? value1
            //    : "Index";

            //var significantFigures = uint.Parse(this.Configuration.TryGetValue("SignificantFigures", out var value2)
            //    ? value2
            //    : "4");

            //using (StreamWriter streamWriter = new StreamWriter(File.Open(filePath, FileMode.Append, FileAccess.Write)))
            //{
            //    for (ulong rowIndex = 0; rowIndex < length; rowIndex++)
            //    {
            //        switch (rowIndexFormat)
            //        {
            //            case "Index":
            //                streamWriter.Write($"{string.Format(_nfi, "{0:N0}", fileOffset + rowIndex)};");
            //                break;
            //            case "Unix":
            //                streamWriter.Write($"{string.Format(_nfi, "{0:N5}", _unixStart + ((fileOffset + rowIndex) / sampleRate.SamplesPerSecond))};");
            //                break;
            //            case "Excel":
            //                streamWriter.Write($"{string.Format(_nfi, "{0:N9}", _excelStart + ((fileOffset + rowIndex) / (decimal)sampleRate.SamplesPerDay))};");
            //                break;
            //            default:
            //                throw new NotSupportedException($"The row index format '{rowIndexFormat}' is not supported.");
            //        }

            //        for (int i = 0; i < simpleBuffers.Count; i++)
            //        {
            //            var value = simpleBuffers[i].Buffer[(int)(bufferOffset + rowIndex)];
            //            streamWriter.Write($"{string.Format(_nfi, $"{{0:G{significantFigures}}}", value)};");
            //        }

            //        streamWriter.WriteLine();
            //    }
            //}
        }

        #endregion
    }
}
