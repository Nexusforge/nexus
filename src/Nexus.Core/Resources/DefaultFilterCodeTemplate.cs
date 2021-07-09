using System;
using System.Collections.Generic;

namespace Nexus.Filters
{
    class FilterProvider : FilterProviderBase
    {
        /* Use this method to do the calculations for a filter */
        public override void Filter(DateTime begin, DateTime end, FilterResource filter, DataProvider dataProvider, Span<double> result)
        {

        }

        /* Use this method to provide one or more filter definitions. */
        protected override List<FilterResource> GetFilters()
        {
            return new List<FilterResource>()
            {
                
            };
        }
    }
}
