using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

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
            _lastFileBegin = begin;
            _unixStart = (decimal)(begin - _unixEpoch).TotalSeconds;
            _excelStart = (decimal)begin.ToOADate();

            foreach (var catalogEntry in catalogMap)
            {
                var catalog = catalogEntry.Key;
                var sampleRate = catalogEntry.Value;
                var physicalId = catalog.Id.TrimStart('/').Replace('/', '_');
                var filePath = Path.Combine(this.TargetFolder, $"{physicalId}_{begin.ToISO8601()}_{sampleRate.ToUnitString(underscore: true)}.csv");

                if (!File.Exists(filePath))
                {
                    using (StreamWriter streamWriter = new StreamWriter(File.Open(filePath, FileMode.Append, FileAccess.Write)))
                    {
                        // comment
                        streamWriter.WriteLine($"# format_version: 1;");
                        streamWriter.WriteLine($"# system_name: Nexus;");
                        streamWriter.WriteLine($"# date_time: { begin.ToISO8601() };");
                        streamWriter.WriteLine($"# sample_period: {sampleRate.ToUnitString()};");

                        streamWriter.WriteLine($"# catalog_id={catalog.Id};");

                        foreach (var entry in catalog.Metadata)
                        {
                            streamWriter.WriteLine($"# {entry.Key}={entry.Value};");
                        }

                        var datasets = catalog.Channels.SelectMany(catalog => catalog.Datasets);

                        /* channel name */
                        var rowIndexFormat = Enum.Parse<CsvRowIndexFormat>(this.Configuration["RowIndexFormat"]);

                        switch (rowIndexFormat)
                        {
                            case CsvRowIndexFormat.Index:
                                streamWriter.Write("index;");
                                break;

                            case CsvRowIndexFormat.Unix:
                                streamWriter.Write("Unix time;");
                                break;

                            case CsvRowIndexFormat.Excel:
                                streamWriter.Write("Excel time;");
                                break;

                            default:
                                throw new NotSupportedException($"The row index format '{rowIndexFormat}' is not supported.");
                        }

                        foreach (var dataset in datasets)
                        {
                            streamWriter.Write($"{ dataset.Channel.Name };");
                        }

                        streamWriter.WriteLine();

                        /* dataset name */
                        streamWriter.Write("-;");

                        foreach (var dataset in datasets)
                        {
                            streamWriter.Write($"{ dataset.Id };");
                        }

                        streamWriter.WriteLine();

                        /* unit */
                        streamWriter.Write("-;");

                        foreach (var dataset in datasets)
                        {
                            streamWriter.Write($"{ dataset.Channel.Unit };");
                        }

                        streamWriter.WriteLine();
                    }
                }
            }
        }

        public void Write(ulong fileOffset, ulong bufferOffset, ulong length, CatalogWriteInfo writeInfoGroup)
        {
            //var catalogDescription = this.DataWriterContext.CatalogDescription;
            //var dataFilePath = Path.Combine(this.DataWriterContext.DataDirectoryPath, $"{catalogDescription.PrimaryGroupName}_{catalogDescription.SecondaryGroupName}_{catalogDescription.CatalogName}_V{catalogDescription.Version }_{_lastFileBegin.ToString("yyyy-MM-ddTHH-mm-ss")}Z_{contextGroup.SampleRate.SamplesPerDay}_samples_per_day.csv");

            //if (length <= 0)
            //    throw new Exception(ErrorMessage.CsvWriter_SampleRateTooLow);

            //var simpleBuffers = contextGroup.ChannelContextSet.Select(channelContext => channelContext.Buffer.ToSimpleBuffer()).ToList();

            //using (StreamWriter streamWriter = new StreamWriter(File.Open(dataFilePath, FileMode.Append, FileAccess.Write)))
            //{
            //    for (ulong rowIndex = 0; rowIndex < length; rowIndex++)
            //    {
            //        switch (_settings.RowIndexFormat)
            //        {
            //            case CsvRowIndexFormat.Index:
            //                streamWriter.Write($"{string.Format(_nfi, "{0:N0}", fileOffset + rowIndex)};");
            //                break;
            //            case CsvRowIndexFormat.Unix:
            //                streamWriter.Write($"{string.Format(_nfi, "{0:N5}", _unixStart + ((fileOffset + rowIndex) / contextGroup.SampleRate.SamplesPerSecond))};");
            //                break;
            //            case CsvRowIndexFormat.Excel:
            //                streamWriter.Write($"{string.Format(_nfi, "{0:N9}", _excelStart + ((fileOffset + rowIndex) / (decimal)contextGroup.SampleRate.SamplesPerDay))};");
            //                break;
            //            default:
            //                throw new NotSupportedException($"The row index format '{_settings.RowIndexFormat}' is not supported.");
            //        }

            //        for (int i = 0; i < simpleBuffers.Count; i++)
            //        {
            //            var value = simpleBuffers[i].Buffer[(int)(bufferOffset + rowIndex)];
            //            streamWriter.Write($"{string.Format(_nfi, $"{{0:G{_settings.SignificantFigures}}}", value)};");
            //        }

            //        streamWriter.WriteLine();
            //    }
            //}
        }

        #endregion
    }
}
