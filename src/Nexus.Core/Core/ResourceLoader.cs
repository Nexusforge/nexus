using System;
using System.IO;
using System.Reflection;

namespace Nexus.Core
{
    internal static class ResourceLoader
    {
        public static Stream GetResourceStream(string resourceName, bool addRootNamespace = false)
        {
            var assembly = typeof(ResourceLoader).GetTypeInfo().Assembly;
            var rootNamespace = assembly.GetName().Name;
            var fullQualitifedName = addRootNamespace ? $"{rootNamespace}.{resourceName}" : resourceName;
            var stream = assembly.GetManifestResourceStream(fullQualitifedName);

            if (stream is null)
                throw new InvalidOperationException($"Resource '{fullQualitifedName}' not found in {assembly.FullName}.");

            return stream;
        }

        public static byte[] GetResourceBlob(string name, bool addRootNamespace = false)
        {
            using (var stream = GetResourceStream(name, addRootNamespace))
            {
                var bytes = new byte[stream.Length];

                using (var memoryStream = new MemoryStream(bytes))
                {
                    stream.CopyTo(memoryStream);
                }

                return bytes;
            }
        }

        public static byte[] GetOrCreateResource(ref byte[]? resource, string name)
        {
            if (resource is null)
                resource = GetResourceBlob(name);

            return resource;
        }
    }
}
