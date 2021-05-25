using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public abstract class SimpleFileDataSource : IDataSource
    {
        // The algorithms assume the following:
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
        // (3) The files are always located in the most nested folder and not distributed
        // over the hierarchy.

        #region Fields

        private FileSystemDescription _configuration;
        private List<Project> _projects;

        #endregion

        #region Properties

        public string RootPath { get; set; }

        public ILogger Logger { get; set; }

        public Dictionary<string, string> Options { get; set; }

        public IReadOnlyList<Project> Projects => _projects;

        #endregion

        #region Methods

        public Project GetProject(string projectId)
        {
            return this.Projects.First(project => project.Id == projectId);
        }

        public abstract Task<(FileSystemDescription, List<Project>)> InitializeDataModelAsync();

        public virtual void AssignProjectLifetime(Project project)
        {
            if (!_configuration.Projects.TryGetValue(project.Id, out var sources))
                throw new Exception($"A configuration for the project '{project.Id}' could not be found.");

            var minDate = DateTime.MaxValue;
            var maxDate = DateTime.MinValue;

            if (Directory.Exists(this.RootPath))
            {
                foreach (var source in sources)
                {
                    // first
                    var firstDateTime = SimpleFileDataSource
                        .GetCandidateDateTimes(source.Value, DateTime.MinValue, this.RootPath)
                        .OrderBy(current => current)
                        .FirstOrDefault();

                    if (firstDateTime == default)
                        firstDateTime = DateTime.MaxValue;

                    if (firstDateTime.Date < minDate)
                        minDate = firstDateTime.Date;

                    // last
                    var lastDateTime = SimpleFileDataSource
                        .GetCandidateDateTimes(source.Value, DateTime.MaxValue, this.RootPath)
                        .OrderByDescending(current => current)
                        .FirstOrDefault();

                    if (lastDateTime == default)
                        lastDateTime = DateTime.MinValue;

                    if (lastDateTime.Date > maxDate)
                        maxDate = lastDateTime.Date;
                }
            }

            project.ProjectStart = minDate;
            project.ProjectEnd = maxDate;
        }

        #endregion

        #region IDataSource

        public async Task<List<Project>> InitializeAsync()
        {
            (_configuration, _projects) = await this.InitializeDataModelAsync();

            foreach (var project in _projects)
            {
                this.AssignProjectLifetime(project);
            }

            return _projects;
        }

        public virtual Task<double> GetAvailabilityAsync(string projectId, DateTime day)
        {
            if (!_configuration.Projects.TryGetValue(projectId, out var sources))
                throw new Exception($"A configuration for the project '{projectId}' could not be found.");

            // no true async file enumeration available: https://github.com/dotnet/runtime/issues/809
            return Task.Run(() =>
            {
                if (!Directory.Exists(this.RootPath))
                    return 0;

                var summedAvailability = sources.Values.Sum(source =>
                {
                    var candidateDateTimes = SimpleFileDataSource.GetCandidateDateTimes(source, day, this.RootPath);

                    var fileCount = candidateDateTimes
                        .Where(current => day <= current && current < day.AddDays(1))
                        .Count();

                    var filesPerDay = TimeSpan.FromDays(1).Ticks / source.FilePeriod.Ticks;

                    return fileCount / (double)filesPerDay;
                });

                return summedAvailability / sources.Count;
            });
        }

        public abstract Task<ReadResult<T>> ReadSingleAsync<T>(Dataset dataset, DateTime begin, DateTime end) 
            where T : unmanaged;

        #endregion

        #region Helpers

        private static IEnumerable<DateTime> GetCandidateDateTimes(SourceDescription source, DateTime searchDate, string rootPath)
        {
            // (could also be named "GetCandidateFiles", since it is similar to the "GetCandidateFolders" method)

            // initial check
            if (!Directory.Exists(rootPath))
                return new List<DateTime>();

            // get all candidate folders
            var candidateFolders = SimpleFileDataSource
                .GetCandidateFolders(searchDate, rootPath, default, source.PathSegments);

            // get all files that can be parsed
            var regex = source.PathPreselectors is not null
                    ? new Regex(source.PathPreselectors.Last())
                    : null;

            var regexString = source.PathPreselectors?.Last();
            var fileNameTemplate = source.PathSegments.Last();

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
                            fileNameTemplate,
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
                            return searchDate == DateTime.MinValue
                                ? DateTime.MaxValue  // searchDate == DateTime.MinValue
                                : DateTime.MinValue; // searchDate == DateTime.MaxValue or searchDate == every other date
                        }
                    })
                    // searchDate == DateTime.MinValue: do not allow default values to propagate
                    // searchDate == DateTime.MaxValue: it does not matter if DateTime.MinValue or empty collection is returned
                    // searchDate == every other date:  it doesn't matter if default values are removed
                    .Where(current => current != default);

                return candidateDateTimes;
            });
        }

        private static IEnumerable<(string FolderPath, DateTime DateTime)> GetCandidateFolders(DateTime searchDate, string root, DateTime rootDate, IEnumerable<string> pathSegments)
        {
            // get all available folders
            var folderPaths = Directory
                .EnumerateDirectories(root)
                .ToList();

            // get all folders that can be parsed
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

                    return (folderPath, parsedDateTime);
                })
               .ToDictionary(current => current.folderPath, current => current.parsedDateTime);

            // keep only folders that fall within the searched time period
            IEnumerable<(string Key, DateTime Value)> folderCandidates;

            if (searchDate == DateTime.MinValue)
            {
                var folderCandidate = folderNameToDateTimeMap
                    .OrderBy(entry => entry.Value)
                    .FirstOrDefault();

                folderCandidates = new List<(string, DateTime)>() { (folderCandidate.Key, folderCandidate.Value) };
            }

            else if (searchDate == DateTime.MaxValue)
            {
                var folderCandidate = folderNameToDateTimeMap
                   .OrderByDescending(entry => entry.Value)
                   .FirstOrDefault();

                folderCandidates = new List<(string, DateTime)>() { (folderCandidate.Key, folderCandidate.Value) };
            }

            else
            {
                var expectedSegmentName = searchDate.ToString(pathSegments.First());

                folderCandidates = folderNameToDateTimeMap
                    .Where(entry =>
                    {
                        // Check for the case that the parsed date/time(2020-01-01T22) is
                        // more specific than the search date/time (2020-01-01T00):
                        if (searchDate <= entry.Value && entry.Value <= searchDate.AddDays(1))
                            return true;

                        // Check for the case that the parsed date/time (2020-01) is less 
                        // specific than the search date/time (2020-01-02)
                        else
                            return Path.GetFileName(entry.Key) == expectedSegmentName;
                    })
                    .Select(entry => (entry.Key, entry.Value));
            }

            // we have reached the most nested folder level
            if (pathSegments.Count() == 2)
            {
                return folderCandidates;
            }

            // go deeper
            else
            {
                return folderCandidates.SelectMany(current =>
                    SimpleFileDataSource.GetCandidateFolders(
                        searchDate,
                        current.Key,
                        current.Value,
                        pathSegments.Skip(1)
                    )
                );
            }
        }

        #endregion
    }
}
