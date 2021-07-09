// WARNING: DO NOT EDIT OR MOVE THIS FILE UNLESS YOU KNOW WHAT YOU ARE DOING!

using System;
using System.Collections.Generic;

namespace Nexus.Filters
{
    public delegate Span<double> GetFilterData(string catalogId, string resourceId, string datasetId, DateTime begin, DateTime end);

    public static class FilterConstants
    {
        public static string SharedCatalogID { get; } = "/IN_MEMORY/FILTERS/SHARED";
    }

    public record FilterResource
    {
        #region Constructors

        public FilterResource()
        {
            //
        }

        public FilterResource(string catalogId, string resourceName, string group, string unit, string description)
        {
            this.CatalogId = catalogId;
            this.ResourceName = resourceName;
            this.Group = group;
            this.Unit = unit;
            this.Description = description;
        }

        #endregion

        #region Properties

        public string CatalogId { get; init; } = FilterConstants.SharedCatalogID;
        public string ResourceName { get; init; } = string.Empty;
        public string Group { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;

        #endregion
    }

    public abstract class FilterProviderBase
    {
        #region Fields

        private List<FilterResource> _filters;

        #endregion

        #region Constructors

        public FilterProviderBase()
        {
            _filters = this.GetFilters();
        }

        #endregion

        #region Properties

        public IReadOnlyList<FilterResource> Filters => _filters;

        #endregion

        #region Methods

        public abstract void Filter(
            DateTime begin,
            DateTime end, 
            FilterResource filterResource, 
            GetFilterData getData,
            Span<double> result);

        protected abstract List<FilterResource> GetFilters();

        #endregion
    }
}