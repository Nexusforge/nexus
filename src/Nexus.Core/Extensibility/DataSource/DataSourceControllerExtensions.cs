using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Utilities;
using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    internal static class DataSourceControllerExtensions
    {
        public static DataSourceDoubleStream ReadAsStream(
            this IDataSourceController controller,
            DateTime begin,
            DateTime end,
            CatalogItem catalogItem,
            ILogger<DataSourceController> logger)
        {
            // DataSourceDoubleStream is only required to enable the browser to determine the download progress.
            // Otherwise the PipeReader.AsStream() would be sufficient.

            var samplePeriod = catalogItem.Representation.SamplePeriod;
            var elementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, samplePeriod);
            var totalLength = elementCount * NexusCoreUtilities.SizeOf(NexusDataType.FLOAT64);
            var pipe = new Pipe();

            _ = controller.ReadSingleAsync(
                begin,
                end,
                catalogItem,
                pipe.Writer,
                statusWriter: default,
                progress: default,
                logger,
                CancellationToken.None);

            return new DataSourceDoubleStream(totalLength, pipe.Reader);
        }

        public static Task ReadSingleAsync(
            this IDataSourceController controller,
            DateTime begin,
            DateTime end,
            CatalogItem catalogItem,
            PipeWriter dataWriter,
            PipeWriter? statusWriter,
            IProgress<double>? progress,
            ILogger<DataSourceController> logger,
            CancellationToken cancellationToken)
        {
            var samplePeriod = catalogItem.Representation.SamplePeriod;

            var readingGroup = new DataReadingGroup(controller, new CatalogItemPipeWriter[]
            {
                new CatalogItemPipeWriter(catalogItem, dataWriter, statusWriter)
            });

            return DataSourceController.ReadAsync(
                begin,
                end,
                samplePeriod,
                new DataReadingGroup[] { readingGroup },
                progress,
                logger,
                cancellationToken);
        }
    }
}