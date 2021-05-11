﻿using System;
using System.Reflection;
using System.Runtime.Loader;

namespace Nexus
{
    public class ExtensionLoadContext : AssemblyLoadContext
    {
        private AssemblyDependencyResolver _resolver;

        public ExtensionLoadContext(string pluginPath)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            string assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);

            if (assemblyPath != null)
                return LoadFromAssemblyPath(assemblyPath);

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

            if (libraryPath != null)
                return LoadUnmanagedDllFromPath(libraryPath);

            return IntPtr.Zero;
        }
    }
}
