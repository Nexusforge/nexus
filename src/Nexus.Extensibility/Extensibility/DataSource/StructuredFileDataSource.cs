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
        // (5) Files periods are constant (except for partially written files). The current
        // implementation recognizes the first of two or more partially written files within
        // a file period but ignores the rest.

        #region Fields

        private bool _isInitialized;
        private List<Project> _projects;

        #endregion

        #region Properties

        public string RootPath { get; set; }

        public ILogger Logger { get; set; }

        public Dictionary<string, string> Parameters { get; set; }

        #endregion

        #region Protected API as seen by subclass

        protected virtual Task OnParametersSetAsync()
        {
            return Task.CompletedTask;
        }

        protected abstract Task<Configuration>
            GetConfigurationAsync(string projectId, CancellationToken cancellationToken);

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
                    var configs = (await this.GetConfigurationAsync(projectId, cancellationToken).ConfigureAwait(false)).All;

                    foreach (var config in configs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // first
                        var firstDateTime = StructuredFileDataSource
                            .GetCandidateFiles(this.RootPath, DateTime.MinValue, DateTime.MinValue, config, cancellationToken)
                            .Select(file => file.DateTime)
                            .OrderBy(current => current)
                            .FirstOrDefault();

                        if (firstDateTime == default)
                            firstDateTime = DateTime.MaxValue;

                        if (firstDateTime.Date < minDate)
                            minDate = firstDateTime.Date;

                        // last
                        var lastDateTime = StructuredFileDataSource
                            .GetCandidateFiles(this.RootPath, DateTime.MaxValue, DateTime.MaxValue, config, cancellationToken)
                            .Select(file => file.DateTime)
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

        protected virtual Task<double>
            GetAvailabilityAsync(string projectId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            if (begin >= end)
                throw new ArgumentException("The start time must be before the end time.");

            // no true async file enumeration available: https://github.com/dotnet/runtime/issues/809
            return Task.Run(async () =>
            {
                if (!Directory.Exists(this.RootPath))
                    return 0;

                var configurations = (await this.GetConfigurationAsync(projectId, cancellationToken).ConfigureAwait(false)).All;
                var summedAvailability = 0.0;

                foreach (var config in configurations)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var candidateFiles = StructuredFileDataSource.GetCandidateFiles(this.RootPath, begin, end, config, cancellationToken);

                    var files = candidateFiles
                        .Where(current => begin <= current.DateTime && current.DateTime < end)
                        .ToList();

                    var tasks = new List<Task<double>>();

                    foreach (var file in files)
                    {
                        var task = this.GetFileAvailabilityAsync(file.FilePath, cancellationToken);

                        _ = task.ContinueWith(
                            x => this.Logger.LogWarning($"Could not process file '{file.FilePath}'. Reason: {ExtensibilityUtilities.GetFullMessage(task.Exception)}"),
                            TaskContinuationOptions.OnlyOnFaulted
                        );

                        tasks.Add(task);
                    }

                    await Task.WhenAll(tasks);

                    var actual = tasks.Sum(task => task.IsCompletedSuccessfully ? task.Result : 0.0);
                    var total = (end - begin).Ticks / config.FilePeriod.Ticks;

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
            ReadSingleAsync<T>(Dataset dataset, ReadResult<T> readResult, DateTime begin, DateTime end, CancellationToken cancellationToken)
            where T : unmanaged
        {
            if (begin >= end)
                throw new ArgumentException("The start time must be before the end time.");

            var project = dataset.Channel.Project;
            var config = (await this.GetConfigurationAsync(project.Id, cancellationToken).ConfigureAwait(false)).Single(dataset);
            var samplesPerDay = dataset.GetSampleRate().SamplesPerDay;
            var fileLength = (long)Math.Round(config.FilePeriod.TotalDays * samplesPerDay, MidpointRounding.AwayFromZero);

            var bufferOffset = 0;
            var currentBegin = begin;
            var remainingPeriod = end - begin;

            while (remainingPeriod.Ticks > 0)
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
                TimeSpan consumedPeriod;

                if (CB_MINUS_FP < fileBegin && fileBegin <= currentBegin)
                {
                    var fileBeginOffset = currentBegin - fileBegin;
                    var fileOffset = (long)Math.Round(fileBeginOffset.TotalDays * samplesPerDay, MidpointRounding.AwayFromZero);
                    consumedPeriod = TimeSpan.FromTicks(Math.Min(config.FilePeriod.Ticks - fileBeginOffset.Ticks, remainingPeriod.Ticks));
                    fileBlock = (int)(fileLength - fileOffset);

                    foreach (var filePath in filePaths)
                    {
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
                                    fileBegin,
                                    fileOffset,
                                    fileLength
                                );

                                await this
                                    .ReadSingleAsync(readParameters, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                this.Logger.LogWarning($"Could not process file '{filePath}'. Reason: {ExtensibilityUtilities.GetFullMessage(ex)}");
                            }
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

        protected virtual Task<(string[], DateTime)> FindFilePathsAsync(DateTime begin, ConfigurationUnit config)
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
            var fileBegin = ExtensibilityUtilities.RoundDown(begin, config.FilePeriod);

            var folderNames = config
                .PathSegments
                .Select(segment => begin.ToString(segment));

            var folderNameArray = new List<string>() { this.RootPath }
                .Concat(folderNames)
                .ToArray();

            var folderPath = Path.Combine(folderNameArray);
            var fileName = begin.Add(config.UtcOffset).ToString(config.FileTemplate);
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

        Task IDataSource.OnParametersSetAsync()
        {
            return this.OnParametersSetAsync();
        }

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
            IEnumerable<(string FilePath, DateTime DateTime)> GetCandidateFiles(string rootPath, DateTime begin, DateTime end, ConfigurationUnit config, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // (could also be named "GetCandidateFiles", since it is similar to the "GetCandidateFolders" method)

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

        private static bool TryGetFileBeginByPath(string filePath, ConfigurationUnit config, out DateTime fileBegin, DateTime folderBegin = default)
        {
            var fileName = Path.GetFileName(filePath);

            if (StructuredFileDataSource.TryGetFileBeginByName(fileName, config, out fileBegin))
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
                            .Skip(pathSegments.Length - config.PathSegments.Length)
                            .ToArray();

                        for (int i = 0; i < pathSegments.Length; i++)
                        {
                            var folderName = pathSegments[i];
                            var folderTemplate = config.PathSegments[i];

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

        private static bool TryGetFileBeginByName(string fileName, ConfigurationUnit config, out DateTime fileBegin)
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
