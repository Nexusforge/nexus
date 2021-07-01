using Nexus.DataModel;
using System;
using System.Buffers;
using System.Text.RegularExpressions;

namespace Nexus.Extensibility
{
    public static class ExtensibilityUtilities
    {
        public static (Memory<byte>, Memory<byte>) CreateBuffers(Dataset dataset, DateTime begin, DateTime end)
        {
            var elementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, dataset.GetSampleRate().Period);

            var dataOwner = MemoryPool<byte>.Shared.Rent(elementCount * dataset.ElementSize);
            var data = dataOwner.Memory.Slice(0, elementCount * dataset.ElementSize);
            data.Span.Clear();

            var statusOwner = MemoryPool<byte>.Shared.Rent(elementCount);
            var status = statusOwner.Memory.Slice(0, elementCount);
            status.Span.Clear();

            return (data, status);
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

        internal static int CalculateElementCount(DateTime begin, DateTime end, TimeSpan samplePeriod)
        {
            return (int)((end.Ticks - begin.Ticks) / samplePeriod.Ticks);
        }

        internal static DateTime RoundDown(DateTime dateTime, TimeSpan timeSpan)
        {
            return new DateTime(dateTime.Ticks - (dateTime.Ticks % timeSpan.Ticks), dateTime.Kind);
        }

        internal static string GetFullMessage(Exception ex, bool includeStackTrace = true)
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
                 : ex.Message + " --> " + ExtensibilityUtilities.GetFullMessage(ex.InnerException);
        }
    }
}