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
    }
}