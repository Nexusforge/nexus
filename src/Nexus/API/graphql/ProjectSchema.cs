using GraphQL.Types;
using Nexus.Services;
using System;

namespace Nexus.API
{
    public class CatalogSchema : Schema
    {
        public CatalogSchema(DatabaseManager databaseManager, IServiceProvider provider) : base(provider)
        {
            Query = new CatalogQuery(databaseManager);
        }
    }
}
