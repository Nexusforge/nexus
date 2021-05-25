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
                    if (this.TryGetDate(source.Value, first: true, out var firstDate))
                    {
                        if (firstDate < minDate)
                            minDate = firstDate;
                    }

                    if (this.TryGetDate(source.Value, first: false, out var lastDate))
                    {
                        if (lastDate > maxDate)
                            maxDate = lastDate;
                    }
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

                var summedAvailability = sources.Values.Sum(current =>
                {
                    var fileCount = 0;
                    var searchPattern = day.ToString(current.DailyFilesTemplate);

                    try
                    {
                        fileCount = SimpleFileDataSource
                            .GetAllMatchingPaths(this.RootPath, searchPattern)
                            .Count();
                    }
                    catch (DirectoryNotFoundException)
                    {
                        fileCount = 0;
                    }

                    var filesPerDay = TimeSpan.FromDays(1).Ticks / current.FilePeriod.Ticks;
                    return fileCount / (double)filesPerDay;
                });

                return summedAvailability / sources.Count;
            });
        }

        public abstract Task<ReadResult<T>> ReadSingleAsync<T>(Dataset dataset, DateTime begin, DateTime end) 
            where T : unmanaged;

        #endregion

        #region Helpers

        private bool TryGetDate(SourceDescription source, bool first, out DateTime date)
        {
            // (1) The following algorithm assumes that the top-most folders carry rough
            // date/time information while deeper nested folders carry more fine-grained
            // information. If the folder hierarchy is reversed, this algorithm will fail
            // to determine the date of the first or last file in the project.
            // (2) Further, the algorithm assumes that the files are always located in the
            // most nested folder and not distributed over the hierarchy.

            // initial check
            if (!Directory.Exists(this.RootPath))
            {
                date = DateTime.MinValue;
                return false;
            }

            // get all candidate folders
            var candidateFolders = SimpleFileDataSource
                .GetCandidateFolders(this.RootPath, default, source.PathSegments, first);

            // get all files that can be parsed
            var regex = source.PathPreselectors is not null
                    ? new Regex(source.PathPreselectors.Last())
                    : null;

            var regexString = source.PathPreselectors?.Last();
            var fileNameTemplate = source.PathSegments.Last();

            var dates = candidateFolders.SelectMany(currentFolder =>
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

                var candidateDates = matchedFiles
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
                            // use file date
                            if (parsedDateTime.Date != default)
                                return parsedDateTime.Date;

                            // use folder date
                            else
                                return currentFolder.Date;
                        }
                        // should never happen if file and folder templates are correct
                        else
                        {
                            return first
                                ? DateTime.MaxValue
                                : DateTime.MinValue;
                        }
                    })
                  .Where(current => current != default);

                // normal case
                if (candidateDates.Any())
                    return candidateDates;

                // i.e. when there are empty folders
                else
                    return new List<DateTime>() { currentFolder.Date };
            });

            // get first or last file of that list
            date = first
                ? dates.FirstOrDefault()
                : dates.LastOrDefault();

            return date != default;
        }

        private static IEnumerable<(string FolderPath, DateTime Date)> GetCandidateFolders(string root, DateTime rootDate, IEnumerable<string> pathSegments, bool first)
        {
            // get all available folders
            var canSortByDateTime = false;

            var folderPaths = Directory
                .EnumerateDirectories(root)
                .ToList();

            // get all folders that can be parsed
            var folderNameToDateTimeMap = folderPaths
                .Select(current =>
                {
                    var folderName = Path.GetFileName(current);

                    var success = DateTime
                        .TryParseExact(
                            folderName,
                            pathSegments.First(),
                            default,
                            DateTimeStyles.NoCurrentDateDefault,
                            out var parsedDateTime
                        );

                    if (success && parsedDateTime != default)
                        canSortByDateTime = true;

                    return (folderName, parsedDateTime);
                })
               .Where(current => current.parsedDateTime != default)
               .ToDictionary(current => current.folderName, current => current.parsedDateTime);

            // there is only a single folder we will continue with
            if (canSortByDateTime)
            {
                var candidate = first

                    ? folderNameToDateTimeMap
                        .OrderBy(current => current.Value)
                        .First()

                    : folderNameToDateTimeMap
                        .OrderBy(current => current.Value)
                        .Last();

                var newRoot = Path.Combine(root, candidate.Key);
                var newRootDate = candidate.Value.Date == default
                    ? rootDate
                    : candidate.Value.Date;

                // we have reached the most nested folder level
                if (pathSegments.Count() == 2)
                {
                    return new List<(string, DateTime)>() { (newRoot, newRootDate) };
                }

                // go deeper
                else
                {
                    return SimpleFileDataSource.GetCandidateFolders(
                        newRoot,
                        newRootDate, 
                        pathSegments.Skip(1),
                        first
                    );
                }
            }

            // all found folders need to be considered
            else
            {
                // we have reached the most nested folder level
                if (pathSegments.Count() == 2)
                {
                    return folderPaths.Select(current => (current, rootDate));
                }

                // go deeper
                else
                {
                    return folderPaths.SelectMany(current =>
                        SimpleFileDataSource.GetCandidateFolders(
                            current,
                            rootDate,
                            pathSegments.Skip(1),
                            first
                        )
                    );
                }
            }
        }

        // taken from https://stackoverflow.com/questions/36753047/multiple-wildcard-directory-file-search-for-arbitrary-directory-structure-in-c-s
        private static IEnumerable<string> GetAllMatchingPaths(string root, string pattern)
        {
            var parts = pattern.Split('\\', '/');

            for (int i = 0; i < parts.Length; i++)
            {
                // if this part of the path is a wildcard that needs expanding
                if (parts[i].Contains('*') || parts[i].Contains('?'))
                {
                    // create an absolute path up to the current wildcard and check if it exists
                    var combined = Path.Combine(root, string.Join("/", parts.Take(i)));

                    if (!Directory.Exists(combined))
                        return new string[0];

                    // if this is the end of the path (a file name)
                    if (i == parts.Length - 1)
                        return Directory.EnumerateFiles(combined, parts[i], SearchOption.TopDirectoryOnly);

                    // if this is in the middle of the path (a directory name)
                    else
                    {
                        var directories = Directory
                            .EnumerateDirectories(
                                combined,
                                parts[i],
                                SearchOption.TopDirectoryOnly
                            );

                        var paths = directories.SelectMany(current =>
                            SimpleFileDataSource.GetAllMatchingPaths(
                                current,
                                string.Join("/", parts.Skip(i + 1))
                            )
                        );

                        return paths;
                    }
                }
            }

            // if pattern ends in an absolute path with no wildcards in the filename
            var absolute = Path.Combine(root, string.Join("/".ToString(), parts));

            if (File.Exists(absolute))
                return new string[] { absolute };

            return new string[0];
        }

        #endregion
    }
}
