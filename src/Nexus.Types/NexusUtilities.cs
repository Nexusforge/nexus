using Nexus.Infrastructure;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Nexus
{
    public static class NexusUtilities
    {
        public static double ToUnixTimeStamp(this DateTime value)
        {
            return value.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        }

        public static int SizeOf(NexusDataType dataType)
        {
            return NexusUtilities.SizeOf(NexusUtilities.GetTypeFromNexusDataType(dataType));
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

        public static Type GetTypeFromNexusDataType(NexusDataType dataType)
        {
            return dataType switch
            {
                NexusDataType.BOOLEAN              => typeof(bool),
                NexusDataType.UINT8                => typeof(Byte),
                NexusDataType.INT8                 => typeof(SByte),
                NexusDataType.UINT16               => typeof(UInt16),
                NexusDataType.INT16                => typeof(Int16),
                NexusDataType.UINT32               => typeof(UInt32),
                NexusDataType.INT32                => typeof(Int32),
                NexusDataType.UINT64               => typeof(UInt64),
                NexusDataType.INT64                => typeof(Int64),
                NexusDataType.FLOAT32              => typeof(Single),
                NexusDataType.FLOAT64              => typeof(Double),
                _                                   => throw new NotSupportedException($"The specified data type '{dataType}' is not supported.")
            };
        }

        public static bool CheckProjectNamingConvention(string value, out string errorDescription, bool includeValue = false)
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

        public static string EnforceNamingConvention(string value, string prefix = "X")
        {
            if (string.IsNullOrWhiteSpace(value))
                value = "unnamed";

            value = Regex.Replace(value, "[^A-Za-z0-9_]", "_");

            if (Regex.IsMatch(value, "^[0-9_]"))
                value = $"{prefix}_" + value;

            return value;
        }

        public static object InvokeGenericMethod<T>(T instance, string methodName, BindingFlags bindingFlags, Type genericType, object[] parameters)
        {
            return NexusUtilities.InvokeGenericMethod(typeof(T), instance, methodName, bindingFlags, genericType, parameters);
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