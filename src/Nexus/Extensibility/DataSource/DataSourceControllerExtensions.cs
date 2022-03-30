using Nexus.Core;
using Nexus.DataModel;
using Nexus.Utilities;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    internal static class DataSourceControllerExtensions
    {
        public static DataSourceDoubleStream ReadAsStream(
            this IDataSourceController controller,
            DateTime begin,
            DateTime end,
            CatalogItemRequest request,
            GeneralOptions generalOptions,
            ILogger<DataSourceController> logger)
        {
            // DataSourceDoubleStream is only required to enable the browser to determine the download progress.
            // Otherwise the PipeReader.AsStream() would be sufficient.

            var samplePeriod = request.Item.Representation.SamplePeriod;
            var elementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, samplePeriod);
            var totalLength = elementCount * NexusCoreUtilities.SizeOf(NexusDataType.FLOAT64);
            var pipe = new Pipe();

            _ = controller.ReadSingleAsync(
                begin,
                end,
                request,
                pipe.Writer,
                generalOptions,
                progress: default,
                logger,
                CancellationToken.None);

            return new DataSourceDoubleStream(totalLength, pipe.Reader);
        }

        public static Task ReadSingleAsync(
            this IDataSourceController controller,
            DateTime begin,
            DateTime end,
            CatalogItemRequest request,
            PipeWriter dataWriter,
            GeneralOptions generalOptions,
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
                generalOptions,
                progress,
                logger,
                cancellationToken);
        }
    }
}