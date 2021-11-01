# Background

Since data may be stored in very heterogeneous databases, Nexus implements an extensibility mechanism to support load data from custom data sources. Whenever data from a certain data source is requests, a `DataSourceController` is instantiated which wraps a data source instance which in turn must implement the `IDataSource` interface.

# IDataSource

The interface is defined as follows:

```cs
/* called right after instantiation to provide the source URL, parameters and a logger instance */
Task SetContextAsync(...);

/* called whenever the database is reloaded */
Task<List<ResourceCatalog>> GetCatalogsAsync(...);

/* called whenever the database time range is requested */
Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(...);

/* called whenever the data availability is requested */
Task<double> GetAvailabilityAsync(...);

/* called whenever data is requested */
Task ReadAsync(...);
```

# Life Cycle
`IDataSource` instances are short-lived to make them thread-safe and enable them to cache open connections or files handles but at the same time make them free all resources when they are disposed (1). 

When the Nexus database is reloaded, the registered `IDataSources` are instantiated and then asked to provided one or more catalog definitions to Nexus. However, for example, when a user asks for the data availability, the `IDataSource` is instantiated again. Since it would be too resource consuming to reload the catalogs every time a simple request is made, subsequent instantiations will get previously provided catalogs passed back within the `SetContextAsync` method. The main reason for the `DataSourceController` to exist is to cache the catalogs and provide them back as needed.

A read operation may be triggered by either streaming or exporting of the data of one or multiple catalog items. Grouped by the corresponding `IDataSource`, all read requests first arrive in a static method called `ReadAsync`, which is located in the `DataSourceController` type. From there the method distributes the read requests to the actual `DataSourceController` instances which forward it to the wrapped IDataSource instance. To keep the memory consumption low, the controller may decide to reduce the time period per request and repeat the reading step until all data has been loaded.

With the collection of read requests passed to the `IDataSource`, the implementation may decide to load the data sequentially or in parallel and when everything is read, to return the results all at once.

(1) A `IDataSource` instance is disposed automatically by Nexus when it implements the `IDisposable` interface.