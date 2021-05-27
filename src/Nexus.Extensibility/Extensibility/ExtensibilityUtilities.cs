using Nexus.DataModel;
using Nexus.Infrastructure;
using System;
using System.Text.RegularExpressions;

namespace Nexus.Extensibility
{
    public static class ExtensibilityUtilities
    {
        public static ReadResult<T> CreateReadResult<T>(Dataset dataset, DateTime begin, DateTime end)
        {
            var samplesPerDay = new SampleRateContainer(dataset.Id).SamplesPerDay;
            var length = (int)Math.Round((end - begin).TotalDays * samplesPerDay, MidpointRounding.AwayFromZero);

            return new ReadResult<T>(length);
        }

        public static string EnforceNamingConvention(string value, string prefix = "X")
        {
            if (string.IsNullOrWhiteSpace(value))
                value = "unnamed";

            value = Regex.Replace(value, "[^A-Za-z0-9_]", "_");

            if (Regex.IsMatch(value, "^[0-9_]"))
                value = $"{prefix}_" + value;

            return value;
        }

        internal static DateTime RoundDown(DateTime dateTime, TimeSpan timeSpan)
        {
            return new DateTime(dateTime.Ticks - (dateTime.Ticks % timeSpan.Ticks), dateTime.Kind);
        }
    }
}