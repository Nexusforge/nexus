using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.PackageManagement;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class ExtensionHive : IExtensionHive
    {
        #region Fields

        private ILogger _logger;
        private PathsOptions _pathsOptions;

        private Dictionary<
            PackageController,
            ReadOnlyCollection<(ExtensionIdentificationAttribute Identification, Type Type)>> _packageControllerMap;

        private ReadOnlyCollection<(ExtensionIdentificationAttribute Identification, Type Type)> _builtinExtensions;

        #endregion

        #region Constructors

        public ExtensionHive(IOptions<PathsOptions> pathsOptions, ILogger logger)
        {
            _logger = logger;
            _pathsOptions = pathsOptions.Value;

            // add built-in extensions
            var thisAssembly = Assembly.GetExecutingAssembly();
            var thisAttributesAndTypes = this.ScanAssembly(thisAssembly);

            _builtinExtensions = thisAttributesAndTypes;
        }

        #endregion

        #region Methods

        public async Task LoadPackagesAsync(IEnumerable<PackageReference> packageReferences, CancellationToken cancellationToken)
        {
            // clean up
            if (_packageControllerMap is not null)
            {
                foreach (var (controller, _) in _packageControllerMap)
                {
                    controller.Unload();
                }

                _packageControllerMap = null;
            }

            // build new
            var packageControllerMap = new Dictionary<PackageController, ReadOnlyCollection<(ExtensionIdentificationAttribute, Type)>>();

            var filteredPackageReferences = packageReferences
                .Where(packageReference => packageReference.ContainsKey("Provider"));

            foreach (var packageReference in filteredPackageReferences)
            {
                var packageController = new PackageController(packageReference, _logger);
                using var scope = _logger.BeginScope(packageReference.ToDictionary(entry => entry.Key, entry => (object)entry.Value));

                try
                {
                    _logger.LogDebug("Load package.");
                    var assembly = await packageController.LoadAsync(_pathsOptions.Packages, cancellationToken);
                    var attributesAndTypes = this.ScanAssembly(assembly);
                    packageControllerMap[packageController] = attributesAndTypes;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Load package failed.");
                }
            }

            _packageControllerMap = packageControllerMap;
        }

        public T GetInstance<T>(string identifier) where T : IExtension
        {
            if (!TryGetInstance<T>(identifier, out var instance))
                throw new Exception($"Could not find extension {identifier} of type {typeof(T).FullName}.");

            return instance;
        }

        public bool TryGetInstance<T>(string identifier, out T instance) where T : IExtension
        {
            instance = default(T);

            if (_packageControllerMap is null)
                return false;

            _logger.LogDebug("Instantiate extension {ExtensionIdentifier} of type {Type}.", identifier, typeof(T).FullName);

            var type = _builtinExtensions
                .Concat(_packageControllerMap.SelectMany(entry => entry.Value))
                .Where(extensionTuple => typeof(T).IsAssignableFrom(extensionTuple.Type) && extensionTuple.Identification.Id == identifier)
                .Select(extenionTuple => extenionTuple.Type)
                .FirstOrDefault();

            if (type is null)
            {
                _logger.LogWarning("Could not find extension {ExtensionIdentifier} of type {Type}.", identifier, typeof(T).FullName);
                return false;
            }
            else
            {
                instance = (T)Activator.CreateInstance(type);
                return true;
            }
        }

        private ReadOnlyCollection<(ExtensionIdentificationAttribute, Type)> ScanAssembly(Assembly assembly)
        {
            var attributesAndTypes = assembly.ExportedTypes
                .Where(type =>
                {
                    var isClass = type.IsClass;
                    var isInstantiatable = !type.IsAbstract;
                    var isDataSource = typeof(IDataSource).IsAssignableFrom(type);
                    var isDataWriter = typeof(IDataWriter).IsAssignableFrom(type);

                    if (isClass && isInstantiatable && (isDataSource | isDataWriter))
                    {
                        var hasAttribute = type.IsDefined(typeof(ExtensionIdentificationAttribute), inherit: false);

                        if (!hasAttribute)
                            _logger.LogWarning("Type {TypeName} from assembly {AssemblyName}, has no extension identification attribute.", type.FullName, assembly.FullName);

                        var hasParameterlessConstructor = type.GetConstructor(Type.EmptyTypes) is not null;

                        if (!hasParameterlessConstructor)
                            _logger.LogWarning("Type {TypeName} from assembly {AssemblyName}, has no parameterless constructor.", type.FullName, assembly.FullName);

                        return hasAttribute && hasParameterlessConstructor;
                    }

                    return false;
                })
                .Select(type =>
                {
                    var attribute = type.GetCustomAttribute<ExtensionIdentificationAttribute>(inherit: false);
                    return (attribute, type);
                })
                .ToList()
                .AsReadOnly();

            return attributesAndTypes;
        }

        #endregion
    }
}
