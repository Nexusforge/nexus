using System;
using System.Text.RegularExpressions;

namespace Nexus.Extensibility
{
    public static class ExtensibilityUtilities
    {
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