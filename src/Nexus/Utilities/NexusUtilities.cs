using Nexus.Core;
using Nexus.DataModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Nexus.Utilities
{
    internal static class NexusUtilities
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Scale(TimeSpan value, TimeSpan samplePeriod) => (int)(value.Ticks / samplePeriod.Ticks);

        public static List<T> GetEnumValues<T>() where T : Enum
        {
            return Enum.GetValues(typeof(T)).Cast<T>().ToList();
        }

        public static async Task FileLoopAsync(
            DateTime begin,
            DateTime end,
            TimeSpan filePeriod, 
            Func<DateTime, TimeSpan, TimeSpan, Task> func)
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

#pragma warning disable VSTHRD200 // Verwenden Sie das Suffix "Async" für asynchrone Methoden
        public static async ValueTask<T[]> WhenAll<T>(params ValueTask<T>[] tasks)
#pragma warning restore VSTHRD200 // Verwenden Sie das Suffix "Async" für asynchrone Methoden
        {
            List<Exception>? exceptions = default;

            var results = new T[tasks.Length];

            for (var i = 0; i < tasks.Length; i++)
            {
                try
                {
                    results[i] = await tasks[i];
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
                _ => throw new NotSupportedException($"The specified data type {dataType} is not supported.")
            };
        }

        public static int SizeOf(NexusDataType dataType)
        {
            return ((ushort)dataType & 0x00FF) / 8;
        }

        public static IEnumerable<T> GetCustomAttributes<T>(this Type type) where T : Attribute
        {
            return type.GetCustomAttributes(false).OfType<T>();
        }

        public static object? InvokeGenericMethod<T>(T instance, string methodName, BindingFlags bindingFlags, Type genericType, object[] parameters)
        {
            if (instance is null)
                throw new ArgumentNullException(nameof(instance));

            return NexusUtilities.InvokeGenericMethod(typeof(T), instance, methodName, bindingFlags, genericType, parameters);
        }

        public static object? InvokeGenericMethod(Type methodParent, object instance, string methodName, BindingFlags bindingFlags, Type genericType, object[] parameters)
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