using System;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
#warning Add "CheckFileSize" method (e.g. for Famos).
    public class DataWriterController
    {
        public void Write(DateTime begin, DateTime end, BackendSource backendSource, TimeSpan samplePeriod, PipeReader dataReader, PipeReader statusReader)
        {
            
        }
    }
}