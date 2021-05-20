using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Infrastructure;
using Nexus.Services;
using Nexus.Types;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

namespace Nexus.Extensions
{
    [ExtensionIdentification("Nexus.Aggregation", "Nexus Aggregation", "Provides access to databases with Nexus aggregation files.")]
    public class AggregationDataSource : DataReaderExtensionBase
    {
        #region Constructors

        public AggregationDataSource(DataSourceRegistration registration, ILogger logger) : base(registration, logger)
        {
            //
        }

        #endregion

        #region Properties

        public FileAccessManager FileAccessManager { get; set; }

        #endregion

        #region Methods

#warning Unify this with other readers
        public override (T[] Dataset, byte[] Status) ReadSingle<T>(Dataset dataset, DateTime begin, DateTime end)
        {
            var project = dataset.Channel.Project;
            var channel = dataset.Channel;
            var projectFolderPath = Path.Combine(this.RootPath, "DATA", WebUtility.UrlEncode(project.Id));
            var samplesPerDay = new SampleRateContainer(dataset.Id).SamplesPerDay;
            var length = (long)Math.Round((end - begin).TotalDays * samplesPerDay, MidpointRounding.AwayFromZero);
            var data = new T[length];
            var status = new byte[length];

            if (!Directory.Exists(projectFolderPath))
                return (data, status);

            var periodPerFile = TimeSpan.FromDays(1);

            // read data
            var currentBegin = begin.RoundDown(periodPerFile);
            var fileLength = (int)Math.Round(periodPerFile.TotalDays * samplesPerDay, MidpointRounding.AwayFromZero);
            var fileOffset = (int)Math.Round((begin - currentBegin).TotalDays * samplesPerDay, MidpointRounding.AwayFromZero);
            var bufferOffset = 0;
            var remainingBufferLength = (int)length;

            while (remainingBufferLength > 0)
            {
                var filePath = Path.Combine(
                    projectFolderPath, 
                    currentBegin.ToString("yyyy-MM"), 
                    currentBegin.ToString("dd"),
                    $"{channel.Id}_{dataset.Id.Replace(" ", "_")}.nex");

                var fileBlock = fileLength - fileOffset;
                var currentBlock = Math.Min(remainingBufferLength, fileBlock);

                if (File.Exists(filePath))
                {
                    try
                    {
                        this.FileAccessManager?.Register(filePath, CancellationToken.None);
                        var aggregationData = AggregationFile.Read<T>(filePath);

                        // write data
                        if (aggregationData.Length == fileLength)
                        {
                            aggregationData.Slice(fileOffset, currentBlock).CopyTo(data.AsSpan(bufferOffset));
                            status.AsSpan(bufferOffset, currentBlock).Fill(1);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.Logger.LogWarning($"Could not process file '{filePath}'. Reason: {ex.Message}");
                    }
                    finally
                    {
                        this.FileAccessManager?.Unregister(filePath);
                    }
                }

                // update loop state
                fileOffset = 0; // Only the data in the first file may have an offset.
                bufferOffset += currentBlock;
                remainingBufferLength -= currentBlock;
                currentBegin += periodPerFile;
            }

            return (data, status);
        }

        protected override List<Project> LoadProjects()
        {
            // (0) load versioning file
            var versioningFilePath = Path.Combine(this.RootPath, "versioning.json");

            var versioning = File.Exists(versioningFilePath)
                ? AggregationVersioning.Load(versioningFilePath)
                : new AggregationVersioning();

            // (1) find beginning of database
            var dataFolderPath = Path.Combine(this.RootPath, "DATA");
            Directory.CreateDirectory(dataFolderPath);

            var firstMonth = DateTime.MaxValue;

            foreach (var projectDirectory in Directory.EnumerateDirectories(dataFolderPath))
            {
                var projectFirstMonth = this.GetProjectFirstMonthWithData(projectDirectory);

                if (projectFirstMonth != DateTime.MinValue && projectFirstMonth < firstMonth)
                    firstMonth = projectFirstMonth;
            }

            // (2) for each month
            var now = DateTime.UtcNow;
            var months = ((now.Year - firstMonth.Year) * 12) + now.Month - firstMonth.Month + 1;
            var currentMonth = firstMonth;

            var cacheFolderPath = Path.Combine(this.RootPath, "CACHE");
            var mainCacheFilePath = Path.Combine(cacheFolderPath, "main.json");
            Directory.CreateDirectory(cacheFolderPath);

            bool cacheChanged = false;

            for (int i = 0; i < months; i++)
            {
                // (3) find available project ids
                var projectIds = Directory
                    .EnumerateDirectories(dataFolderPath)
                    .Select(current => WebUtility.UrlDecode(Path.GetFileName(current)))
                    .ToList();

                // (4) find corresponding cache file
                var cacheFilePath = Path.Combine(cacheFolderPath, $"{currentMonth.ToString("yyyy-MM")}.json");
               
                List<Project> cache;

                // (5.a) cache file exists
                if (File.Exists(cacheFilePath))
                {
                    cache = JsonSerializerHelper.Deserialize<List<Project>>(cacheFilePath);
                    cache.ForEach(project => project.Initialize());

                    foreach (var projectId in projectIds)
                    {
                        var project = cache.FirstOrDefault(project => project.Id == projectId);
                        var currentMonthFolder = Path.Combine(dataFolderPath, WebUtility.UrlEncode(projectId), currentMonth.ToString("yyyy-MM"));

                        // project is in cache ...
                        if (project != null)
                        {
                            // ... but cache is outdated
                            if (this.IsCacheOutdated(projectId, currentMonthFolder, versioning))
                            {
                                project = this.ScanFiles(projectId, currentMonthFolder, versioning);
                                cacheChanged = true;
                            }
                        }
                        // project is not in cache
                        else
                        {
                            project = this.ScanFiles(projectId, currentMonthFolder, versioning);
                            cache.Add(project);
                            cacheChanged = true;
                        }
                    }
                }
                // (5.b) cache file does not exist
                else
                {
                    cache = projectIds.Select(projectId =>
                    {
                        var currentMonthFolder = Path.Combine(dataFolderPath, WebUtility.UrlEncode(projectId), currentMonth.ToString("yyyy-MM"));
                        var project = this.ScanFiles(projectId, currentMonthFolder, versioning);
                        cacheChanged = true;
                        return project;
                    }).ToList();
                }

                // (6) save cache and versioning files
                if (cacheChanged)
                {
                    JsonSerializerHelper.Serialize(cache, cacheFilePath);
                    JsonSerializerHelper.Serialize(versioning, versioningFilePath);
                }

                currentMonth = currentMonth.AddMonths(1);
            }

            // (7) update main cache
            List<Project> mainCache;

            if (cacheChanged || !File.Exists(mainCacheFilePath))
            {
                var cacheFiles = Directory.EnumerateFiles(cacheFolderPath, "*-*.json");
                mainCache = new List<Project>();

                var message = "Merging cache files into main cache ...";

                try
                {
                    this.Logger.LogInformation(message);

                    foreach (var cacheFile in cacheFiles)
                    {
                        var cache = JsonSerializerHelper.Deserialize<List<Project>>(cacheFile);
                        cache.ForEach(project => project.Initialize());

                        foreach (var project in cache)
                        {
                            var reference = mainCache.FirstOrDefault(current => current.Id == project.Id);

                            if (reference != null)
                                reference.Merge(project, ChannelMergeMode.NewWins);
                            else
                                mainCache.Add(project);
                        }
                    }

                    this.Logger.LogInformation($"{message} Done.");
                }
                catch (Exception ex)
                {
                    this.Logger.LogError($"{message} Error: {ex.GetFullMessage()}");
                    throw;
                }

                JsonSerializerHelper.Serialize(mainCache, mainCacheFilePath);
            }
            else
            {
                mainCache = JsonSerializerHelper.Deserialize<List<Project>>(mainCacheFilePath);
                mainCache.ForEach(project => project.Initialize());
            }

            // update project start and end
            foreach (var project in mainCache)
            {
                var projectFolderPath = Path.Combine(dataFolderPath, WebUtility.UrlEncode(project.Id));
                var projectFirstMonth = this.GetProjectFirstMonthWithData(projectFolderPath);
                var currentMonthFolder = Path.Combine(dataFolderPath, WebUtility.UrlEncode(project.Id), projectFirstMonth.ToString("yyyy-MM"));
                var folders = Directory.EnumerateDirectories(currentMonthFolder);
                var firstDateTime = this.GetFirstDateTime(folders);

                project.ProjectStart = firstDateTime;
                project.ProjectEnd = versioning.ScannedUntilMap[project.Id];
            }

            return mainCache;
        }

        protected override double GetAvailability(string projectId, DateTime day)
        {
            if (!this.Projects.Any(project => project.Id == projectId))
                throw new Exception($"The project '{projectId}' could not be found.");

            var folderPath = Path.Combine(
                this.RootPath, 
                "DATA", 
                WebUtility.UrlEncode(projectId),
                day.ToString("yyyy-MM"),
                day.ToString("dd")
            );

            return Directory.Exists(folderPath) ? 1 : 0;
        }

        private bool IsCacheOutdated(string projectId, string monthFolder, AggregationVersioning versioning)
        {
            if (Directory.Exists(monthFolder))
            {
                var folders = Directory
               .EnumerateDirectories(monthFolder);

                var lastDateTime = this.GetLastDateTime(folders);
                return lastDateTime > versioning.ScannedUntilMap[projectId];
            }
            else
            {
                return false;
            }
        }

        private Project ScanFiles(string projectId, string currentMonthFolder, AggregationVersioning versioning)
        {
            var project = new Project(projectId);

            if (Directory.Exists(currentMonthFolder))
            {
                var message = $"Scanning files for {Path.GetFileName(currentMonthFolder)} ...";
                this.Logger.LogInformation(message);

                var dayFolders = Directory.EnumerateDirectories(currentMonthFolder);

                try
                {
                    foreach (var dayFolder in dayFolders)
                    {
                        var newProject = this.GetProject(projectId, dayFolder);
                        project.Merge(newProject, ChannelMergeMode.NewWins);
                    }

                    // update scanned until
                    var scannedUntil = this.GetLastDateTime(dayFolders);

                    if (versioning.ScannedUntilMap.TryGetValue(projectId, out var value))
                    {
                        if (scannedUntil > value)
                            versioning.ScannedUntilMap[projectId] = scannedUntil;
                    }
                    else
                    {
                        versioning.ScannedUntilMap[projectId] = scannedUntil;
                    }

                    this.Logger.LogInformation($"{message} Done.");
                }
                catch (Exception ex)
                {
                    this.Logger.LogError($"{message} Error: {ex.GetFullMessage()}");
                }
            }

            return project;
        }

        private DateTime GetProjectFirstMonthWithData(string projectFolderPath)
        {
            var monthFolders = Directory
                    .EnumerateDirectories(projectFolderPath);

            if (monthFolders.Any())
            {
                var result = monthFolders
                    // convert path to date/time
                    .Select(monthDirectory =>
                    {
                        var dateTime = DateTime.ParseExact(
                            Path.GetFileName(monthDirectory),
                            "yyyy-MM",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
                        );

                        return (monthDirectory, dateTime);
                    })
                    // order by date/time
                    .OrderBy(value => value.dateTime)
                    // find first folder that contains files
                    .FirstOrDefault(value => Directory.EnumerateFiles(value.monthDirectory, "*", SearchOption.AllDirectories).Any())
                    // return only date/time
                    .dateTime;

                return result == default 
                    ? DateTime.MaxValue 
                    : result;
            }
            else
            {
                return DateTime.MaxValue;
            }
        }

        private DateTime GetFirstDateTime(IEnumerable<string> dayFolders)
        {
            if (dayFolders.Any())
            {
                return dayFolders
                   .Select(dayFolder => DateTime.ParseExact(
                       $"{dayFolder.Substring(dayFolder.Length - 10).Remove(7, 1).Insert(7, "-")}",
                       "yyyy-MM-dd",
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
                   )
                   .OrderBy(value => value)
                   .First();
            }
            else
            {
                return DateTime.MaxValue;
            }
        }

        private DateTime GetLastDateTime(IEnumerable<string> dayFolders)
        {
            if (dayFolders.Any())
            {
                return dayFolders
                   .Select(dayFolder => DateTime.ParseExact(
                       $"{dayFolder.Substring(dayFolder.Length - 10).Remove(7, 1).Insert(7, "-")}",
                       "yyyy-MM-dd",
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
                   )
                   .OrderBy(value => value)
                   .Last();
            }
            else
            {
                return DateTime.MinValue;
            }
        }

        private Project GetProject(string projectId, string dayFolder)
        {
            var project = new Project(projectId);
            var channelMap = new Dictionary<Guid, Channel>();

            Directory
                .EnumerateFiles(dayFolder, "*.nex", SearchOption.TopDirectoryOnly)
                .ToList()
                .ForEach(filePath =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    var fileNameParts = fileName.Split('_');
                    var id = Guid.Parse(fileNameParts[0]);
                    var datasetName = $"{fileNameParts[1]} {fileNameParts[2]}_{fileNameParts[3]}";

                    if (!channelMap.TryGetValue(id, out var channel))
                    {
                        channel = new Channel(id, project);
                        channelMap[id] = channel;
                    }

                    var dataset = new Dataset(datasetName, channel)
                    {
                        DataType = NexusDataType.FLOAT64
                    };

                    channel.Datasets.Add(dataset);
                });

            project.Channels.AddRange(channelMap.Values.ToList());

            return project;
        }

        #endregion
    }
}
