using GraphQL.Types;
using Nexus.Services;
using System;

namespace Nexus.API
{
    public class ProjectSchema : Schema
    {
        public ProjectSchema(DatabaseManager databaseManager, IServiceProvider provider) : base(provider)
        {
            Query = new ProjectQuery(databaseManager);
        }
    }
}
