﻿using System;
using System.Reflection;
using System.Runtime.Loader;

namespace Nexus.PackageManagement
{
    internal class PackageLoadContext : AssemblyLoadContext
    {
        #region Fields

        private AssemblyDependencyResolver _resolver;

        #endregion

        #region Constructors

        public PackageLoadContext(string entryDllPath) : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(entryDllPath);
        }

        #endregion

        #region Methods

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);

            if (assemblyPath is not null)
                return LoadFromAssemblyPath(assemblyPath);

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

            if (libraryPath is not null)
                return LoadUnmanagedDllFromPath(libraryPath);

            return IntPtr.Zero;
        }

        #endregion
    }
}
