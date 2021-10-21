using Nexus.Core;
using Nexus.Filters;
using System.Collections.Generic;
using System.Runtime.Loader;

namespace Nexus.Extensions
{
    internal class FilterDataSourceLoadContext : AssemblyLoadContext
    {
        public FilterDataSourceLoadContext() : base(isCollectible: true) 
        {
            //
        }
    }

    internal record FilterDataSourceCacheEntry(
        CodeDefinition FilterCodeDefinition,
        FilterDataSourceLoadContext LoadContext,
        FilterProviderBase FilterProvider,
        List<string> SupportedResourceIds);
}
