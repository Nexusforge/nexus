using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.DataModel;
using Nexus.Infrastructure;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public abstract class DataReaderExtensionBase : IDisposable
    {
        #region Constructors

        public DataReaderExtensionBase(DataSourceRegistration registration, ILogger logger)
        {
            this.Registration = registration;
            this.Logger = logger;
            this.Progress = new Progress<double>();
        }

        #endregion

        #region Properties

        public string RootPath => "";

        public ILogger Logger { get; }

        public Progress<double> Progress { get; }

        public List<Catalog> Catalogs { get; private set; }

        public Dictionary<string, string> OptionalParameters { get; set; }

        internal DataSourceRegistration Registration { get; }

        #endregion

        #region Methods

#warning Fake Method
        public (DateTime, DateTime) GetTimeRange(string id)
        {
            return default;
        }

        public void InitializeCatalogs()
        {
            var catalogs = this.LoadCatalogs();

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

        public DataReaderDoubleStream ReadAsDoubleStream(
            Dataset dataset,
            DateTime begin,
            DateTime end,
            ulong upperBlockSize,
            CancellationToken cancellationToken)
        {
            var progressRecords = this.Read(new List<Dataset>() { dataset }, begin, end, upperBlockSize, TimeSpan.FromMinutes(1), cancellationToken);
            var samplesPerSecond = new SampleRateContainer(dataset.Id).SamplesPerSecond;
            var length = (long)Math.Round(samplesPerSecond * 
                (decimal)(end - begin).TotalSeconds, MidpointRounding.AwayFromZero) * 
                NexusUtilities.SizeOf(NexusDataType.FLOAT64);

            return new DataReaderDoubleStream(length, progressRecords);
        }

        public IEnumerable<DataReaderProgressRecord> Read(
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

            return this.Read(new List<Dataset>() { dataset }, begin, end, upperBlockSize, fundamentalPeriod, cancellationToken);
        }

        public IEnumerable<DataReaderProgressRecord> Read(
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

            return this.Read(datasets, begin, end, upperBlockSize, fundamentalPeriod, cancellationToken);
        }

        public IEnumerable<DataReaderProgressRecord> Read(
            Dataset dataset,
            DateTime begin,
            DateTime end,
            ulong upperBlockSize,
            TimeSpan fundamentalPeriod,
            CancellationToken cancellationToken)
        {
            return this.Read(new List<Dataset>() { dataset }, begin, end, upperBlockSize, fundamentalPeriod, cancellationToken);
        }

        public IEnumerable<DataReaderProgressRecord> Read(
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

            return this.InternalRead(datasets, begin, end, blockSizeLimit, basePeriod, fundamentalPeriod, cancellationToken);
        }

        private IEnumerable<DataReaderProgressRecord> InternalRead(
            List<Dataset> datasets,
            DateTime begin,
            DateTime end,
            ulong blockSizeLimit,
            TimeSpan basePeriod,
            TimeSpan fundamentalPeriod,
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
                var datasetToRecordMap = new Dictionary<Dataset, DataRecord>();
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
                    (var data, var status) = this.ReadSingle(dataset, currentBegin, currentEnd);
                    datasetToRecordMap[dataset] = new DataRecord(data, status);

                    // update progress
                    var localProgress = TimeSpan.FromTicks(currentPeriod.Ticks * index / count);
                    var currentProgress = (currentBegin + localProgress - begin).Ticks / (double)period.Ticks;

                    ((IProgress<double>)this.Progress).Report(currentProgress);
                    index++;
                }

                // notify about new data
                yield return new DataReaderProgressRecord(datasetToRecordMap, currentBegin, currentEnd);

                // continue in time
                currentBegin += currentPeriod;
                remainingPeriod = end - currentBegin;
            }
        }

        public AvailabilityResult GetAvailability(string catalogId, DateTime begin, DateTime end, AvailabilityGranularity granularity)
        {
            var dateBegin = begin.Date;
            var dateEnd = end.Date;

            ConcurrentDictionary<DateTime, double> aggregatedData = default;
            
            var totalDays = (int)(dateEnd - dateBegin).TotalDays;

            switch (granularity)
            {
                case AvailabilityGranularity.Day:

                    aggregatedData = new ConcurrentDictionary<DateTime, double>();

                    Parallel.For(0, totalDays, day =>
                    {
                        var date = dateBegin.AddDays(day);
                        var availability = this.GetAvailability(catalogId, date);
                        aggregatedData.TryAdd(date, availability);
                    });

                    break;

                case AvailabilityGranularity.Month:

                    granularity = AvailabilityGranularity.Month;

                    var months = new DateTime[totalDays];
                    var datasets = new double[totalDays];

                    Parallel.For(0, totalDays, day =>
                    {
                        var date = dateBegin.AddDays(day);
                        var month = new DateTime(date.Year, date.Month, 1);

                        months[day] = month;
                        datasets[day] = this.GetAvailability(catalogId, date);
                    });

                    var uniqueMonths = months
                        .Distinct()
                        .OrderBy(month => month)
                        .ToList();

                    var zipData = months
                        .Zip(datasets, (month, dataset) => (month, dataset))
                        .ToList();

                    aggregatedData = new ConcurrentDictionary<DateTime, double>();

                    for (int i = 0; i < uniqueMonths.Count; i++)
                    {
                        var currentMonth = uniqueMonths[i];
                        var availability = (int)zipData
                            .Where(current => current.month == currentMonth)
                            .Average(current => current.dataset);

                        aggregatedData.TryAdd(currentMonth, availability);
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

        public bool IsDataOfDayAvailable(string catalogId, DateTime day)
        {
            return this.GetAvailability(catalogId, day) > 0;
        }

#warning Why generic?
        public abstract (T[] Dataset, byte[] Status) ReadSingle<T>(Dataset dataset, DateTime begin, DateTime end) where T : unmanaged;

        public (Array Dataset, byte[] Status) ReadSingle(Dataset dataset, DateTime begin, DateTime end)
        {
            // invoke generic method
            var type = typeof(DataReaderExtensionBase);
            var flags = BindingFlags.Instance | BindingFlags.Public;
            var genericType = NexusUtilities.GetTypeFromNexusDataType(dataset.DataType);
            var parameters = new object[] { dataset, begin, end };

            var result = NexusUtilities.InvokeGenericMethod(type, this, nameof(this.ReadSingle), flags, genericType, parameters);

            // cast result
            var resultType = result.GetType();
            var propertyInfo1 = resultType.GetField("Item1");
            var propertyInfo2 = resultType.GetField("Item2");

            var data = propertyInfo1.GetValue(result) as Array;
            var status = propertyInfo2.GetValue(result) as byte[];

            // return
            return (data, status);
        }

        public virtual void Dispose()
        {
            //
        }

        protected abstract List<Catalog> LoadCatalogs();

        protected abstract double GetAvailability(string catalogId, DateTime Day);

        #endregion
    }
}
