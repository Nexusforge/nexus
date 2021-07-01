using Nexus.Utilities;
using System;

namespace Nexus
{
    public static class CustomExtensions
    {
        public static Memory<T> Cast<T>(this Memory<byte> buffer)
            where T : unmanaged
        {
            return new CastMemoryManager<byte, T>(buffer).Memory;
        }

        public static Memory<T> Cast<T>(this ReadOnlyMemory<byte> buffer)
            where T : unmanaged
        {
            return new ReadonlyCastMemoryManager<byte, T>(buffer).Memory;
        }

        public static string ToISO8601(this DateTime dateTime)
        {
            return dateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH-mm-ssZ");
        }

        public static DateTime RoundDown(this DateTime dateTime, TimeSpan timeSpan)
        {
            return new DateTime(dateTime.Ticks - (dateTime.Ticks % timeSpan.Ticks), dateTime.Kind);
        }

        public static string GetFullMessage(this Exception ex, bool includeStackTrace = true)
        {
            if (includeStackTrace)
                return $"{ex.InternalGetFullMessage()} - stack trace: {ex.StackTrace}";
            else
                return ex.InternalGetFullMessage();
        }

        private static string InternalGetFullMessage(this Exception ex)
        {
            return ex.InnerException == null
                 ? ex.Message
                 : ex.Message + " --> " + ex.InnerException.GetFullMessage();
        }
    }
}
