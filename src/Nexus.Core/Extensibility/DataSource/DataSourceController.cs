using Nexus.DataModel;
using Nexus.Infrastructure;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public class DataSourceController : IDisposable
    {
        #region Constructors

        public DataSourceController(IDataSource dataSource, DataSourceRegistration registration)
        {
            this.DataSource = dataSource;
            this.Registration = registration;
            this.Progress = new Progress<double>();
        }

        #endregion

        #region Properties

        public IDataSource DataSource { get; }

        public Progress<double> Progress { get; }

        public List<Catalog> Catalogs { get; private set; }

        internal DataSourceRegistration Registration { get; }

        #endregion

        #region Methods

        public async Task InitializeCatalogsAsync(CancellationToken cancellationToken)
        {
            var catalogs = await this.DataSource.GetCatalogsAsync(cancellationToken);

            foreach (var catalog in catalogs)
            {
                foreach (var channel in catalog.Channels)
                {
                    foreach (var dataset in channel.Datasets)
                    {
                        dataset.Registration = this.Registration;
                    }
                }
            }

            this.Catalogs = catalogs;
        }

        public void InitializeCatalogs(List<Catalog> catalogs)
        {
            this.Catalogs = catalogs;
        }

        public DataSourceDoubleStream ReadAsDoubleStream(
            Dataset dataset,
            DateTime begin,
            DateTime end,
            ulong upperBlockSize,
            CancellationToken cancellationToken)
        {
            var progressRecords = this.ReadAsync(new List<Dataset>() { dataset }, begin, end, upperBlockSize, TimeSpan.FromMinutes(1), cancellationToken);
            var samplesPerSecond = new SampleRateContainer(dataset.Id).SamplesPerSecond;
            var length = (long)Math.Round(samplesPerSecond * 
                (decimal)(end - begin).TotalSeconds, MidpointRounding.AwayFromZero) * 
                NexusUtilities.SizeOf(NexusDataType.FLOAT64);

            return new DataSourceDoubleStream(length, progressRecords);
        }

        public IAsyncEnumerable<DataSourceProgressRecord> ReadAsync(
            Dataset dataset,
            DateTime begin,
            DateTime end,
            ulong upperBlockSize,
            CancellationToken cancellationToken)
        {
#warning This is only a workaround. Should not be necessary when 1 Minute Base limit has been removed and all code is unit tested and rewritten.
            var fundamentalPeriod = (dataset.GetSampleRate().SamplesPerDay == 144)
                ? TimeSpan.FromMinutes(10)
                : TimeSpan.FromMinutes(1);

            return this.ReadAsync(new List<Dataset>() { dataset }, begin, end, upperBlockSize, fundamentalPeriod, cancellationToken);
        }

        public IAsyncEnumerable<DataSourceProgressRecord> ReadAsync(
            List<Dataset> datasets,
            DateTime begin,
            DateTime end,
            ulong upperBlockSize,
            CancellationToken cancellationToken)
        {
#warning This is only a workaround. Should not be necessary when 1 Minute Base limit has been removed and all code is unit tested and rewritten.

            var fundamentalPeriod = (datasets.First().GetSampleRate().SamplesPerDay == 144) 
                ? TimeSpan.FromMinutes(10) 
                : TimeSpan.FromMinutes(1);

            return this.ReadAsync(datasets, begin, end, upperBlockSize, fundamentalPeriod, cancellationToken);
        }

        public IAsyncEnumerable<DataSourceProgressRecord> ReadAsync(
            Dataset dataset,
            DateTime begin,
            DateTime end,
            ulong upperBlockSize,
            TimeSpan fundamentalPeriod,
            CancellationToken cancellationToken)
        {
            return this.ReadAsync(new List<Dataset>() { dataset }, begin, end, upperBlockSize, fundamentalPeriod, cancellationToken);
        }

        public IAsyncEnumerable<DataSourceProgressRecord> ReadAsync(
            List<Dataset> datasets,
            DateTime begin,
            DateTime end,
            ulong blockSizeLimit,
            TimeSpan fundamentalPeriod,
            CancellationToken cancellationToken)
        {
            var basePeriod = TimeSpan.FromMinutes(1);
            var period = end - begin;

            // sanity checks
            if (begin >= end)
                throw new ValidationException("The begin datetime must be less than the end datetime.");

            if (begin.Ticks % basePeriod.Ticks != 0)
                throw new ValidationException("The begin parameter must be a multiple of 1 minute.");

            if (end.Ticks % basePeriod.Ticks != 0)
                throw new ValidationException("The end parameter must be a multiple of 1 minute.");

            if (fundamentalPeriod.Ticks % basePeriod.Ticks != 0)
                throw new ValidationException("The fundamental period parameter must be a multiple of 1 minute.");

            if (period.Ticks % fundamentalPeriod.Ticks != 0)
                throw new ValidationException("The request period must be a multiple of the fundamental period.");

            if (blockSizeLimit == 0)
                throw new ValidationException("The upper block size must be > 0 bytes.");

            return this.InternalReadAsync(datasets, begin, end, blockSizeLimit, basePeriod, fundamentalPeriod, cancellationToken);
        }

        private async IAsyncEnumerable<DataSourceProgressRecord> InternalReadAsync(
            List<Dataset> datasets,
            DateTime begin,
            DateTime end,
            ulong blockSizeLimit,
            TimeSpan basePeriod,
            TimeSpan fundamentalPeriod,
            [EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            /* 
             * |....................|
             * |
             * |
             * |....................
             * |
             * |
             * |....................
             * |
             * |====================
             * |....................
             * |
             * |
             * |....................|
             * 
             * |     = base period (1 minute)
             *  ...  = fundamental period (e.g. 10 minutes)
             * |...| = begin & end markers
             *  ===  = block period
             */

            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (!datasets.Any() || begin == end)
                yield break;

            // calculation
            var minutesPerFP = fundamentalPeriod.Ticks / basePeriod.Ticks;

            var bytesPerFP = datasets.Sum(dataset =>
            {
                var bytesPerSample = NexusUtilities.SizeOf(dataset.DataType);
                var samplesPerMinute = dataset.GetSampleRate().SamplesPerSecond * 60;
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
                var datasetToResultMap = new Dictionary<Dataset, ReadResult>();
                var currentPeriod = TimeSpan.FromTicks(Math.Min(remainingPeriod.Ticks, maxPeriodPerRequest.Ticks));
                var currentEnd = currentBegin + currentPeriod;
                var index = 1;
                var count = datasets.Count;

                foreach (var dataset in datasets)
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

#warning add Try/Catch + Logging block at this point when implementing new IDataSource. Catching all ReadSingle Errors and return NaN should be easy then
#warning redesign
                    //#error ReadSingle returns ReadResult ... and that read result should be disposable using the IMemoryOwners
                    //#error every consumer that is done with the specific read result must dispose it!

                    var readResult = ExtensibilityUtilities.CreateReadResult(dataset, begin, end);
                    await this.DataSource.ReadSingleAsync(dataset, readResult, currentBegin, currentEnd, cancellationToken);
                    datasetToResultMap[dataset] = readResult;

                    // update progress
                    var localProgress = TimeSpan.FromTicks(currentPeriod.Ticks * index / count);
                    var currentProgress = (currentBegin + localProgress - begin).Ticks / (double)period.Ticks;

                    ((IProgress<double>)this.Progress).Report(currentProgress);
                    index++;
                }

                // notify about new data
                yield return new DataSourceProgressRecord(datasetToResultMap, currentBegin, currentEnd);

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
                DataSourceRegistration = this.Registration,
                Data = aggregatedData
                    .ToDictionary(entry => entry.Key, entry => entry.Value)
            };
        }

        public async Task<TimeRangeResult>
            GetTimeRangeAsync(string catalogId,  CancellationToken cancellationToken)
        {
            (var begin, var end) = await this.DataSource.GetTimeRangeAsync(catalogId, cancellationToken);

            return new TimeRangeResult() 
            {
                DataSourceRegistration = this.Registration,
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

        public async Task ReadSingleAsync(Dataset dataset, ReadResult result, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            await this.DataSource.ReadSingleAsync(dataset, result, begin, end, cancellationToken);
        }

        public virtual void Dispose()
        {
            //
        }

        #endregion
    }
}
