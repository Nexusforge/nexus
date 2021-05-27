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
    public abstract class SimpleFileDataSource : IDataSource
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
        // by separate format providers.
        //
        // (2) The files are always located in the most nested folder and not distributed
        // over the hierarchy.
        //
        // (3) Most nested folders are not empty.
        //
        // (4) UtcOffset is only considered when reading data, not to determine the
        // availability or the project time range.
        //
        // (5) File names begin at multiples of the file length, e.g. file length = 00:10:00
        // and file start times are 

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
                    var sources = await this.GetSourceDescriptionsAsync(projectId, cancellationToken);

                    foreach (var source in sources)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // first
                        var firstDateTime = SimpleFileDataSource
                            .GetCandidateDateTimes(this.RootPath, DateTime.MinValue, DateTime.MinValue, source, cancellationToken)
                            .OrderBy(current => current)
                            .FirstOrDefault();

                        if (firstDateTime == default)
                            firstDateTime = DateTime.MaxValue;

                        if (firstDateTime.Date < minDate)
                            minDate = firstDateTime.Date;

                        // last
                        var lastDateTime = SimpleFileDataSource
                            .GetCandidateDateTimes(this.RootPath, DateTime.MaxValue, DateTime.MaxValue, source, cancellationToken)
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

                var sources = await this.GetSourceDescriptionsAsync(projectId, cancellationToken);

                var summedAvailability = sources.Sum(source =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var candidateDateTimes = SimpleFileDataSource.GetCandidateDateTimes(this.RootPath, begin, end, source, cancellationToken);

                    var fileCount = candidateDateTimes
                        .Where(current => begin <= current && current < end)
                        .Count();

                    var filesPerTimeRange = (end - begin).Ticks / source.FilePeriod.Ticks;

                    return fileCount / (double)filesPerTimeRange;
                });

                return summedAvailability / sources.Count;
            }).ConfigureAwait(false);
        }

        protected virtual async Task 
            ReadSingleAsync<T>(Dataset dataset, ReadResult<T> readResult, DateTime begin, DateTime end, CancellationToken cancellationToken)
            where T : unmanaged
        {
            throw new NotImplementedException();
//            var project = dataset.Channel.Project;
//#warning !!!
//            var sourceDescription = (await this.GetSourceDescriptionsAsync(project.Id, cancellationToken)).First();
//            var samplesPerDay = dataset.GetSampleRate().SamplesPerDay;

//            var currentBegin = ExtensibilityUtilities.RoundDown(begin, sourceDescription.FilePeriod);
//            var fileLength = (int)Math.Round(sourceDescription.FilePeriod.TotalDays * samplesPerDay, MidpointRounding.AwayFromZero);
//            var fileOffset = (int)Math.Round((begin - currentBegin).TotalDays * samplesPerDay, MidpointRounding.AwayFromZero);
//            var bufferOffset = 0;
//            var remainingBufferLength = readResult.Length;

//            while (remainingBufferLength > 0)
//            {
//                // get data
//                var groupSettings = _config.GroupSettings[dataset.Channel.Group];
//                var fileName = $"{currentBegin.ToString(_config.DateTimeFormat)}{groupSettings.PathPostfix}";
//                var filePath = Directory.EnumerateFiles(_dataFolderPath, fileName, SearchOption.TopDirectoryOnly).FirstOrDefault();

//                var filePath = this.GetFilePathAsync();
//                var fileBlock = fileLength - fileOffset;
//                var currentBlock = Math.Min(remainingBufferLength, fileBlock);

//                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
//                {
//                    try
//                    {
//                        using var campbellFile = new CampbellFile(filePath);

//                        var campbellVariable = campbellFile.Variables.First(current => current.Name.Replace("thies_", "thies_25_") == dataset.Channel.Name);
//                        var campbellData = campbellFile.Read<T>(campbellVariable);
//                        var result = campbellData.Data.Buffer;

//                        // write data
//                        if (result.Length == fileLength)
//                        {
//                            result.AsSpan(fileOffset, currentBlock).CopyTo(data.AsSpan(bufferOffset));
//                            status.AsSpan(bufferOffset, currentBlock).Fill(1);
//                        }
//                    }
//                    catch (Exception ex)
//                    {
//                        this.Logger.LogError(ex.Message);
//                    }
//                }

//                // update loop state
//                fileOffset = 0; // Only the data in the first file may have an offset.
//                bufferOffset += currentBlock;
//                remainingBufferLength -= currentBlock;
//                currentBegin += _config.Period;
//            }

//            return Task.FromResult(new ReadResult<T>(data, status));
        }

        //protected async Task<string> GetFilePathAsync(Dataset dataset, DateTime currentBegin)
        //{

        //}

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

        private async Task EnsureProjectsAsync(CancellationToken cancellationToken)
        {
            if (!_isInitialized)
            {
                _projects = await this
                    .GetDataModelAsync(cancellationToken)
                    .ConfigureAwait(false);

                _isInitialized = true;
            }
        }

        private static IEnumerable<DateTime> GetCandidateDateTimes(string rootPath, 
                                                                   DateTime begin,
                                                                   DateTime end,
                                                                   SourceDescription source,
                                                                   CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // (could also be named "GetCandidateFiles", since it is similar to the "GetCandidateFolders" method)

            // initial check
            if (!Directory.Exists(rootPath))
                return new List<DateTime>();

            // get all candidate folders
            var candidateFolders = source.PathSegments.Count >= 1

                ? SimpleFileDataSource
                    .GetCandidateFolders(rootPath, default, begin, end, source.PathSegments, cancellationToken)

                : new List<(string, DateTime)>() { (rootPath, default) };

            // get all files that can be parsed
            var fileTemplate = source.FileTemplate;
            var regex = default(Regex);

            if (!string.IsNullOrWhiteSpace(source.FileDateTimePreselector))
            {
                if (string.IsNullOrEmpty(source.FileDateTimeSelector))
                    throw new Exception("When a file date/time preselector is provided, the selector itself must be provided too.");

                fileTemplate = source.FileDateTimeSelector;
                regex = new Regex(source.FileDateTimePreselector);
            }

            return candidateFolders.SelectMany(currentFolder =>
            {
                var filePaths = Directory.EnumerateFiles(currentFolder.FolderPath);

                // (1) Regex is required in scenarios when there are more complex
                // file names, i.e. file names containing an opaque string that
                // changes for every file. This could be a counter, a serial
                // number or some other unpredictable proprietary string.
                // 
                // (2) It is also required as a filter if there is more than one
                // file type in the containing folder, i.e. high frequent and
                // averaged data files that are being treated as different sources.
                var matchedFiles = filePaths
                    .Select(filePath =>
                    {
                        var fileName = Path.GetFileName(filePath);

                        var match = regex is not null
                           ? regex.Match(fileName)
                           : null;

                        return (fileName, match);
                    })
                    .Where(current => current.match == null || current.match.Success);

                var candidateDateTimes = matchedFiles
                    .Select(currentFileMatch =>
                    {
                        var fileName = currentFileMatch.fileName;

                        if (regex is not null)
                            fileName = string.Join("", regex
                                .Match(fileName).Groups
                                .Cast<Group>()
                                .Skip(1)
                                .Select(match => match.Value)
                        );

                        var success = DateTime.TryParseExact(
                            fileName,
                            fileTemplate,
                            default,
                            DateTimeStyles.NoCurrentDateDefault,
                            out var parsedDateTime
                        );

                        if (success)
                        {
                            // Parsing "yxz" with format "'xyz'" will succeed,
                            // as well as parsing files with time information only,
                            // so further distinction is required:

                            // use file date/time
                            if (parsedDateTime.Date != default)
                                return parsedDateTime;

                            // use combined folder and file date/time
                            if (parsedDateTime.TimeOfDay != default)
                                return new DateTime(currentFolder.DateTime.Date.Ticks + parsedDateTime.TimeOfDay.Ticks);

                            // use folder date/time
                            else
                                return currentFolder.DateTime;
                        }
                        // should only happen when file path segment template and preselection are incorrect
                        else
                        {
                            return begin == DateTime.MinValue && end == DateTime.MinValue
                                ? DateTime.MaxValue  // begin/end == DateTime.MinValue
                                : DateTime.MinValue; // begin/end == DateTime.MaxValue or begin/end == every other date
                        }
                    })
                    // begin/end == DateTime.MinValue: do not allow default values to propagate
                    // begin/end == DateTime.MaxValue: it does not matter if DateTime.MinValue or empty collection is returned
                    // begin/end == every other date:  it doesn't matter if default values are removed
                    .Where(current => current != default);

                return candidateDateTimes;
            });
        }

        private static IEnumerable<(string FolderPath, DateTime DateTime)> GetCandidateFolders(string root,
                                                                                               DateTime rootDate,
                                                                                               DateTime begin,
                                                                                               DateTime end,
                                                                                               List<string> pathSegments,
                                                                                               CancellationToken cancellationToken)
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
                ? SimpleFileDataSource
                    .FilterBySearchDate(begin, end, folderNameToDateTimeMap, expectedSegmentName)

                // filter by exact match
                : folderNameToDateTimeMap
                    .Where(entry => Path.GetFileName(entry.Key) == expectedSegmentName)
                    .Select(entry => (entry.Key, entry.Value));

            // go deeper
            if (pathSegments.Count() > 1)
            {
                return folderCandidates.SelectMany(current =>
                    SimpleFileDataSource.GetCandidateFolders(
                        current.Key,
                        current.Value,
                        begin,
                        end,
                        pathSegments.Skip(1).ToList(), cancellationToken
                    )
                );
            }

            // we have reached the most nested folder level
            else
            {
                return folderCandidates;
            }
        }

        private static IEnumerable<(string Key, DateTime Value)> FilterBySearchDate(DateTime begin,
                                                                                    DateTime end,
                                                                                    Dictionary<string, DateTime> folderNameToDateTimeMap,
                                                                                    string expectedSegmentName)
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

        #endregion
    }
}
