using Nexus.Core;
using Nexus.Filters;
using System.Collections.Generic;
using System.Runtime.Loader;

namespace Nexus.Extensions
{
    public class FilterDataSourceLoadContext : AssemblyLoadContext
    {
        public FilterDataSourceLoadContext() : base(isCollectible: true) 
        {
            //
        }
    }

    public record FilterDataSourceCacheEntry(
        CodeDefinition FilterCodeDefinition,
        FilterDataSourceLoadContext LoadContext,
        FilterProviderBase FilterProvider,
        List<string> SupportedResourceIds);
}
