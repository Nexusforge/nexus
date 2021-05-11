using Nexus.Core;
using Nexus.Filters;
using System.Collections.Generic;
using System.Runtime.Loader;

namespace Nexus.Extensions
{
    public class FilterDataReaderLoadContext : AssemblyLoadContext
    {
        public FilterDataReaderLoadContext() : base(isCollectible: true) 
        {
            //
        }
    }

    public record FilterDataReaderCacheEntry(
        CodeDefinition FilterCodeDefinition,
        FilterDataReaderLoadContext LoadContext,
        FilterProviderBase FilterProvider,
        List<string> SupportedChanneIds);
}
