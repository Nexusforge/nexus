using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;

public class DataSourceProgressRecord
{
    #region Constructors

    public DataSourceProgressRecord(Dictionary<DatasetRecord, ReadResult> datasetRecordToResultMap, DateTime begin, DateTime end)
    {
        this.DatasetToResultMap = datasetRecordToResultMap;
        this.Begin = begin;
        this.End = end;
    }

    #endregion

    #region Properties

    public Dictionary<DatasetRecord, ReadResult> DatasetToResultMap { get; }

    public DateTime Begin { get; }

    public DateTime End { get; }

    #endregion
}