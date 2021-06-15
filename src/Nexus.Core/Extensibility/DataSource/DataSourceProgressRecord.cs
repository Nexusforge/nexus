using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;

public class DataSourceProgressRecord
{
    #region Constructors

    public DataSourceProgressRecord(Dictionary<Dataset, ReadResult> datasetToResultMap, DateTime begin, DateTime end)
    {
        this.DatasetToResultMap = datasetToResultMap;
        this.Begin = begin;
        this.End = end;
    }

    #endregion

    #region Properties

    public Dictionary<Dataset, ReadResult> DatasetToResultMap { get; }

    public DateTime Begin { get; }

    public DateTime End { get; }

    #endregion
}