﻿using Nexus.DataModel;
using System;
using System.Buffers;

namespace Nexus.Extensibility
{
    public static class ExtensibilityUtilities
    {
        public static (Memory<byte>, Memory<byte>) CreateBuffers(Representation representation, DateTime begin, DateTime end)
        {
            var elementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, representation.SamplePeriod);

            var dataOwner = MemoryPool<byte>.Shared.Rent(elementCount * representation.ElementSize);
            var data = dataOwner.Memory.Slice(0, elementCount * representation.ElementSize);
            data.Span.Clear();

            var statusOwner = MemoryPool<byte>.Shared.Rent(elementCount);
            var status = statusOwner.Memory.Slice(0, elementCount);
            status.Span.Clear();

            return (data, status);
        }

        internal static int CalculateElementCount(DateTime begin, DateTime end, TimeSpan samplePeriod)
        {
            return (int)((end.Ticks - begin.Ticks) / samplePeriod.Ticks);
        }

        internal static DateTime RoundDown(DateTime dateTime, TimeSpan timeSpan)
        {
            return new DateTime(dateTime.Ticks - (dateTime.Ticks % timeSpan.Ticks), dateTime.Kind);
        }
    }
}