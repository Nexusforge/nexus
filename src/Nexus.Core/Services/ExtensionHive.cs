﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.PackageManagement;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class ExtensionHive : IExtensionHive
    {
        #region Fields

        private ILogger<ExtensionHive> _logger;
        private ILoggerFactory _loggerFactory;
        private PathsOptions _pathsOptions;

        private Dictionary<
            PackageController,
            ReadOnlyCollection<Type>>? _packageControllerMap;

        private ReadOnlyCollection<Type> _builtinExtensions;

        #endregion

        #region Constructors

        public ExtensionHive(IOptions<PathsOptions> pathsOptions, ILogger<ExtensionHive> logger, ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _pathsOptions = pathsOptions.Value;

            // add built-in extensions
            var thisAssembly = Assembly.GetExecutingAssembly();
            var thisTypes = this.ScanAssembly(thisAssembly, thisAssembly.DefinedTypes);

            _builtinExtensions = thisTypes;
        }

        #endregion

        #region Methods

        public async Task LoadPackagesAsync(IEnumerable<PackageReference> packageReferences, CancellationToken cancellationToken)
        {
            // clean up
            _logger.LogDebug("Unload previously loaded packages");

            if (_packageControllerMap is not null)
            {
                foreach (var (controller, _) in _packageControllerMap)
                {
                    controller.Unload();
                }

                _packageControllerMap = null;
            }

            // build new
            var packageControllerMap = new Dictionary<PackageController, ReadOnlyCollection<Type>>();

            var filteredPackageReferences = packageReferences
                .Where(packageReference => packageReference.ContainsKey("Provider"));

            foreach (var packageReference in filteredPackageReferences)
            {
                var packageController = new PackageController(packageReference, _loggerFactory.CreateLogger<PackageController>());
                using var scope = _logger.BeginScope(packageReference.ToDictionary(entry => entry.Key, entry => (object)entry.Value));

                try
                {
                    _logger.LogDebug("Load package");
                    var assembly = await packageController.LoadAsync(_pathsOptions.Packages, cancellationToken);
                    var types = this.ScanAssembly(assembly, assembly.ExportedTypes);
                    packageControllerMap[packageController] = types;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Loading package failed");
                }
            }

            _packageControllerMap = packageControllerMap;
        }

        public IEnumerable<Type> GetExtensions<T>() where T : IExtension
        {
            var types = _packageControllerMap is null
                ? _builtinExtensions
                : _builtinExtensions.Concat(_packageControllerMap.SelectMany(entry => entry.Value));

            return types
                .Where(type => typeof(T).IsAssignableFrom(type));
        }

        public T GetInstance<T>(string fullName) where T : IExtension
        {
            if (!TryGetInstance<T>(fullName, out var instance))
                throw new Exception($"Could not find extension {fullName} of type {typeof(T).FullName}.");

            return instance;
        }

        public bool TryGetInstance<T>(string fullName, [NotNullWhen(true)] out T? instance) where T : IExtension
        {
            instance = default(T);

            _logger.LogDebug("Instantiate extension {ExtensionType}", fullName);

            var types = _packageControllerMap is null
                ? _builtinExtensions
                : _builtinExtensions.Concat(_packageControllerMap.SelectMany(entry => entry.Value));

            var type = types
                .Where(current => typeof(T).IsAssignableFrom(current) && current.FullName == fullName)
                .FirstOrDefault();

            if (type is null)
            {
                _logger.LogWarning("Could not find extension {ExtensionType}", fullName);
                return false;
            }
            else
            {
                instance = (T)(Activator.CreateInstance(type) ?? throw new Exception("instance is null"));
                return true;
            }
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
