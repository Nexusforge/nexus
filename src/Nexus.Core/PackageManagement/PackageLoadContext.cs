using System;
using System.Reflection;
using System.Runtime.Loader;

namespace Nexus.PackageManagement
{
    public class PackageLoadContext : AssemblyLoadContext
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

        protected override Assembly Load(AssemblyName assemblyName)
        {
            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);

            if (assemblyPath != null)
                return LoadFromAssemblyPath(assemblyPath);

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

            if (libraryPath != null)
                return LoadUnmanagedDllFromPath(libraryPath);

            return IntPtr.Zero;
        }

        #endregion
    }
}
