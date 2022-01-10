## Package References

Nexus maintains a list of package references to allow extending the core functionalities. This list, which is part of the `config` folder, can only be edited by administrators to make sure only trusted code is executed. For example, a simple C# data source should only read the requested data and do nothing else. It should never expose the data in any other ways.

## Extensions

When Nexus boots, the package references are restored and their content is loaded into memory. After that, Nexus searches for implementations of `IDataSource` and `IDataWriter`.

To enable users to activate own `IDataSource` instances (e.g. in case of the `RpcDataSource` to run user defined code in a sandboxed Docker container) there is a list of backend sources for each user in the `users` folder, which describe how to instantiate an `IDataSource` implementations.

## Catalogs

Every `IDataSource` instance contributes zero or more resource catalogs. Catalogs can be visible for all users or for the registering user only, which depends on the value of the `CanEditCatalog` claim and if the user is an administrator.

After Nexus has loaded the backend sources for each user, it starts asking all `IDataSource` instances for their list of top level catalogs. Every catalog is then converted into a `CatalogContainer` which defers loading the catalog content to when it is actually required. The top level catalogs are "mounted" in a "subdirectory" the root catalog. A mounted catalog can consists of more child catalogs. To keep things simple and efficient, these child catalogs can only be provided by backend source that provided the parent catalog.

## Use Case: External System with User Specific Catalogs

There may be the requirement that a catalog should have one or more child catalogs. This might happen when an `IDataSource` wants to provide access to an external system or to another Nexus instance. In that case, it may also be necessary to provide user credentials for the external system. Additionally, the presented catalog hierarchy might be specific to the current user.

To enable this scenario, there is the need for Nexus to distinguish between `static` child catalogs which are loaded once and `transient` ones that should be looked up on each request. This option can be specified by the `IDataSource` individually for each catalog registration.

An `IDataSource` which provides user-specific catalogs would register these as `transient` and use the user credentials to retrieve them from the external system. To speed things up, the `IDataSource` may decide to cache the returned information on a per user level and delete that cache entry after a certain amount of time. 

The credentials should be available for the lifetime of the current user session. In a browser-only scenario this could be implemented using a session cookie. For other client types like the Open API client, a more natural approach would be to attach additional configuration values that should be passed to the `IDataSource` instance to the bearer token which is created when the user authenticates.

Configuration values that are part of the bearer token need to have a selector prepended in front of the configuration key to define the target `IDataSource` instance. This is necessary to ensure that credentials do no leak to the wrong `IDataSource`. 

Example: The user credentials for a catalog named `/A/B/C` could be written as:

```c#
"/A/B/C:Username" = "foo"
"/A/B/C:Password" = "bar"
```

> 🛈 The exact configuration key names depend on the `IDataSource` implementation.

> 🛈 Requests to other catalogs like `/A`, `/A/B`, `/A/B/D` or `/A/B/C/X` would also get these configuration values passed because they are provided by the same `IDataSource`. This is not considered a security issue.

## Considerations

**Package Reference Duplicates**

There might be duplicate package references. This is not issue because all extensions are loaded into a separate load context.

**Backend Source Duplicates**

When a backend source is registered twice this will most likely lead to duplicate catalogs. Duplicates will be ignored with a log warning. Catalogs provided by backend sources registerd by administrators will win over other catalogs. Also catalogs that are already registered will win over new catalogs.

**Add / Remove Package Reference**

Recreate `CatalogState`.

**Add / Remove Backend Source**

Recreate `CatalogState` but reuse `CatalogInfo`.

**Loading Child Catalogs**

Update `CatalogState`.

The `IDataSource` interface offers the method `GetCatalogRegistrations(string path)`. This method should return the direct child catalogs (if any). When, for instance, the path is `/`, the returned identifiers could be `/a/b` and `/c`. When a user then clicks on the catalog `/a/b` from within the GUI, `GetCatalogRegistrations` is called again, now with the path `/a/b` and this time the `IDataSource` instance might return the catalog identifier `/a/b/c`.

When, alternatively, the REST API is used to access the not yet known catalog `/a/b/c`, the `IDataSource` of the next higher known catalog is consulted to provide child catalog identifiers. This process is repeated until the requested catalog is found and loaded.

In the end, this leads to something similar to a virtual file system with the root folder `/`.

**IDataWriter**

Currently there are no plans to allow users to register their own `IDataWriter` implementations.