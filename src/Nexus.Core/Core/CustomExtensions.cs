using Nexus.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Nexus
{
    internal static class CustomExtensions
    {
#pragma warning disable VSTHRD200 // Verwenden Sie das Suffix "Async" für asynchrone Methoden
        public static async Task<(T[] Results, AggregateException Exception)> WhenAllEx<T>(this IEnumerable<Task<T>> tasks)
#pragma warning restore VSTHRD200 // Verwenden Sie das Suffix "Async" für asynchrone Methoden
        {
            tasks = tasks.ToArray();

            await Task.WhenAll(tasks);

            var results = tasks
                .Where(task => task.Status == TaskStatus.RanToCompletion)
                .Select(task => task.Result)
                .ToArray();

            var aggregateExceptions = tasks
                .Where(task => task.IsFaulted && task.Exception is not null)
                .Select(task => task.Exception ?? throw new Exception("exception is null"))
                .ToArray();

            var flattenedAggregateException = new AggregateException(aggregateExceptions).Flatten();

            return (results, flattenedAggregateException);
        }

        public static byte[] Hash(this string value)
        {
            var md5 = MD5.Create(); // compute hash is not thread safe!
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(value)); // 
            return hash;
        }

        public static Memory<To> Cast<TFrom, To>(this Memory<TFrom> buffer)
            where TFrom : unmanaged
            where To : unmanaged
        {
            return new CastMemoryManager<TFrom, To>(buffer).Memory;
        }

        public static Memory<To> Cast<TFrom, To>(this ReadOnlyMemory<TFrom> buffer)
            where TFrom : unmanaged
            where To : unmanaged
        {
            return new ReadonlyCastMemoryManager<TFrom, To>(buffer).Memory;
        }

        public static string ToISO8601(this DateTime dateTime)
        {
            return dateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH-mm-ssZ");
        }

        public static DateTime RoundDown(this DateTime dateTime, TimeSpan timeSpan)
        {
            return new DateTime(dateTime.Ticks - (dateTime.Ticks % timeSpan.Ticks), dateTime.Kind);
        }
    }
}
