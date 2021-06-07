using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;

public class DataReaderProgressRecord
{
    #region Constructors

    public DataReaderProgressRecord(Dictionary<Dataset, DataRecord> datasetToRecordMap, DateTime begin, DateTime end)
    {
        this.DatasetToRecordMap = datasetToRecordMap;
        this.Begin = begin;
        this.End = end;
    }

    #endregion

    #region Properties

    public Dictionary<Dataset, DataRecord> DatasetToRecordMap { get; }

    public DateTime Begin { get; }

    public DateTime End { get; }

    #endregion
}