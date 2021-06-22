using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.Data;

namespace Nexus.Extensibility
{
    public record DatasetWriteInfo(DataSet Dataset, Memory<byte> Data, Memory<byte> Status);
    public record CatalogWriteInfo(string CatalogId, SampleRateContainer SampleRate, List<DatasetWriteInfo> DatasetInfos);
}
