﻿using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Extensibility
{
    public record DataSourceContext()
    {
        public Uri ResourceLocator { get; init; }
        public Dictionary<string, string> Configuration { get; init; }
        public ILogger Logger { get; init; }
    }

    public record ReadRequest(
        CatalogItem CatalogItem,
        Memory<byte> Data,
        Memory<byte> Status);

    public sealed record BackendSource(string Type, Uri ResourceLocator, Dictionary<string, string>? Configuration = null)
    {
        public override int GetHashCode()
        {
            return HashCode.Combine(this.Type, this.ResourceLocator);
        }

        public bool Equals(BackendSource? other)
        {
            return other is not null &&
                this.Type == other.Type &&
                this.ResourceLocator == other.ResourceLocator;
        }
    }
}