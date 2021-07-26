﻿using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nexus.Utilities
{
    internal static class NexusCoreUtilities
    {
        public static TimeSpan ValueAndUnitToSamplePeriod(long value, string unit)
        {
            var ticksPerUnit = unit switch
            {
                 "ns"   => 0.01,
                 "us"   => 10,
                 "ms"   => 10_000,
                  "s"   => 10_000_000,
                 "Hz"   => 10_000_000,
                "min"   => 600_000_000,
                _       => throw new Exception($"The unit {unit} is not supported.")
            };

            if (unit == "Hz")
                return TimeSpan.FromTicks((long)(ticksPerUnit / value));

            else
                return TimeSpan.FromTicks((long)(value * ticksPerUnit));
        }

        public static async Task FileLoopAsync(
            DateTime begin,
            DateTime end,
            TimeSpan filePeriod, Func<DateTime, TimeSpan, TimeSpan, Task> func)
        {
            var lastFileBegin = default(DateTime);
            var currentBegin = begin;
            var remainingPeriod = end - begin;

            while (remainingPeriod > TimeSpan.Zero)
            {
                DateTime fileBegin;

                if (filePeriod == TimeSpan.Zero)
                    fileBegin = lastFileBegin != DateTime.MinValue ? lastFileBegin : begin;

                else
                    fileBegin = currentBegin.RoundDown(filePeriod);

                lastFileBegin = fileBegin;

                var fileOffset = currentBegin - fileBegin;
                var remainingFilePeriod = filePeriod - fileOffset;
                var duration = TimeSpan.FromTicks(Math.Min(remainingFilePeriod.Ticks, remainingPeriod.Ticks));

                await func.Invoke(fileBegin, fileOffset, duration);

                // update loop state
                currentBegin += duration;
                remainingPeriod -= duration;
            }
        }

        public static async ValueTask<T[]> WhenAll<T>(params ValueTask<T>[] tasks)
        {
            List<Exception>? exceptions = null;

            var results = new T[tasks.Length];

            for (var i = 0; i < tasks.Length; i++)
            {
                try
                {
                    results[i] = await tasks[i].ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    exceptions ??= new List<Exception>(tasks.Length);
                    exceptions.Add(ex);
                }
            }

            return exceptions is null
                ? results
                : throw new AggregateException(exceptions);
        }

        public static Type GetTypeFromNexusDataType(NexusDataType dataType)
        {
            return dataType switch
            {
                NexusDataType.UINT8 => typeof(Byte),
                NexusDataType.INT8 => typeof(SByte),
                NexusDataType.UINT16 => typeof(UInt16),
                NexusDataType.INT16 => typeof(Int16),
                NexusDataType.UINT32 => typeof(UInt32),
                NexusDataType.INT32 => typeof(Int32),
                NexusDataType.UINT64 => typeof(UInt64),
                NexusDataType.INT64 => typeof(Int64),
                NexusDataType.FLOAT32 => typeof(Single),
                NexusDataType.FLOAT64 => typeof(Double),
                _ => throw new NotSupportedException($"The specified data type '{dataType}' is not supported.")
            };
        }

        public static double ToUnixTimeStamp(this DateTime value)
        {
            return value.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        }

        public static int SizeOf(NexusDataType dataType)
        {
            return NexusCoreUtilities.SizeOf(NexusCoreUtilities.GetTypeFromNexusDataType(dataType));
        }

        public static int SizeOf(Type type)
        {
            if (type == typeof(bool))
                return 1;
            else
                return Marshal.SizeOf(type);
        }

        public static T GetFirstAttribute<T>(this Type type) where T : Attribute
        {
            return type.GetCustomAttributes(false).OfType<T>().FirstOrDefault();
        }

        public static bool CheckCatalogNamingConvention(string value, out string errorDescription, bool includeValue = false)
        {
            var valueAsString = string.Empty;

            if (includeValue)
                valueAsString = $" (value: '{value}')";

            errorDescription = true switch
            {
                true when !value.StartsWith('/') => $"{ErrorMessage.NexusUtilities_InvalidLeadingCharacter2}{valueAsString}",
                true when value.Split('/').Count() != 4 => $"{ErrorMessage.NexusUtilities_InvalidPathSeparatorCount}{valueAsString}",
                _ => string.Empty
            };

            return string.IsNullOrWhiteSpace(errorDescription);
        }

        public static bool CheckNamingConvention(string value, out string errorDescription, bool includeValue = false)
        {
            var valueAsString = string.Empty;

            if (includeValue)
                valueAsString = $" (value: '{value}')";

            errorDescription = true switch
            {
                true when string.IsNullOrWhiteSpace(value)      => $"{ErrorMessage.NexusUtilities_NameEmpty}{valueAsString}",
                true when Regex.IsMatch(value, "[^A-Za-z0-9_]") => $"{ErrorMessage.NexusUtilities_InvalidCharacters}{valueAsString}",
                true when Regex.IsMatch(value, "^[0-9_]")       => $"{ErrorMessage.NexusUtilities_InvalidLeadingCharacter}{valueAsString}",
                _                                               => string.Empty
            };

            return string.IsNullOrWhiteSpace(errorDescription);
        }

        public static object InvokeGenericMethod<T>(T instance, string methodName, BindingFlags bindingFlags, Type genericType, object[] parameters)
        {
            return NexusCoreUtilities.InvokeGenericMethod(typeof(T), instance, methodName, bindingFlags, genericType, parameters);
        }

        public static object InvokeGenericMethod(Type methodParent, object instance, string methodName, BindingFlags bindingFlags, Type genericType, object[] parameters)
        {
            var methodInfo = methodParent
                .GetMethods(bindingFlags)
                .Where(methodInfo => methodInfo.IsGenericMethod && methodInfo.Name == methodName)
                .First();

            var genericMethodInfo = methodInfo.MakeGenericMethod(genericType);

            return genericMethodInfo.Invoke(instance, parameters);
        }
    }
}