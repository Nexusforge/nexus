using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        [Fact]
        public async Task CanWrite()
        {
            var dataWriter = Mock.Of<IDataWriter>();

            var backendSource = new BackendSource()
            {
                ResourceLocator = new Uri("file:///empty"),
                Configuration = new Dictionary<string, string>()
            };

            var controller = new DataWriterController(dataWriter, backendSource, NullLogger.Instance);
            await controller.InitializeAsync(CancellationToken.None);

            var begin = new DateTime(2020, 01, 01, 1, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 01, 3, 0, 0, DateTimeKind.Utc);
            var samplePeriod = TimeSpan.FromMinutes(10);
            var filePeriod = TimeSpan.FromMinutes(30);

            var catalogItems = _fixture.Catalogs
                .SelectMany(catalog => catalog.Resources
                .SelectMany(resource => resource.Representations
                .Select(representation => new CatalogItem(catalog, resource, representation with { Id = "10 min_mean" }))))
                .ToArray();

            var pipes = catalogItems
                .Select(catalogItem => new Pipe())
                .ToArray();

            var catalogItemPipeReaders = catalogItems
                .Zip(pipes)
                .Select((value) => new CatalogItemPipeReader(value.First, value.Second.Reader))
                .ToArray();

            var random = new Random(Seed: 1);
            var totalLength = (end - begin).Ticks / samplePeriod.Ticks;

            var expectedDatasets = pipes
                .Select(pipe => Enumerable.Range(0, (int)totalLength).Select(value => random.NextDouble()).ToArray())
                .ToArray();

            var actualDatasets = pipes
               .Select(pipe => Enumerable.Range(0, (int)totalLength).Select(value => 0.0).ToArray())
               .ToArray();

            // prepare mock
            Mock.Get(dataWriter)
                .Setup(s => s.WriteAsync(
                    It.IsAny<TimeSpan>(),
                    It.IsAny<WriteRequest[]>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>())
                )
                .Callback<TimeSpan, WriteRequest[], IProgress<double>, CancellationToken>((fileOffset, requests, progress, cancellationToken) =>
                {
                    var fileElementOffset = (int)(fileOffset.Ticks / samplePeriod.Ticks);

                    foreach (var ((catalogItem, source), target) in requests.Zip(actualDatasets))
                    {
                        source.Span.CopyTo(target.AsSpan(fileElementOffset));
                    }
                })
                .Returns(Task.CompletedTask);

            // go
            var chunk = 2;

            var reading = Task.Run(async () =>
            {
                var remaining = totalLength;
                var offset = 0;

                while (remaining > 0)
                {
                    var currentChunk = (int)Math.Min(remaining, chunk);

                    foreach (var (pipe, dataset) in pipes.Zip(expectedDatasets))
                    {
                        var buffer = dataset.AsMemory().Slice(offset, currentChunk).Cast<double, byte>();
                        await pipe.Writer.WriteAsync(buffer);
                        await pipe.Writer.FlushAsync();

                        //pipe.Writer.Advance(buffer.Length); // only if using writer.GetMemory!!
                    }

                    remaining -= currentChunk;
                    offset += currentChunk;
                }

                foreach (var pipe in pipes)
                {
                    await pipe.Writer.CompleteAsync();
                }
            });

            var writing = controller.WriteAsync(begin, end, samplePeriod, filePeriod, catalogItemPipeReaders, default, CancellationToken.None);

            await Task.WhenAll(writing, reading);

            // assert
            var begin1 = new DateTime(2020, 01, 01, 1, 0, 0, DateTimeKind.Utc);
            Mock.Get(dataWriter).Verify(dataWriter => dataWriter.OpenAsync(begin1, samplePeriod, catalogItems, default), Times.Once);

            var begin2 = new DateTime(2020, 01, 01, 1, 30, 0, DateTimeKind.Utc);
            Mock.Get(dataWriter).Verify(dataWriter => dataWriter.OpenAsync(begin2, samplePeriod, catalogItems, default), Times.Once);

            var begin3 = new DateTime(2020, 01, 01, 2, 0, 0, DateTimeKind.Utc);
            Mock.Get(dataWriter).Verify(dataWriter => dataWriter.OpenAsync(begin3, samplePeriod, catalogItems, default), Times.Once);
            
            var begin4 = new DateTime(2020, 01, 01, 2, 30, 0, DateTimeKind.Utc);
            Mock.Get(dataWriter).Verify(dataWriter => dataWriter.OpenAsync(begin4, samplePeriod, catalogItems, default), Times.Once);

            foreach (var (expected, actual) in expectedDatasets.Zip(actualDatasets))
            {
                Assert.True(expected.SequenceEqual(actual));
            }
        }
    }
}
