﻿using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.PackageManagement;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Nexus.Services
{
    internal interface IExtensionHive
    {
        IEnumerable<Type> GetExtensions<T>(
            ) where T : IExtension;

        PackageReference GetPackageReference<T>(
            string fullName) where T : IExtension;

        T GetInstance<T>(
            string fullName) where T : IExtension;

        Task LoadPackagesAsync(
            IEnumerable<PackageReference> packageReferences,
            IProgress<double> progress,
            CancellationToken cancellationToken);

        Task<string[]> GetVersionsAsync(
            PackageReference packageReference,
            CancellationToken cancellationToken);
    }

    internal class ExtensionHive : IExtensionHive
    {
        #region Fields

        private ILogger<ExtensionHive> _logger;
        private ILoggerFactory _loggerFactory;
        private PathsOptions _pathsOptions;

        private Dictionary<PackageController, ReadOnlyCollection<Type>>? _packageControllerMap = default!;

        #endregion

        #region Constructors

        public ExtensionHive(
            IOptions<PathsOptions> pathsOptions,
            ILogger<ExtensionHive> logger,
            ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _pathsOptions = pathsOptions.Value;
        }

        #endregion

        #region Methods

        public async Task LoadPackagesAsync(
            IEnumerable<PackageReference> packageReferences,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            // clean up
            if (_packageControllerMap is not null)
            {
                _logger.LogDebug("Unload previously loaded packages");

                foreach (var (controller, _) in _packageControllerMap)
                {
                    controller.Unload();
                }

                _packageControllerMap = default;
            }

            var nexusPackageReference = new PackageReference(
                Provider: PackageController.BUILTIN_PROVIDER,
                Configuration: new Dictionary<string, string>(),
                ProjectUrl: "https://github.com/Nexusforge/nexus",
                RepositoryUrl: "https://github.com/Nexusforge/nexus/blob/master/src/Nexus/Extensions"
            );

            packageReferences = new List<PackageReference>() { nexusPackageReference }.Concat(packageReferences);

            // build new
            var packageControllerMap = new Dictionary<PackageController, ReadOnlyCollection<Type>>();
            var currentCount = 0;
            var totalCount = packageReferences.Count();

            foreach (var packageReference in packageReferences)
            {
                var packageController = new PackageController(packageReference, _loggerFactory.CreateLogger<PackageController>());
                using var scope = _logger.BeginScope(packageReference.Configuration.ToDictionary(entry => entry.Key, entry => (object)entry.Value));

                try
                {
                    _logger.LogDebug("Load package");
                    var assembly = await packageController.LoadAsync(_pathsOptions.Packages, cancellationToken);

                    var types = ScanAssembly(assembly, packageReference.Provider == PackageController.BUILTIN_PROVIDER
                        ? assembly.DefinedTypes
                        : assembly.ExportedTypes);

                    packageControllerMap[packageController] = types;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Loading package failed");
                }

                currentCount++;
                progress.Report(currentCount / (double)totalCount);
            }

            _packageControllerMap = packageControllerMap;
        }

        public Task<string[]> GetVersionsAsync(
            PackageReference packageReference,
            CancellationToken cancellationToken)
        {
            var controller = new PackageController(
                packageReference,
                _loggerFactory.CreateLogger<PackageController>());

            return controller.DiscoverAsync(cancellationToken);
        }

        public IEnumerable<Type> GetExtensions<T>() where T : IExtension
        {
            if (_packageControllerMap is null)
            {
                return Enumerable.Empty<Type>();
            }

            else
            {
                var types = _packageControllerMap.SelectMany(entry => entry.Value);

                return types
                    .Where(type => typeof(T).IsAssignableFrom(type));
            }
        }

        public PackageReference GetPackageReference<T>(string fullName) where T : IExtension
        {
            if (!TryGetTypeInfo<T>(fullName, out var packageController, out var _))
                throw new Exception($"Could not find extension {fullName} of type {typeof(T).FullName}.");

            return packageController.PackageReference;
        }

        public T GetInstance<T>(string fullName) where T : IExtension
        {
            if (!TryGetTypeInfo<T>(fullName, out var _, out var type))
                throw new Exception($"Could not find extension {fullName} of type {typeof(T).FullName}.");

            _logger.LogDebug("Instantiate extension {ExtensionType}", fullName);

            var instance = (T)(Activator.CreateInstance(type) ?? throw new Exception("instance is null"));

            return instance;
        }

        private bool TryGetTypeInfo<T>(
            string fullName, 
            [NotNullWhen(true)] out PackageController? packageController,
            [NotNullWhen(true)] out Type? type) 
            where T : IExtension
        {
            type = default;
            packageController = default;

            if (_packageControllerMap is null)
                return false;

            IEnumerable<(PackageController Controller, Type Type)> typeInfos = _packageControllerMap
                .SelectMany(entry => entry.Value.Select(type => (entry.Key, type)));

            (packageController, type) = typeInfos
                .Where(typeInfo => typeof(T).IsAssignableFrom(typeInfo.Type) && typeInfo.Type.FullName == fullName)
                .FirstOrDefault();

            if (type is null)
                return false;

            return true;
        }

        private ReadOnlyCollection<Type> ScanAssembly(Assembly assembly, IEnumerable<Type> types)
        {
            var foundTypes = types
                .Where(type =>
                {
                    var isClass = type.IsClass;
                    var isInstantiatable = !type.IsAbstract;
                    var isDataSource = typeof(IDataSource).IsAssignableFrom(type);
                    var isDataWriter = typeof(IDataWriter).IsAssignableFrom(type);

                    if (isClass && isInstantiatable && (isDataSource | isDataWriter))
                    {
                        var hasParameterlessConstructor = type.GetConstructor(Type.EmptyTypes) is not null;

                        if (!hasParameterlessConstructor)
                            _logger.LogWarning("Type {TypeName} from assembly {AssemblyName} has no parameterless constructor", type.FullName, assembly.FullName);

                        return hasParameterlessConstructor;
                    }

                    return false;
                })
                .ToList()
                .AsReadOnly();

            return foundTypes;
        }

        #endregion
    }
}
