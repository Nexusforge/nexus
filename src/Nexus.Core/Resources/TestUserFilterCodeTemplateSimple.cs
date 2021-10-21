using System;
using System.Collections.Generic;

namespace Nexus.Filters
{
    class FilterProvider : FilterProviderBase
    {
        /* Use this method to do the calculations for a filter that can be based on one or more
         * resources of available and accessible catalogs.
         *   begin:         Start of the current time period.
         *   filter:        The filter channel to make the calculations for.
         *   end:           End of the current time period.
         *   dataProvider:  Contains data of the preselected catalogs.
         *   result:        The resulting double array with length matching the time period and sample rate.
         */
        public override void Filter(DateTime begin, DateTime end, FilterChannel filter, DataProvider dataProvider, double[] result)
        {
            /* This dataset has the same length as the result array. */
            var t1 = dataProvider.IN_MEMORY_TEST_ACCESSIBLE.T1.DATASET_1_s_mean;

            for (int i = 0; i < result.Length; i++)
            {
                /* Example: Square each value. */
                result[i] = Math.Pow(t1[i], 2);

                /* Example: Add +1 to each value (to demonstrate how to use shared code). */
                // result[i] = Shared.AddOne(result[i]);
            }
        }

        /* Use this method to provide one or more filter definitions. */
        protected override List<FilterChannel> GetFilters()
        {
            return new List<FilterChannel>()
            {
                new FilterChannel()
                {
                    ResourceId = "T1_squared",
                    Unit = "°C²",
                    Description = "Temperature squared."
                }
            };
        }
    }
}