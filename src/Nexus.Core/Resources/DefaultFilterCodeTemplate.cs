﻿using System;
using System.Collections.Generic;

namespace Nexus.Filters
{
    class FilterProvider : FilterProviderBase
    {
        /* Use this method to do the calculations for a filter */
        public override void Filter(DateTime begin, DateTime end, FilterChannel filter, DataProvider dataProvider, Span<double> result)
        {

        }

        /* Use this method to provide one or more filter definitions. */
        protected override List<FilterChannel> GetFilters()
        {
            return new List<FilterChannel>()
            {
                
            };
        }
    }
}
