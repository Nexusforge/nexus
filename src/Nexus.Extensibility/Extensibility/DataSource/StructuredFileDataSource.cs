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
        // (4) UtcOffset is only considered when reading data, not to determine the
        // availability or the project time range.
        //
        // (5) Files periods are constant (except for partially written files)

        #region Fields

        private bool _isInitialized;
        private List<Project> _projects;

        #endregion

        #region Properties

        public string RootPath { get; set; }

        public ILogger Logger { get; set; }

        public Dictionary<string, string> Options { get; set; }

        #endregion

        #region Protected API as seen by subclass

        protected abstract Task<List<SourceDescription>>
            GetSourceDescriptionsAsync(string projectId, CancellationToken cancellationToken);

        protected abstract Task<List<Project>>
            GetDataModelAsync(CancellationToken cancellationToken);

        protected virtual async Task<(DateTime Begin, DateTime End)> 
            GetProjectTimeRangeAsync(string projectId, CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                var minDate = DateTime.MaxValue;
                var maxDate = DateTime.MinValue;

                if (Directory.Exists(this.RootPath))
                {
                    var sourceDescriptions = await this.GetSourceDescriptionsAsync(projectId, cancellationToken);

                    foreach (var sourceDescription in sourceDescriptions)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // first
                        var firstDateTime = StructuredFileDataSource
                            .GetCandidateDateTimes(this.RootPath, DateTime.MinValue, DateTime.MinValue, sourceDescription, cancellationToken)
                            .OrderBy(current => current)
                            .FirstOrDefault();

                        if (firstDateTime == default)
                            firstDateTime = DateTime.MaxValue;

                        if (firstDateTime.Date < minDate)
                            minDate = firstDateTime.Date;

                        // last
                        var lastDateTime = StructuredFileDataSource
                            .GetCandidateDateTimes(this.RootPath, DateTime.MaxValue, DateTime.MaxValue, sourceDescription, cancellationToken)
                            .OrderByDescending(current => current)
                            .FirstOrDefault();

                        if (lastDateTime == default)
                            lastDateTime = DateTime.MinValue;

                        if (lastDateTime.Date > maxDate)
                            maxDate = lastDateTime.Date;
                    }
                }

                return (minDate, maxDate);
            }).ConfigureAwait(false);
        }

        protected virtual async Task<double>
            GetAvailabilityAsync(string projectId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            if (begin >= end)
                throw new ArgumentException("The start time must be before the end time.");

            // no true async file enumeration available: https://github.com/dotnet/runtime/issues/809
            return await Task.Run(async () =>
            {
                if (!Directory.Exists(this.RootPath))
                    return 0;

                var sourceDescription = await this.GetSourceDescriptionsAsync(projectId, cancellationToken);

                var summedAvailability = sourceDescription.Sum(sourceDescription =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var candidateDateTimes = StructuredFileDataSource.GetCandidateDateTimes(this.RootPath, begin, end, sourceDescription, cancellationToken);

                    var fileCount = candidateDateTimes
                        .Where(current => begin <= current && current < end)
                        .Count();

                    var filesPerTimeRange = (end - begin).Ticks / sourceDescription.FilePeriod.Ticks;

                    return fileCount / (double)filesPerTimeRange;
                });

                return summedAvailability / sourceDescription.Count;
            }).ConfigureAwait(false);
        }

        protected virtual async Task 
            ReadSingleAsync<T>(Dataset dataset, ReadResult<T> readResult, DateTime begin, DateTime end, CancellationToken cancellationToken)
            where T : unmanaged
        {
            if (begin >= end)
                throw new ArgumentException("The start time must be before the end time.");

            var project = dataset.Channel.Project;
#warning !!!
            var sourceDescription = (await this.GetSourceDescriptionsAsync(project.Id, cancellationToken)).First();
            var samplesPerDay = dataset.GetSampleRate().SamplesPerDay;
            var fileLength = (long)Math.Round(sourceDescription.FilePeriod.TotalDays * samplesPerDay, MidpointRounding.AwayFromZero);

            var bufferOffset = 0;
            var currentBegin = begin;
            var remainingPeriod = end - begin;

            while (remainingPeriod.Ticks > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int fileBlock;
                TimeSpan consumedPeriod;

                // get file path and begin
                var localCurrentBegin = DateTime.SpecifyKind(currentBegin.Add(sourceDescription.UtcOffset), DateTimeKind.Utc);
                (var filePath, var fileBegin) = await this.FindFilePathAsync(localCurrentBegin, sourceDescription);
                fileBegin = fileBegin.Add(-sourceDescription.UtcOffset);

                // determine file begin if not yet done
                if (!string.IsNullOrWhiteSpace(filePath) && fileBegin == default)
                {
                    if (!StructuredFileDataSource.TryGetFileBeginByPath(filePath, sourceDescription, out fileBegin, default))
                        throw new Exception("Unable to determine file date/time.");
                }

                /* CB = Current Begin, FP = File Period
                 * 
                 *  begin    CB-FP        CB         CB+FP                 end
                 *    |--------|-----------|-----------|-----------|--------|
                 */
                var CB_MINUS_FP = currentBegin - sourceDescription.FilePeriod;
                var CB_PLUS_FP = currentBegin + sourceDescription.FilePeriod;

                if (CB_MINUS_FP < fileBegin && fileBegin <= currentBegin)
                {
                    var fileBeginOffset = currentBegin - fileBegin;
                    var fileOffset = (long)Math.Round(fileBeginOffset.TotalDays * samplesPerDay, MidpointRounding.AwayFromZero);
                    consumedPeriod = TimeSpan.FromTicks(Math.Min(sourceDescription.FilePeriod.Ticks - fileBeginOffset.Ticks, remainingPeriod.Ticks));
                    fileBlock = (int)(fileLength - fileOffset);

                    if (File.Exists(filePath))
                    {
                        try
                        {
                            var slicedData = readResult
                                .Data
                                .Slice(bufferOffset, fileBlock);

                            var slicedStatus = readResult
                                .Status
                                .Slice(bufferOffset, fileBlock);

                            var readParameters = new ReadInfo<T>(
                                filePath,
                                dataset,
                                slicedData,
                                slicedStatus,
                                fileOffset,
                                fileLength
                            );

                            await this
                                .ReadSingleAsync(readParameters, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            this.Logger.LogError(ex.Message);
                        }
                    }
                }
                else if (CB_PLUS_FP <= fileBegin && fileBegin < end)
                {
                    consumedPeriod = fileBegin - currentBegin;
                    fileBlock = (int)Math.Round(consumedPeriod.TotalDays * samplesPerDay, MidpointRounding.AwayFromZero);
                }
                else
                {
                    break;
                }

                // update loop state
                bufferOffset += fileBlock;
                remainingPeriod -= consumedPeriod;
                currentBegin += consumedPeriod;
            }
        }

        protected abstract Task 
            ReadSingleAsync<T>(ReadInfo<T> readInfo, CancellationToken cancellationToken)
            where T : unmanaged;

        protected virtual Task<(string, DateTime)> FindFilePathAsync(DateTime begin, SourceDescription sourceDescription)
        {
            // This implementation assumes that all files date and times are aligned multiples of the file period.
            var fileBegin = ExtensibilityUtilities.RoundDown(begin, sourceDescription.FilePeriod);

            var folderNames = sourceDescription
                .PathSegments
                .Select(segment => begin.ToString(segment));

            var folderNameArray = new List<string>() { this.RootPath }
                .Concat(folderNames)
                .ToArray();

            var folderPath = Path.Combine(folderNameArray);
            var fileName = begin.ToString(sourceDescription.FileTemplate);
            var filePath = Path.Combine(folderPath, fileName);

            if (fileName.Contains("?") || fileName.Contains("*") && Directory.Exists(folderPath))
                filePath = Directory.EnumerateFiles(folderPath, fileName).FirstOrDefault();

            return Task.FromResult((filePath, fileBegin));
        }

        #endregion

        #region Public API as seen by Nexus and unit tests

        async Task<List<Project>> 
            IDataSource.GetDataModelAsync(CancellationToken cancellationToken)
        {
            await this
                .EnsureProjectsAsync(cancellationToken)
                .ConfigureAwait(false);

            return _projects;
        }

        Task<(DateTime Begin, DateTime End)> 
            IDataSource.GetProjectTimeRangeAsync(string projectId, CancellationToken cancellationToken)
        {
            return this.GetProjectTimeRangeAsync(projectId, cancellationToken);
        }

        Task<double> 
            IDataSource.GetAvailabilityAsync(string projectId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            return this.GetAvailabilityAsync(projectId, begin, end, cancellationToken);
        }

        Task 
            IDataSource.ReadSingleAsync<T>(Dataset dataset, ReadResult<T> readResult, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            return this.ReadSingleAsync(dataset, readResult, begin, end, cancellationToken);
        }

        #endregion

        #region Helpers

        private async Task
            EnsureProjectsAsync(CancellationToken cancellationToken)
        {
            if (!_isInitialized)
            {
                _projects = await this
                    .GetDataModelAsync(cancellationToken)
                    .ConfigureAwait(false);

                _isInitialized = true;
            }
        }

        private static 
            IEnumerable<DateTime> GetCandidateDateTimes(string rootPath, DateTime begin, DateTime end, SourceDescription sourceDescription, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // (could also be named "GetCandidateFiles", since it is similar to the "GetCandidateFolders" method)

            // initial check
            if (!Directory.Exists(rootPath))
                return new List<DateTime>();

            // get all candidate folders
            var candidateFolders = sourceDescription.PathSegments.Length >= 1

                ? StructuredFileDataSource
                    .GetCandidateFolders(rootPath, default, begin, end, sourceDescription.PathSegments, cancellationToken)

                : new List<(string, DateTime)>() { (rootPath, default) };

            return candidateFolders.SelectMany(currentFolder =>
            {
                var filePaths = Directory.EnumerateFiles(currentFolder.FolderPath);

                var candidateDateTimes = filePaths
                    .Select(filePath =>
                    {
                        var success = StructuredFileDataSource
                            .TryGetFileBeginByPath(filePath, sourceDescription, out var fileBegin, currentFolder.DateTime);

                        return (success, fileBegin);
                    })
                    .Where(current => current.success)
                    .Select(current => current.fileBegin);            

                return candidateDateTimes;
            });
        }

        private static 
            IEnumerable<(string FolderPath, DateTime DateTime)> GetCandidateFolders(string root, DateTime rootDate, DateTime begin, DateTime end, string[] pathSegments, CancellationToken cancellationToken)
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
                            DateTimeStyles.NoCurrentDateDefault,
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
                        pathSegments.Skip(1).ToArray(), cancellationToken
                    )
                );
            }

            // we have reached the most nested folder level
            else
            {
                return folderCandidates;
            }
        }

        private static 
            IEnumerable<(string Key, DateTime Value)> FilterBySearchDate(DateTime begin, DateTime end, Dictionary<string, DateTime> folderNameToDateTimeMap, string expectedSegmentName)
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

        private static bool TryGetFileBeginByPath(string filePath, SourceDescription sourceDescription, out DateTime fileBegin, DateTime folderBegin = default)
        {
            var fileName = Path.GetFileName(filePath);

            if (StructuredFileDataSource.TryGetFileBeginByName(fileName, sourceDescription, out fileBegin))
            {
                // use file date/time
                if (fileBegin.Date != default)
                {
                    return true;
                }

                // use combined folder and file date/time
                else if (fileBegin.TimeOfDay != default)
                {
                    // short cut
                    if (folderBegin != default)
                    {
                        fileBegin = new DateTime(folderBegin.Date.Ticks + fileBegin.TimeOfDay.Ticks);
                        return true;
                    }

                    // long way
                    else
                    {
                        var pathSegments = filePath
                            .Split('/', '\\');

                        pathSegments = pathSegments
                            .Skip(pathSegments.Length - sourceDescription.PathSegments.Length)
                            .ToArray();

                        for (int i = 0; i < pathSegments.Length; i++)
                        {
                            var folderName = pathSegments[i];
                            var folderTemplate = sourceDescription.PathSegments[i];

                            var success = DateTime.TryParseExact(
                                folderName,
                                folderTemplate,
                                default,
                                DateTimeStyles.NoCurrentDateDefault,
                                out var currentFolderBegin
                            );

                            if (currentFolderBegin > folderBegin)
                                folderBegin = currentFolderBegin;
                        }

                        if (folderBegin == default)
                            return false;

                        return true;
                    }
                }

                // use folder date/time
                else
                {
                    fileBegin = folderBegin;
                    return true;
                }
            }
            else
            {
                return false;
            }
        }

        private static bool TryGetFileBeginByName(string fileName, SourceDescription sourceDescription, out DateTime fileBegin)
        {
            /* (1) Regex is required in scenarios when there are more complex
             * file names, i.e. file names containing an opaque string that
             * changes for every file. This could be a counter, a serial
             * number or some other unpredictable proprietary string.
             *
             * (2) It is also required as a filter if there is more than one
             * file type in the containing folder, i.e. high frequent and
             * averaged data files that are being treated as different sources.
             */

            var fileTemplate = sourceDescription.FileTemplate;

            if (!string.IsNullOrWhiteSpace(sourceDescription.FileDateTimePreselector))
            {
                if (string.IsNullOrEmpty(sourceDescription.FileDateTimeSelector))
                    throw new Exception("When a file date/time preselector is provided, the selector itself must be provided too.");

                fileTemplate = sourceDescription.FileDateTimeSelector;
                var regex = new Regex(sourceDescription.FileDateTimePreselector);

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
                DateTimeStyles.NoCurrentDateDefault,
                out fileBegin
            );

            // Parsing "xyz" with format "'xyz'" will succeed,
            // but returns DateTime.MinValue, so filter it out:
            return success && fileBegin != DateTime.MinValue;
        }

        #endregion
    }
}
