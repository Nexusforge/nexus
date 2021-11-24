## Package References

Nexus maintains a list of package references to allow extending the core functionalities. This list, which is part of the `config` folder, can only be edited by administrators to make sure only trusted code is executed. For example, a simple C# data source should only read the requested data and do nothing else. It should never expose the data in any other ways.

## Extensions

When Nexus boots, the package references are restored and their content is loaded into memory. After that, Nexus searches for implementations of `IDataSource` and `IDataWriter`.

To enable users to activate own `IDataSource` instances (e.g. in case of the `RpcDataSource` to run user defined code in a sandboxed Docker container) there is a list of backend sources for each user in the `users` folder, which describe how to instantiate an `IDataSource` implementations.

## Catalogs

Every `IDataSource` instance contributes zero or more resource catalogs. Catalogs can be visible for all users or for the registering user only, which depends on the value of the `CanEditCatalog` claim and if the user is an administrator.

After Nexus has loaded the backend sources for each user, it starts asking all `IDataSource` instances for their list of top level catalogs. Every catalog is then converted into a `CatalogContainer` which defers loading the catalog content to when it is actually required. Depending on the visibility (for all users or for the registering user only), the catalogs are organized using a dictionary with one key per contributing user and a special key to hold a list of common catalogs.

## Use Case: External Database with user specific catalogs

It should be avoided that Nexus stores user credentials for external databases. The current solution approach is to let the `IDataSource` instance deliver catalogs which act as a normal catalogs and at the same time as a folder for child catalogs. When that catalog/folder is opened, the child catalogs are loaded asynchronously (optionally using credentials the user has provided temporarily in the GUI or in the REST request).

To implement this, the `IDataSource` interface offers the method `GetCatalogIds(string path)`. This method should return the direct child catalogs. When, for instance, the path is `/`, the returned identifiers could be `/a/b` and `/c`. When a user then clicks on the catalog `/a/b` from within the GUI, `GetCatalogIds` is called again, now with the path `/a/b` and this time the `IDataSource` instance might return the catalog identifier `/a/b/c`.

When, alternatively, the REST API is used to access the not yet known catalog `/a/b/c`, the `IDataSource` of the next higher known catalog is consulted to provide child catalog identifiers. This process is repeated until the requested catalog is found and loaded.

In the end, this leads to something similar to a virtual file system with the root folder `/`.

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

**IDataWriter**
Currently there are no plans to allow users to register their own `IDataWriter` implementations.