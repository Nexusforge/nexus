using Microsoft.Extensions.Logging;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Nexus.Extension.Csv
{
    public class CsvWriter : IDataWriter
    {
        #region "Fields"

        private CsvSettings _settings;

        private decimal _unixStart;
        private decimal _excelStart;
        private DateTime _unixEpoch;
        private DateTime _excelEpoch;
        private DateTime _lastFileStartDateTime;
        private NumberFormatInfo _nfi;

        #endregion

        #region "Constructors"

        public CsvWriter(CsvSettings settings, ILogger logger) : base(settings, logger)
        {
            _settings = settings;

            _unixEpoch = new DateTime(1970, 01, 01);
            _excelEpoch = new DateTime(1900, 01, 01);

            _nfi = new NumberFormatInfo()
            {
                NumberDecimalSeparator = ".",
                NumberGroupSeparator = string.Empty
            };
        }
        
        #endregion

        #region "Methods"

        protected override void OnPrepareFile(DateTime startDateTime, List<ChannelContextGroup> channelContextGroupSet)
        {
            _lastFileStartDateTime = startDateTime;
            _unixStart = (decimal)(startDateTime - _unixEpoch).TotalSeconds;
            _excelStart = (decimal)startDateTime.ToOADate();

            foreach (var contextGroup in channelContextGroupSet)
            {
                var dataFilePath = Path.Combine(this.DataWriterContext.DataDirectoryPath, $"{this.DataWriterContext.CatalogDescription.PrimaryGroupName}_{this.DataWriterContext.CatalogDescription.SecondaryGroupName}_{this.DataWriterContext.CatalogDescription.CatalogName}_V{this.DataWriterContext.CatalogDescription.Version}_{startDateTime.ToString("yyyy-MM-ddTHH-mm-ss")}Z_{contextGroup.SampleRate.SamplesPerDay}_samples_per_day.csv");

                if (!File.Exists(dataFilePath))
                {
                    using (StreamWriter streamWriter = new StreamWriter(File.Open(dataFilePath, FileMode.Append, FileAccess.Write)))
                    {
                        // comment
                        streamWriter.WriteLine($"# format_version: { this.FormatVersion };");
                        streamWriter.WriteLine($"# system_name: { this.DataWriterContext.SystemName };");
                        streamWriter.WriteLine($"# date_time: { startDateTime.ToISO8601() };");
                        streamWriter.WriteLine($"# samples_per_day: { contextGroup.SampleRate.SamplesPerDay };");

                        foreach (var customMetadataEntry in this.DataWriterContext.CustomMetadataEntrySet.Where(customMetadataEntry => customMetadataEntry.CustomMetadataEntryLevel == CustomMetadataEntryLevel.File))
                        {
                            streamWriter.WriteLine($"# { customMetadataEntry.Key }: { customMetadataEntry.Value };");
                        }

                        streamWriter.WriteLine($"# catalog_first_level: { this.DataWriterContext.CatalogDescription.PrimaryGroupName };");
                        streamWriter.WriteLine($"# catalog_second_level: { this.DataWriterContext.CatalogDescription.SecondaryGroupName };");
                        streamWriter.WriteLine($"# catalog_name: { this.DataWriterContext.CatalogDescription.CatalogName };");
                        streamWriter.WriteLine($"# catalog_version: { this.DataWriterContext.CatalogDescription.Version };");

                        foreach (var customMetadataEntry in this.DataWriterContext.CustomMetadataEntrySet.Where(customMetadataEntry => customMetadataEntry.CustomMetadataEntryLevel == CustomMetadataEntryLevel.Catalog))
                        {
                            streamWriter.WriteLine($"# { customMetadataEntry.Key }: { customMetadataEntry.Value };");
                        }

                        /* channel name */
                        switch (_settings.RowIndexFormat)
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
                                throw new NotSupportedException($"The row index format '{_settings.RowIndexFormat}' is not supported.");
                        }

                        foreach (var channelContext in contextGroup.ChannelContextSet)
                        {
                            streamWriter.Write($"{ channelContext.ChannelDescription.ChannelName };");
                        }

                        streamWriter.WriteLine();

                        /* dataset name */
                        streamWriter.Write("-;");

                        foreach (var channelContext in contextGroup.ChannelContextSet)
                        {
                            streamWriter.Write($"{ channelContext.ChannelDescription.DatasetName };");
                        }

                        streamWriter.WriteLine();

                        /* unit */
                        streamWriter.Write("-;");

                        foreach (var channelContext in contextGroup.ChannelContextSet)
                        {
                            streamWriter.Write($"{ channelContext.ChannelDescription.Unit };");
                        }

                        streamWriter.WriteLine();
                    }
                }
            }
        }

        protected override void OnWrite(ChannelContextGroup contextGroup, ulong fileOffset, ulong bufferOffset, ulong length)
        {
            var catalogDescription = this.DataWriterContext.CatalogDescription;
            var dataFilePath = Path.Combine(this.DataWriterContext.DataDirectoryPath, $"{catalogDescription.PrimaryGroupName}_{catalogDescription.SecondaryGroupName}_{catalogDescription.CatalogName}_V{catalogDescription.Version }_{_lastFileStartDateTime.ToString("yyyy-MM-ddTHH-mm-ss")}Z_{contextGroup.SampleRate.SamplesPerDay}_samples_per_day.csv");

            if (length <= 0)
                throw new Exception(ErrorMessage.CsvWriter_SampleRateTooLow);

            var simpleBuffers = contextGroup.ChannelContextSet.Select(channelContext => channelContext.Buffer.ToSimpleBuffer()).ToList();

            using (StreamWriter streamWriter = new StreamWriter(File.Open(dataFilePath, FileMode.Append, FileAccess.Write)))
            {
                for (ulong rowIndex = 0; rowIndex < length; rowIndex++)
                {
                    switch (_settings.RowIndexFormat)
                    {
                        case CsvRowIndexFormat.Index:
                            streamWriter.Write($"{string.Format(_nfi, "{0:N0}", fileOffset + rowIndex)};");
                            break;
                        case CsvRowIndexFormat.Unix:
                            streamWriter.Write($"{string.Format(_nfi, "{0:N5}", _unixStart + ((fileOffset + rowIndex) / contextGroup.SampleRate.SamplesPerSecond))};");
                            break;
                        case CsvRowIndexFormat.Excel:
                            streamWriter.Write($"{string.Format(_nfi, "{0:N9}", _excelStart + ((fileOffset + rowIndex) / (decimal)contextGroup.SampleRate.SamplesPerDay))};");
                            break;
                        default:
                            throw new NotSupportedException($"The row index format '{_settings.RowIndexFormat}' is not supported.");
                    }

                    for (int i = 0; i < simpleBuffers.Count; i++)
                    {
                        var value = simpleBuffers[i].Buffer[(int)(bufferOffset + rowIndex)];
                        streamWriter.Write($"{string.Format(_nfi, $"{{0:G{_settings.SignificantFigures}}}", value)};");
                    }

                    streamWriter.WriteLine();
                }
            }
        }

        #endregion
    }
}
