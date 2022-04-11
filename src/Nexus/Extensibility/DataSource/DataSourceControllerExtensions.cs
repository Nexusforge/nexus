﻿using Nexus.Core;
using Nexus.DataModel;
using Nexus.Utilities;
using System.IO.Pipelines;
using System.Runtime.InteropServices;

namespace Nexus.Extensibility
{
    internal static class DataSourceControllerExtensions
    {
        public static DataSourceDoubleStream ReadAsStream(
            this IDataSourceController controller,
            DateTime begin,
            DateTime end,
            CatalogItemRequest request,
            DataOptions dataOptions,
            ILogger<DataSourceController> logger)
        {
            // DataSourceDoubleStream is only required to enable the browser to determine the download progress.
            // Otherwise the PipeReader.AsStream() would be sufficient.

            var samplePeriod = request.Item.Representation.SamplePeriod;
            var elementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, samplePeriod);
            var totalLength = elementCount * NexusUtilities.SizeOf(NexusDataType.FLOAT64);
            var pipe = new Pipe();
            var stream = new DataSourceDoubleStream(totalLength, pipe.Reader);

            var task = controller.ReadSingleAsync(
                begin,
                end,
                request,
                pipe.Writer,
                dataOptions,
                progress: default,
                logger,
                CancellationToken.None);

            _ = Task.Run(async () =>
            {
                try
                {
#pragma warning disable VSTHRD003 // Vermeiden Sie das Warten auf fremde Aufgaben
                    await task;
#pragma warning restore VSTHRD003 // Vermeiden Sie das Warten auf fremde Aufgaben
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Streaming failed");
                    stream.Cancel();
                }
            });

            return stream;
        }

        public static Task ReadSingleAsync(
            this IDataSourceController controller,
            DateTime begin,
            DateTime end,
            CatalogItemRequest request,
            PipeWriter dataWriter,
            DataOptions dataOptions,
            IProgress<double>? progress,
            ILogger<DataSourceController> logger,
            CancellationToken cancellationToken)
        {
            var samplePeriod = request.Item.Representation.SamplePeriod;

            var readingGroup = new DataReadingGroup(controller, new CatalogItemRequestPipeWriter[]
            {
                new CatalogItemRequestPipeWriter(request, dataWriter)
            });

            return DataSourceController.ReadAsync(
                begin,
                end,
                samplePeriod,
                new DataReadingGroup[] { readingGroup },
                dataOptions,
                progress,
                logger,
                cancellationToken);
        }
    }
}