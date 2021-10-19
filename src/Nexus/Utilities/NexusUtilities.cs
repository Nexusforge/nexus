using System;

namespace Nexus.Utilities
{
    internal static class NexusUtilities
    {
        public static string GetEnumLocalization(Enum enumValue)
        {
            return EnumerationDescription.ResourceManager.GetString(enumValue.GetType().Name + "_" + enumValue.ToString());
        }

        public static string GetEnumIconName(Enum enumValue)
        {
            return EnumerationIconName.ResourceManager.GetString(enumValue.GetType().Name + "_" + enumValue.ToString());
        }

        public static string FormatByteCount(long byteCount)
        {
            if (byteCount >= 1000 * 1000 * 1000)
                return $"{(double)byteCount / 1000 / 1000 / 1000:G3} GB";
            else if (byteCount >= 1000 * 1000)
                return $"{(double)byteCount / 1000 / 1000:G3} MB";
            else if (byteCount >= 1000)
                return $"{(double)byteCount / 1000:G3} kB";
            else
                return $"{(double)byteCount:F0} B";
        }
    }
}
