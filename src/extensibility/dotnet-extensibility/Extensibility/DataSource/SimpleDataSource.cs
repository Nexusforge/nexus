using Microsoft.Extensions.Logging;
using Nexus.DataModel;

namespace Nexus.Extensibility
{
    /// <summary>
    /// A simple implementation of a data source.
    /// </summary>
    public abstract class SimpleDataSource : IDataSource
    {
        #region Fields

        private CatalogRegistration[] _catalogRegistrations;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleDataSource"/>.
        /// </summary>
        /// <param name="catalogRegistrations">A list of catalog registrations.</param>
        public SimpleDataSource(CatalogRegistration[] catalogRegistrations)
        {
            _catalogRegistrations = catalogRegistrations;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the data source context. This property is not accessible from within class constructors as it will bet set later.
        /// </summary>
        public DataSourceContext Context { get; private set; } = default!;

        /// <summary>
        /// Gets the data logger. This property is not accessible from within class constructors as it will bet set later.
        /// </summary>
        public ILogger Logger { get; private set; } = default!;

        #endregion

        #region Methods

        /// <inheritdoc />
        public virtual Task SetContextAsync(
            DataSourceContext context, 
            ILogger logger, 
            CancellationToken cancellationToken)
        {
            Context = context;
            Logger = logger;

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(
            string path,
            CancellationToken cancellationToken)
        {
            if (path == "/")
                return Task.FromResult(_catalogRegistrations);

            else
                return Task.FromResult(new CatalogRegistration[0]);
        }

        /// <inheritdoc />
        public abstract Task<ResourceCatalog> GetCatalogAsync(
            string catalogId, 
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public virtual Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(
            string catalogId, 
            CancellationToken cancellationToken)
        {
            return Task.FromResult((DateTime.MinValue, DateTime.MaxValue));
        }

        /// <inheritdoc />
        public virtual Task<double> GetAvailabilityAsync(
            string catalogId, 
            DateTime begin, 
            DateTime end, 
            CancellationToken cancellationToken)
        {
            return Task.FromResult(double.NaN);
        }

        /// <inheritdoc />
        public abstract Task ReadAsync(
            DateTime begin, 
            DateTime end, 
            ReadRequest[] requests, 
            ReadDataHandler readData, 
            IProgress<double> progress, 
            CancellationToken cancellationToken);

        #endregion
    }
}
