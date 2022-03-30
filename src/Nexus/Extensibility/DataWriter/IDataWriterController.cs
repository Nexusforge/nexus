﻿namespace Nexus.Extensibility
{
    internal interface IDataWriterController : IDisposable
    {
        Task InitializeAsync(ILogger logger, CancellationToken cancellationToken);
        Task WriteAsync(DateTime begin, DateTime end, TimeSpan samplePeriod, TimeSpan filePeriod, CatalogItemRequestPipeReader[] catalogItemRequestPipeReaders, IProgress<double> progress, CancellationToken cancellationToken);
    }
}