using Nexus.DataModel;
using Nexus.Infrastructure;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public class DataSourceController : IDisposable
    {
        #region Constructors

        public DataSourceController(IDataSource dataSource, BackendSource backendSource)
        {
            this.DataSource = dataSource;
            this.BackendSource = backendSource;
            this.Progress = new Progress<double>();
        }

        #endregion

        #region Properties

        public IDataSource DataSource { get; }

        public Progress<double> Progress { get; }

        public List<Catalog> Catalogs { get; private set; }

        internal BackendSource BackendSource { get; }

        #endregion

        #region Methods

        public async Task InitializeAsync(DataSourceContext context, CancellationToken cancellationToken)
        {
            await this.DataSource.SetContextAsync(context, cancellationToken);

            if (context.Catalogs is null)
            {
                var catalogs = await this.DataSource.GetCatalogsAsync(cancellationToken);

                catalogs = catalogs
                    .Where(catalog => NexusUtilities.CheckCatalogNamingConvention(catalog.Id, out var _))
                    .ToList();

                foreach (var catalog in catalogs)
                {
                    foreach (var channel in catalog.Channels)
                    {
                        foreach (var dataset in channel.Datasets)
                        {
                            dataset.BackendSource = this.BackendSource;
                        }
                    }
                }

                this.Catalogs = catalogs;
            }
            else
            {
                this.Catalogs = context.Catalogs;
            }
        }

        public void SetCatalogs(List<Catalog> catalogs)
        {
            this.Catalogs = catalogs;
        }

        public DataSourceDoubleStream ReadAsDoubleStream(
            DatasetRecord datasetRecords,
            DateTime begin,
            DateTime end,
            ulong upperBlockSize,
            CancellationToken cancellationToken)
        {
            var progressRecords = this.ReadAsync(new List<DatasetRecord>() { datasetRecords }, begin, end, upperBlockSize, TimeSpan.FromMinutes(1), cancellationToken);
            var samplesPerSecond = new SampleRateContainer(datasetRecords.Dataset.Id).SamplesPerSecond;
            var length = (long)Math.Round(samplesPerSecond * 
                (decimal)(end - begin).TotalSeconds, MidpointRounding.AwayFromZero) * 
                NexusUtilities.SizeOf(NexusDataType.FLOAT64);

            return new DataSourceDoubleStream(length, progressRecords);
        }

        public IAsyncEnumerable<DataSourceProgressRecord> ReadAsync(
            DatasetRecord datasetRecord,
            DateTime begin,
            DateTime end,
            ulong upperBlockSize,
            CancellationToken cancellationToken)
        {
#warning This is only a workaround. Should not be necessary when 1 Minute Base limit has been removed and all code is unit tested and rewritten.
            var fundamentalPeriod = (datasetRecord.Dataset.GetSampleRate().SamplesPerDay == 144)
                ? TimeSpan.FromMinutes(10)
                : TimeSpan.FromMinutes(1);

            return this.ReadAsync(new List<DatasetRecord>() { datasetRecord }, begin, end, upperBlockSize, fundamentalPeriod, cancellationToken);
        }

        public IAsyncEnumerable<DataSourceProgressRecord> ReadAsync(
            List<DatasetRecord> datasetRecords,
            DateTime begin,
            DateTime end,
            ulong upperBlockSize,
            CancellationToken cancellationToken)
        {
#warning This is only a workaround. Should not be necessary when 1 Minute Base limit has been removed and all code is unit tested and rewritten.

            var fundamentalPeriod = (datasetRecords.First().Dataset.GetSampleRate().SamplesPerDay == 144) 
                ? TimeSpan.FromMinutes(10) 
                : TimeSpan.FromMinutes(1);

            return this.ReadAsync(datasetRecords, begin, end, upperBlockSize, fundamentalPeriod, cancellationToken);
        }

        public IAsyncEnumerable<DataSourceProgressRecord> ReadAsync(
            DatasetRecord datasetRecord,
            DateTime begin,
            DateTime end,
            ulong upperBlockSize,
            TimeSpan fundamentalPeriod,
            CancellationToken cancellationToken)
        {
            return this.ReadAsync(new List<DatasetRecord>() { datasetRecord }, begin, end, upperBlockSize, fundamentalPeriod, cancellationToken);
        }

        public IAsyncEnumerable<DataSourceProgressRecord> ReadAsync(
            List<DatasetRecord> datasetRecords,
            DateTime begin,
            DateTime end,
            ulong blockSizeLimit,
            TimeSpan fundamentalPeriod,
            CancellationToken cancellationToken)
        {
            var samplePeriod = new SampleRateContainer(datasetRecords.First().Dataset.Id).Period;

            // sanity checks
            if (begin >= end)
                throw new ValidationException("The begin datetime must be less than the end datetime.");

            if (begin.Ticks % samplePeriod.Ticks != 0)
                throw new ValidationException("The begin parameter must be a multiple of the sample period.");

            if (end.Ticks % samplePeriod.Ticks != 0)
                throw new ValidationException("The end parameter must be a multiple of the sample period.");

            if (blockSizeLimit == 0)
                throw new ValidationException("The upper block size must be > 0 bytes.");

            return this.InternalReadAsync(datasetRecords, begin, end, blockSizeLimit, samplePeriod, cancellationToken);
        }

        private async IAsyncEnumerable<DataSourceProgressRecord> InternalReadAsync(
            List<DatasetRecord> datasetRecords,
            DateTime begin,
            DateTime end,
            ulong blockSizeLimit,
            TimeSpan samplePeriod,
            [EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (!datasetRecords.Any() || begin == end)
                yield break;

            // calculation
#error Continue here
            var minutesPerFP = fundamentalPeriod.Ticks / basePeriod.Ticks;

            var bytesPerFP = datasetRecords.Sum(datasetRecord =>
            {
                var bytesPerSample = NexusUtilities.SizeOf(datasetRecord.Dataset.DataType);
                var samplesPerMinute = datasetRecord.Dataset.GetSampleRate().SamplesPerSecond * 60;
                var bytesPerFP = bytesPerSample * samplesPerMinute * minutesPerFP;

                return bytesPerFP;
            });

            var FPCountPerBlock = blockSizeLimit / bytesPerFP;
            var roundedFPCount = (long)Math.Floor(FPCountPerBlock);

            if (roundedFPCount < 1)
                throw new Exception("The block size limit is too small.");

            var maxPeriodPerRequest = TimeSpan.FromTicks(fundamentalPeriod.Ticks * roundedFPCount);

            // load data
            var period = end - begin;
            var currentBegin = begin;
            var remainingPeriod = end - currentBegin;

            while (remainingPeriod > TimeSpan.Zero)
            {
                var datasetRecordToResultMap = new Dictionary<DatasetRecord, ReadResult>();
                var currentPeriod = TimeSpan.FromTicks(Math.Min(remainingPeriod.Ticks, maxPeriodPerRequest.Ticks));
                var currentEnd = currentBegin + currentPeriod;
                var index = 1;
                var count = datasetRecords.Count;

                foreach (var datasetRecord in datasetRecords)
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

#warning add Try/Catch + Logging block at this point when implementing new IDataSource. Catching all ReadSingle Errors and return NaN should be easy then
#warning redesign
                    //#error ReadSingle returns ReadResult ... and that read result should be disposable using the IMemoryOwners
                    //#error every consumer that is done with the specific read result must dispose it!

                    var readResult = ExtensibilityUtilities.CreateReadResult(datasetRecord.Dataset, begin, end);
                    await this.DataSource.ReadSingleAsync(datasetRecord.GetPath(), readResult, currentBegin, currentEnd, cancellationToken);
                    datasetRecordToResultMap[datasetRecord] = readResult;

                    // update progress
                    var localProgress = TimeSpan.FromTicks(currentPeriod.Ticks * index / count);
                    var currentProgress = (currentBegin + localProgress - begin).Ticks / (double)period.Ticks;

                    ((IProgress<double>)this.Progress).Report(currentProgress);
                    index++;
                }

                // notify about new data
                yield return new DataSourceProgressRecord(datasetRecordToResultMap, currentBegin, currentEnd);

                // continue in time
                currentBegin += currentPeriod;
                remainingPeriod = end - currentBegin;
            }
        }

        public async Task<AvailabilityResult> 
            GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, AvailabilityGranularity granularity, CancellationToken cancellationToken)
        {
            var dateBegin = begin.Date;
            var dateEnd = end.Date;
            var aggregatedData = new ConcurrentDictionary<DateTime, double>();

            switch (granularity)
            {
                case AvailabilityGranularity.Day:

                    var totalDays = (int)(dateEnd - dateBegin).TotalDays;

                    var tasks = Enumerable.Range(0, totalDays).Select(async day =>
                    {
                        var date = dateBegin.AddDays(day);
                        var availability = await this.DataSource.GetAvailabilityAsync(catalogId, date, date.AddDays(1), cancellationToken);
                        aggregatedData.TryAdd(date, availability);
                    });

                    await Task.WhenAll(tasks);

                    break;

                case AvailabilityGranularity.Month:

                    var currentBegin = new DateTime(begin.Year, begin.Month, 1, 0, 0, 0, DateTimeKind.Utc);

                    while (currentBegin < end)
                    {
                        var availability = await this.DataSource.GetAvailabilityAsync(catalogId, currentBegin, currentBegin.AddMonths(1), cancellationToken);
                        aggregatedData.TryAdd(currentBegin, availability);
                        currentBegin = currentBegin.AddMonths(1);
                    }

                    break;

                default:
                    throw new NotSupportedException($"Availability granularity value '{granularity}' is not supported.");
            }

            return new AvailabilityResult()
            {
                BackendSource = this.BackendSource,
                Data = aggregatedData.ToDictionary(entry => entry.Key, entry => entry.Value)
            };
        }

        public async Task<TimeRangeResult>
            GetTimeRangeAsync(string catalogId,  CancellationToken cancellationToken)
        {
            (var begin, var end) = await this.DataSource.GetTimeRangeAsync(catalogId, cancellationToken);

            return new TimeRangeResult() 
            {
                BackendSource = this.BackendSource,
                Begin = begin, 
                End = end 
            };
        }

        public List<string> GetCatalogIds()
        {
            return this.Catalogs.Select(catalog => catalog.Id).ToList();
        }

        public bool TryGetCatalog(string catalogId, out Catalog catalogInfo)
        {
            catalogInfo = this.Catalogs.FirstOrDefault(catalog => catalog.Id == catalogId);
            return catalogInfo != null;
        }

        public Catalog GetCatalog(string catalogId)
        {
            return this.Catalogs.First(catalog => catalog.Id == catalogId);
        }

        public async Task<bool> IsDataOfDayAvailableAsync(string catalogId, DateTime day, CancellationToken cancellationToken)
        {
            return (await this.DataSource.GetAvailabilityAsync(catalogId, day, day.AddDays(1), cancellationToken)) > 0;
        }

        public async Task ReadSingleAsync(DatasetRecord datasetRecord, ReadResult result, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            await this.DataSource.ReadSingleAsync(datasetRecord.GetPath(), result, begin, end, cancellationToken);
        }

        public virtual void Dispose()
        {
            //
        }

        #endregion
    }
}
