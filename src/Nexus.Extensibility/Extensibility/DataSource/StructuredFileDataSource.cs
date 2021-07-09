using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public abstract class StructuredFileDataSource : IDataSource
    {
        // This implementation assumes the following:
        //
        // (1) The top-most folders carry rough date/time information while deeper nested
        // folders carry more fine-grained date/time information. Examples:
        //
        // OK:      /2019/2019-12/2019-12-31_12-00-00.dat
        // OK:      /2019-12/2019-12-31_12-00-00.dat
        // OK:      /2019-12/2019-12-31/2019-12-31_12-00-00.dat
        // OK:      /2019-12/2019-12-31/12-00-00.dat
        //
        // NOT OK:  /2019/12/...
        // NOT OK:  /2019/12-31/...
        // NOT OK:  /2019-12/31/...
        // NOT OK:  /2019-12-31/31/...
        //
        // NOTE: The format of the date/time is only illustrative and is being determined
        // by the specified format provider.
        //
        // (2) The files are always located in the most nested folder and not distributed
        // over the hierarchy.
        //
        // (3) Most nested folders are not empty.
        //
        // (4) File periods are constant (except for partially written files). The current
        // implementation recognizes the first of two or more partially written files within
        // a file period but ignores the rest.
        //
        // (5) UTC offset is a correction factor that should be selected so that the parsed
        // date/time of a file points to the UTC date/time of the very first representation within
        // that file.
        //
        // (6) Only file URLs are supported

        #region Properties

        protected string Root { get; private set; }

        private DataSourceContext Context { get; set; }

        #endregion

        #region Protected API as seen by subclass

        protected abstract Task
            SetContextAsync(DataSourceContext context, CancellationToken cancellationToken);

        protected abstract Task<Configuration>
            GetConfigurationAsync(string catalogId, CancellationToken cancellationToken);

        protected abstract Task<List<ResourceCatalog>>
            GetCatalogsAsync(CancellationToken cancellationToken);

        protected virtual Task<(DateTime Begin, DateTime End)> 
            GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                var minDateTime = DateTime.MaxValue;
                var maxDateTime = DateTime.MinValue;

                if (Directory.Exists(this.Root))
                {
                    var configs = (await this.GetConfigurationAsync(catalogId, cancellationToken).ConfigureAwait(false)).All;

                    foreach (var config in configs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // first
                        var firstDateTime = StructuredFileDataSource
                            .GetCandidateFiles(this.Root, DateTime.MinValue, DateTime.MinValue, config, cancellationToken)
                            .Select(file => file.DateTime)
                            .OrderBy(current => current)
                            .FirstOrDefault();

                        if (firstDateTime == default)
                            firstDateTime = DateTime.MaxValue;

                        firstDateTime = this.AdjustToUtc(firstDateTime, config.UtcOffset);

                        if (firstDateTime < minDateTime)
                            minDateTime = firstDateTime;

                        // last
                        var lastDateTime = StructuredFileDataSource
                            .GetCandidateFiles(this.Root, DateTime.MaxValue, DateTime.MaxValue, config, cancellationToken)
                            .Select(file => file.DateTime)
                            .OrderByDescending(current => current)
                            .FirstOrDefault();

                        if (lastDateTime == default)
                            lastDateTime = DateTime.MinValue;

                        lastDateTime = this.AdjustToUtc(lastDateTime, config.UtcOffset);
                        lastDateTime = lastDateTime.Add(config.FilePeriod);

                        if (lastDateTime > maxDateTime)
                            maxDateTime = lastDateTime;
                    }
                }

                return (minDateTime, maxDateTime);
            });
        }

        protected virtual Task<double>
            GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            if (begin >= end)
                throw new ArgumentException("The start time must be before the end time.");

            this.EnsureUtc(begin);
            this.EnsureUtc(end);

            // no true async file enumeration available: https://github.com/dotnet/runtime/issues/809
            return Task.Run(async () =>
            {
                if (!Directory.Exists(this.Root))
                    return 0;

                var configurations = (await this.GetConfigurationAsync(catalogId, cancellationToken).ConfigureAwait(false)).All;
                var summedAvailability = 0.0;

                foreach (var config in configurations)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var localBegin = begin.Add(config.UtcOffset);
                    var localEnd = end.Add(config.UtcOffset);

                    var candidateFiles = StructuredFileDataSource.GetCandidateFiles(this.Root, localBegin, localEnd, config, cancellationToken);

                    var files = candidateFiles
                        .Where(current => localBegin <= current.DateTime && current.DateTime < localEnd)
                        .ToList();

                    var availabilityTasks = files.Select(file =>
                    {
                        var availabilityTask = this.GetFileAvailabilityAsync(file.FilePath, cancellationToken);

                        _ = availabilityTask.ContinueWith(
                            x => this.Context.Logger.LogWarning($"Could not process file '{file.FilePath}'. Reason: {ExtensibilityUtilities.GetFullMessage(availabilityTask.Exception)}"),
                            TaskContinuationOptions.OnlyOnFaulted
                        );

                        return availabilityTask;
                    });

                    var availabilities = await Task.WhenAll(availabilityTasks).ConfigureAwait(false);
                    var actual = availabilities.Sum();
                    var total = (end - begin).Ticks / (double)config.FilePeriod.Ticks;

                    summedAvailability += actual / total;
                }

                return summedAvailability / configurations.Count;
            });
        }

        protected virtual Task<double> 
            GetFileAvailabilityAsync(string filePath, CancellationToken cancellationToken)
        {
            return Task.FromResult(1.0);
        }

        protected virtual async Task 
            ReadSingleAsync(RepresentationRecord representationRecord, DateTime begin, DateTime end, Memory<byte> data, Memory<byte> status, CancellationToken cancellationToken)
        {
            var representation = representationRecord.Representation;
            var catalog = representationRecord.Catalog;
            var config = (await this.GetConfigurationAsync(catalog.Id, cancellationToken).ConfigureAwait(false)).Single(representationRecord);
            var samplePeriod = representation.GetSamplePeriod();
            var fileLength = config.FilePeriod.Ticks / samplePeriod.Ticks;

            var bufferOffset = 0;
            var currentBegin = begin;
            var totalPeriod = end - begin;
            var consumedPeriod = TimeSpan.Zero;
            var remainingPeriod = totalPeriod;

            while (consumedPeriod < totalPeriod)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // get file path and begin
                (var filePaths, var fileBegin) = await this.FindFilePathsAsync(currentBegin, config);

                // determine file begin if not yet done using the first file name returned
                if (filePaths is not null && fileBegin == default)
                {
                    if (!StructuredFileDataSource.TryGetFileBeginByPath(filePaths.First(), config, out fileBegin, default))
                        throw new Exception("Unable to determine file date/time.");
                }

                /* CB = Current Begin, FP = File Period
                 * 
                 *  begin    CB-FP        CB         CB+FP                 end
                 *    |--------|-----------|-----------|-----------|--------|
                 */
                var CB_MINUS_FP = currentBegin - config.FilePeriod;
                var CB_PLUS_FP = currentBegin + config.FilePeriod;

                int fileBlock;
                TimeSpan currentPeriod;

                /* normal case: current begin may be greater than file begin if: 
                 * - this is the very first iteration
                 * - the current file begin later than expected (incomplete file)
                 */
                if (CB_MINUS_FP < fileBegin && fileBegin <= currentBegin)
                {
                    var consumedFilePeriod = currentBegin - fileBegin;
                    var remainingFilePeriod = config.FilePeriod - consumedFilePeriod;

                    currentPeriod = TimeSpan.FromTicks(Math.Min(remainingFilePeriod.Ticks, remainingPeriod.Ticks));
                    fileBlock = (int)(currentPeriod.Ticks / samplePeriod.Ticks);

                    var fileOffset = consumedFilePeriod.Ticks / samplePeriod.Ticks;

                    foreach (var filePath in filePaths)
                    {
                        if (File.Exists(filePath))
                        {
                            try
                            {
                                var slicedData = data
                                    .Slice(bufferOffset * representation.ElementSize, fileBlock * representation.ElementSize);

                                var slicedStatus = status
                                    .Slice(bufferOffset, fileBlock);

                                var readInfo = new ReadInfo(
                                    filePath,
                                    representationRecord,
                                    slicedData,
                                    slicedStatus,
                                    fileBegin,
                                    fileOffset,
                                    fileBlock,
                                    fileLength
                                );

                                await this
                                    .ReadSingleAsync(readInfo, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                this.Context.Logger.LogWarning($"Could not process file '{filePath}'. Reason: {ExtensibilityUtilities.GetFullMessage(ex)}");
                            }
                        }
                    }
                }
                /* there was an incomplete file, skip the incomplete part */
                else if (CB_PLUS_FP <= fileBegin && fileBegin < end)
                {
                    currentPeriod = fileBegin - currentBegin;
                    fileBlock = (int)(currentPeriod.Ticks / samplePeriod.Ticks);
                }
                else
                {
                    break;
                }

                // update loop state
                bufferOffset += fileBlock;
                currentBegin += currentPeriod;
                consumedPeriod += currentPeriod;
                remainingPeriod -= currentPeriod;
            }
        }

        protected abstract Task
            ReadSingleAsync(ReadInfo info, CancellationToken cancellationToken);

        protected virtual Task<(string[], DateTime)> 
            FindFilePathsAsync(DateTime begin, ConfigurationUnit config)
        {
            // This implementation assumes that the file start times are aligned to multiples
            // of the file period. Depending on the file template, it is possible to find more
            // than one matching file. There is one special case where two files are expected:
            // A data logger creates versioned files with a granularity of e.g. 1 file per day.
            // When the version changes, the logger creates a new file with same name but new
            // version. This could look like this:
            // 2020-01-01T00-00-00Z_v1.dat (contains data from midnight to time t0)
            // 2020-01-01T00-00-00Z_v2.dat (contains data from time t0 + x to next midnight)
            // Where x is the time period the system was offline to apply the new version.

            var localBegin = begin.Kind switch
            {
                DateTimeKind.Local => begin,
                DateTimeKind.Utc => DateTime.SpecifyKind(begin.Add(config.UtcOffset), DateTimeKind.Local),
                _ => throw new ArgumentException("The begin parameter must have its kind property specified.")
            };

            var fileBegin = ExtensibilityUtilities.RoundDown(begin, config.FilePeriod);

            var folderNames = config
                .PathSegments
                .Select(segment => localBegin.ToString(segment));

            var folderNameArray = new List<string>() { this.Root }
                .Concat(folderNames)
                .ToArray();

            var folderPath = Path.Combine(folderNameArray);
            var fileName = localBegin.ToString(config.FileTemplate);
            var filePaths = new string[] { Path.Combine(folderPath, fileName) };

            if (fileName.Contains("?") || fileName.Contains("*") && Directory.Exists(folderPath))
            {
                filePaths = Directory
                   .EnumerateFiles(folderPath, fileName)
                   .ToArray();
            }

            return Task.FromResult((filePaths, fileBegin));
        }

        #endregion

        #region Public API as seen by Nexus and unit tests

        async Task 
            IDataSource.SetContextAsync(DataSourceContext context, CancellationToken cancellationToken)
        {
            this.Root = context.ResourceLocator.ToPath();
            this.Context = context;

            await this.SetContextAsync(context, cancellationToken);
        }

        async Task<List<ResourceCatalog>> 
            IDataSource.GetCatalogsAsync(CancellationToken cancellationToken)
        {
            if (this.Context.Catalogs is null)
            {
                this.Context = this.Context with
                {
                    Catalogs = await this.GetCatalogsAsync(cancellationToken)
                };
            }

            return this.Context.Catalogs;
        }

        Task<(DateTime Begin, DateTime End)> 
            IDataSource.GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            return this.GetTimeRangeAsync(catalogId, cancellationToken);
        }

        Task<double> 
            IDataSource.GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            return this.GetAvailabilityAsync(catalogId, begin, end, cancellationToken);
        }

        async Task
            IDataSource.ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (begin >= end)
                throw new ArgumentException("The start time must be before the end time.");

            this.EnsureUtc(begin);
            this.EnsureUtc(end);

            var counter = 0.0;

            foreach (var (resourcePath, dataBuffer, statusBuffer) in requests)
            {
                var representationRecord = ResourceCatalog.Find(resourcePath, this.Context.Catalogs);

                try
                {
                    await this.ReadSingleAsync(representationRecord, begin, end, dataBuffer, statusBuffer, cancellationToken);
                }
                catch (Exception ex)
                {
                    this.Context.Logger.LogWarning($"Could not read representation '{resourcePath}'. Reason: {ExtensibilityUtilities.GetFullMessage(ex)}");
                }

                progress.Report(++counter / requests.Length);
            }
        }

        #endregion

        #region Helpers

        private static IEnumerable<(string FilePath, DateTime DateTime)> 
            GetCandidateFiles(string rootPath, DateTime begin, DateTime end, ConfigurationUnit config, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // initial check
            if (!Directory.Exists(rootPath))
                return new List<(string, DateTime)>();

            // get all candidate folders
            var candidateFolders = config.PathSegments.Length >= 1

                ? StructuredFileDataSource
                    .GetCandidateFolders(rootPath, default, begin, end, config.PathSegments, cancellationToken)

                : new List<(string, DateTime)>() { (rootPath, default) };

            return candidateFolders.SelectMany(currentFolder =>
            {
                var filePaths = Directory.EnumerateFiles(currentFolder.FolderPath);

                var candidateFiles = filePaths
                    .Select(filePath =>
                    {
                        var success = StructuredFileDataSource
                            .TryGetFileBeginByPath(filePath, config, out var fileBegin, currentFolder.DateTime);

                        return (success, filePath, fileBegin);
                    })
                    .Where(current => current.success)
                    .Select(current => (current.filePath, current.fileBegin));

                return candidateFiles;
            });
        }

        private static IEnumerable<(string FolderPath, DateTime DateTime)> 
            GetCandidateFolders(string root, DateTime rootDate, DateTime begin, DateTime end, string[] pathSegments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // get all available folders
            var folderPaths = Directory
                .EnumerateDirectories(root)
                .ToList();

            // get all folders that can be parsed
            var hasDateTimeInformation = false;

            var folderNameToDateTimeMap = folderPaths
                .Select(folderPath =>
                {
                    var folderName = Path.GetFileName(folderPath);

                    var success = DateTime
                        .TryParseExact(
                            folderName,
                            pathSegments.First(),
                            default,
                            DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AdjustToUniversal,
                            out var parsedDateTime
                        );

                    if (parsedDateTime == default)
                        parsedDateTime = rootDate;

                    else
                        hasDateTimeInformation = true;

                    return (folderPath, parsedDateTime);
                })
               .ToDictionary(current => current.folderPath, current => current.parsedDateTime);

            // keep only folders that fall within the wanted time range

            /* The expected segment name is used for two purposes:
             * (1) for "filter by search date" where only the begin date/time matters
             * (2) for "filter by exact match" where any date/time can be put into
             * the ToString() method to remove the quotation marks from the path segment
             */
            var expectedSegmentName = begin.ToString(pathSegments.First());

            var folderCandidates = hasDateTimeInformation

                // filter by search date
                ? StructuredFileDataSource
                    .FilterBySearchDate(begin, end, folderNameToDateTimeMap, expectedSegmentName)

                // filter by exact match
                : folderNameToDateTimeMap
                    .Where(entry => Path.GetFileName(entry.Key) == expectedSegmentName)
                    .Select(entry => (entry.Key, entry.Value));

            // go deeper
            if (pathSegments.Count() > 1)
            {
                return folderCandidates.SelectMany(current =>
                    StructuredFileDataSource.GetCandidateFolders(
                        current.Key,
                        current.Value,
                        begin,
                        end,
                        pathSegments.Skip(1).ToArray(), 
                        cancellationToken
                    )
                );
            }

            // we have reached the most nested folder level
            else
            {
                return folderCandidates;
            }
        }

        private static IEnumerable<(string Key, DateTime Value)> 
            FilterBySearchDate(DateTime begin, DateTime end, Dictionary<string, DateTime> folderNameToDateTimeMap, string expectedSegmentName)
        {
            if (begin == DateTime.MinValue && end == DateTime.MinValue)
            {
                var folderCandidate = folderNameToDateTimeMap
                    .OrderBy(entry => entry.Value)
                    .FirstOrDefault();

                return new List<(string, DateTime)>() { (folderCandidate.Key, folderCandidate.Value) };
            }

            else if (begin == DateTime.MaxValue && end == DateTime.MaxValue)
            {
                var folderCandidate = folderNameToDateTimeMap
                   .OrderByDescending(entry => entry.Value)
                   .FirstOrDefault();

                return new List<(string, DateTime)>() { (folderCandidate.Key, folderCandidate.Value) };
            }

            else
            {
                return folderNameToDateTimeMap
                    .Where(entry =>
                    {
                        // Check for the case that the parsed date/time
                        // (1) is more specific (2020-01-01T22) than the search time range (2020-01-01T00 - 2021-01-02T00):
                        // (2) is less specific but in-between (2020-02) the search time range (2020-01-01 - 2021-03-01)
                        if (begin <= entry.Value && entry.Value < end)
                            return true;

                        // Check for the case that the parsed date/time
                        // (1) is less specific (2020-01) and outside the search time range (2020-01-02 - 2020-01-03)
                        else
                            return Path.GetFileName(entry.Key) == expectedSegmentName;
                    })
                    .Select(entry => (entry.Key, entry.Value));
            }
        }

        private static bool 
            TryGetFileBeginByPath(string filePath, ConfigurationUnit config, out DateTime fileBegin, DateTime folderBegin = default)
        {
            var fileName = Path.GetFileName(filePath);

            if (StructuredFileDataSource.TryGetFileBeginByName(fileName, config, out fileBegin))
            {
                // When TryGetFileBeginByName == true, then the input string was parsed successfully and the
                // result contains date/time information of either kind: date+time, time-only, default.

                // date+time: use file date/time
                if (fileBegin.Date != default)
                {
                    return true;
                }

                // time-only: use combined folder and file date/time
                else if (fileBegin != default)
                {
                    // short cut
                    if (folderBegin != default)
                    {
                        fileBegin = new DateTime(folderBegin.Date.Ticks + fileBegin.TimeOfDay.Ticks, fileBegin.Kind);
                        return true;
                    }

                    // long way
                    else
                    {
                        var pathSegments = filePath
                            .Split('/', '\\');

                        pathSegments = pathSegments
                            .Skip(pathSegments.Length - config.PathSegments.Length)
                            .ToArray();

                        for (int i = 0; i < pathSegments.Length; i++)
                        {
                            var folderName = pathSegments[i];
                            var folderTemplate = config.PathSegments[i];

                            var _ = DateTime.TryParseExact(
                                folderName,
                                folderTemplate,
                                default,
                                DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AdjustToUniversal,
                                out var currentFolderBegin
                            );

                            if (currentFolderBegin > folderBegin)
                                folderBegin = currentFolderBegin;
                        }

                        fileBegin = folderBegin;
                        return fileBegin != default;
                    }
                }
                // default: use folder date/time
                else
                {
                    fileBegin = folderBegin;
                    return fileBegin != default;
                }
            }
            // no date + no time: failed
            else
            {
                return false;
            }
        }

        private static bool 
            TryGetFileBeginByName(string fileName, ConfigurationUnit config, out DateTime fileBegin)
        {
            /* (1) Regex is required in scenarios when there are more complex
             * file names, i.e. file names containing an opaque string that
             * changes for every file. This could be a counter, a serial
             * number or some other unpredictable proprietary string.
             *
             * (2) It is also required as a filter if there is more than one
             * file type in the containing folder, e.g. high frequent and
             * averaged data files that are being treated as different sources.
             */

            var fileTemplate = config.FileTemplate;

            if (!string.IsNullOrWhiteSpace(config.FileDateTimePreselector))
            {
                if (string.IsNullOrEmpty(config.FileDateTimeSelector))
                    throw new Exception("When a file date/time preselector is provided, the selector itself must be provided too.");

                fileTemplate = config.FileDateTimeSelector;
                var regex = new Regex(config.FileDateTimePreselector);

                fileName = string.Join("", regex
                    .Match(fileName)
                    .Groups
                    .Cast<Group>()
                    .Skip(1)
                    .Select(match => match.Value)
                );
            }

            var success = DateTime.TryParseExact(
                fileName,
                fileTemplate,
                default,
                DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AdjustToUniversal,
                out fileBegin
            );

            return success;
        }

        private void 
            EnsureUtc(DateTime dateTime)
        {
            if (dateTime.Kind != DateTimeKind.Utc)
                throw new ArgumentException("UTC date/times are required.");
        }

        private DateTime 
            AdjustToUtc(DateTime dateTime, TimeSpan utcOffset)
        {
            var result = dateTime;

            if (dateTime != DateTime.MinValue && dateTime != DateTime.MaxValue)
            {
                if (dateTime.Kind != DateTimeKind.Utc)
                    result = DateTime.SpecifyKind(dateTime.Subtract(utcOffset), DateTimeKind.Utc);
            }

            return result;
        }

        #endregion
    }
}
