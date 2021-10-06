﻿// WARNING: DO NOT EDIT OR MOVE THIS FILE UNLESS YOU KNOW WHAT YOU ARE DOING!

using System;
using System.Collections.Generic;

namespace Nexus.Filters
{
    public delegate Span<double> GetFilterData(string catalogId, string resourceId, string representationId, DateTime begin, DateTime end);

    public static class FilterConstants
    {
        public static string SharedCatalogID { get; } = "/IN_MEMORY/FILTERS/SHARED";
    }

    public record FilterChannel
    {
        #region Constructors

        public FilterChannel()
        {
            //
        }

        public FilterChannel(string catalogId, string resourceId, string group, string unit, string description)
        {
            this.CatalogId = catalogId;
            this.ResourceId = resourceId;
            this.Group = group;
            this.Unit = unit;
            this.Description = description;
        }

        #endregion

        #region Properties

        public string CatalogId { get; init; } = FilterConstants.SharedCatalogID;
        public string ResourceId { get; init; } = string.Empty;
        public string Group { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;

        #endregion
    }

    public abstract class FilterProviderBase
    {
        #region Fields

        private List<FilterChannel> _filters;

        #endregion

        #region Constructors

        public FilterProviderBase()
        {
            _filters = this.GetFilters();
        }

        #endregion

        #region Properties

        public IReadOnlyList<FilterChannel> Filters => _filters;

        #endregion

        #region Methods

        public abstract void Filter(
            DateTime begin,
            DateTime end, 
            FilterChannel filterChannel, 
            GetFilterData getData,
            Span<double> result);

        protected abstract List<FilterChannel> GetFilters();

        #endregion
    }
}