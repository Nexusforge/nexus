using Microsoft.Extensions.Logging.Abstractions;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Nexus.Core.Tests
{
    public class DataWriterControllerTests : IClassFixture<DataWriterFixture>
    {
        private DataWriterFixture _fixture;

        public DataWriterControllerTests(DataWriterFixture fixture)
        {
            _fixture = fixture;
        }

        public async Task CanWrite()
        {
            var targetFolder = _fixture.GetTargetFolder();
            var dataWriter = new CsvDataWriter() as IDataWriter;

            var backendSource = new BackendSource()
            {
                ResourceLocator = new Uri(targetFolder),
                Configuration = new Dictionary<string, string>()
            };

            var controller = new DataWriterController(dataWriter, backendSource, NullLogger.Instance);
            await controller.InitializeAsync(CancellationToken.None);

            var begin = new DateTime(01, 01, 2020, 1, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(01, 01, 2020, 2, 0, 0, DateTimeKind.Utc);
            var samplePeriod = TimeSpan.FromMinutes(10);
            var filePeriod = TimeSpan.FromMinutes(30);

            var catalogItems = _fixture.Catalogs
                .SelectMany(catalog => catalog.Resources
                .SelectMany(resource => resource.Representations
                .Select(representation => new CatalogItem(catalog, resource, representation))));

            var pipes = catalogItems
                .Select(catalogItem => new Pipe());

            var catalogItemPipeReaders = catalogItems
                .Zip(pipes)
                .Select((value) => new CatalogItemPipeReader(value.First, value.Second.Reader))
                .ToArray();

            var reading = Task.Run(async () =>
            {
#error Todo: Implement

                foreach (var pipe in pipes)
                {
                    // or as stream
                    await pipe.Writer.WriteAsync();
                    await pipe.Writer.FlushAsync();
                    pipe.Writer.Advance();
                }
            });

            var writing = controller.WriteAsync(begin, end, samplePeriod, filePeriod, catalogItemPipeReaders, default, CancellationToken.None);

            await Task.WhenAll(writing, reading);
        }
    }
}
