﻿using System.Text.RegularExpressions;

namespace Nexus.DataModel
{
    /// <summary>
    /// Contains extension methods to make life easier working with the data model types.
    /// </summary>
    public static partial class DataModelExtensions
    {
        #region Fluent API

        internal const string Description = "Description";
        internal const string Warning = "Warning";
        internal const string Unit = "Unit";
        internal const string Groups = "Groups";
        internal const string BasePath = "BasePath";

        /// <summary>
        /// Adds a description.
        /// </summary>
        /// <param name="catalogBuilder">The catalog builder.</param>
        /// <param name="description">The description to add.</param>
        /// <returns>A resource catalog builder.</returns>
        public static ResourceCatalogBuilder WithDescription(this ResourceCatalogBuilder catalogBuilder, string description)
        {
            return catalogBuilder.WithProperty(Description, description);
        }

        /// <summary>
        /// Adds a unit.
        /// </summary>
        /// <param name="resourceBuilder">The resource builder.</param>
        /// <param name="unit">The unit to add.</param>
        /// <returns>A resource builder.</returns>
        public static ResourceBuilder WithUnit(this ResourceBuilder resourceBuilder, string unit)
        {
            return resourceBuilder.WithProperty(Unit, unit);
        }

        /// <summary>
        /// Adds a description.
        /// </summary>
        /// <param name="resourceBuilder">The resource builder.</param>
        /// <param name="description">The description to add.</param>
        /// <returns>A resource builder.</returns>
        public static ResourceBuilder WithDescription(this ResourceBuilder resourceBuilder, string description)
        {
            return resourceBuilder.WithProperty(Description, description);
        }

        /// <summary>
        /// Adds a warning.
        /// </summary>
        /// <param name="resourceBuilder">The resource builder.</param>
        /// <param name="warning">The warning to add.</param>
        /// <returns>A resource builder.</returns>
        public static ResourceBuilder WithWarning(this ResourceBuilder resourceBuilder, string warning)
        {
            return resourceBuilder.WithProperty(Warning, warning);
        }

        /// <summary>
        /// Adds groups.
        /// </summary>
        /// <param name="resourceBuilder">The resource builder.</param>
        /// <param name="groups">The groups to add.</param>
        /// <returns>A resource builder.</returns>
        public static ResourceBuilder WithGroups(this ResourceBuilder resourceBuilder, params string[] groups)
        {
            return resourceBuilder.WithGroups((IEnumerable<string>)groups);
        }

        /// <summary>
        /// Adds groups.
        /// </summary>
        /// <param name="resourceBuilder">The resource builder.</param>
        /// <param name="groups">The groups to add.</param>
        /// <returns>A resource builder.</returns>
        public static ResourceBuilder WithGroups(this ResourceBuilder resourceBuilder, IEnumerable<string> groups)
        {
            var counter = 0;

            foreach (var group in groups)
            {
                resourceBuilder.WithProperty($"{Groups}:{counter}", group);
                counter++;
            }

            return resourceBuilder;
        }

        #endregion

        #region Misc

        /// <summary>
        /// Converts a url into a local file path.
        /// </summary>
        /// <param name="url">The url to convert.</param>
        /// <returns>The local file path.</returns>
        public static string ToPath(this Uri url)
        {
            var isRelativeUri = !url.IsAbsoluteUri;

            if (isRelativeUri)
                return url.ToString();

            else if (url.IsFile)
                return url.LocalPath.Replace('\\', '/');

            else
                throw new Exception("Only a file URI can be converted to a path.");
        }

        private const int NS_PER_TICK = 100;
        private static long[] _nanoseconds = new[] { (long)1e0, (long)1e3, (long)1e6, (long)1e9, (long)60e9 };
        private static int[] _quotients = new[] { 1000, 1000, 1000, 60, 1 };
        private static string[] _postFixes = new[] { "ns", "us", "ms", "s", "min" };
        private static Regex _unitStringEvaluator = new Regex(@"^([0-9]+)_([a-z]+)$", RegexOptions.Compiled);

        /// <summary>
        /// Converts period into a human readable number string with unit.
        /// </summary>
        /// <param name="samplePeriod">The period to convert.</param>
        /// <returns>The human readable number string with unit.</returns>
        public static string ToUnitString(this TimeSpan samplePeriod)
        {
            var currentValue = samplePeriod.Ticks * NS_PER_TICK;

            for (int i = 0; i < _postFixes.Length; i++)
            {
                var quotient = Math.DivRem(currentValue, _quotients[i], out var remainder);

                if (remainder != 0)
                    return $"{currentValue}_{_postFixes[i]}";

                else
                    currentValue = quotient;
            }

            return $"{(int)currentValue}_{_postFixes.Last()}";
        }

        // this method is placed here because it requires access to _postFixes and _nanoseconds
        internal static TimeSpan ToSamplePeriod(string unitString)
        {
            var match = _unitStringEvaluator.Match(unitString);

            if (!match.Success)
                throw new Exception("The provided unit string is invalid.");

            var unitIndex = Array.IndexOf(_postFixes, match.Groups[2].Value);

            if (unitIndex == -1)
                throw new Exception("The provided unit is invalid.");

            var totalNanoSeconds = long.Parse(match.Groups[1].Value) * _nanoseconds[unitIndex];

            if (totalNanoSeconds % NS_PER_TICK != 0)
                throw new Exception("The sample period must be a multiple of 100 ns.");

            var ticks = totalNanoSeconds / NS_PER_TICK;

            return new TimeSpan(ticks);
        }

        #endregion
    }
}
