using Microsoft.Extensions.Logging.Abstractions;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Writers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace DataWriter
{
    public class CsvDataWriterTests : IClassFixture<DataWriterFixture>
    {
        private DataWriterFixture _fixture;

        public CsvDataWriterTests(DataWriterFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]
        [InlineData("Index")]
        [InlineData("Unix")]
        [InlineData("Excel")]
        [InlineData("ISO 8601")]
        public async Task CanWriteFiles(string rowIndexFormat)
        {
            var targetFolder = _fixture.GetTargetFolder();
            var dataWriter = new Csv() as IDataWriter;

            var context = new DataWriterContext(
                ResourceLocator: new Uri(targetFolder),
                Configuration: new Dictionary<string, string>()
                {
                    ["RowIndexFormat"] = rowIndexFormat,
                    ["SignificantFigures"] = "7"
                },
                Logger: NullLogger.Instance);

            await dataWriter.SetContextAsync(context, CancellationToken.None);

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var samplePeriod = TimeSpan.FromSeconds(1);

            var catalogItems = _fixture.Catalogs.SelectMany(catalog => catalog.Resources
                .SelectMany(resource => resource.Representations.Select(representation => new CatalogItem(catalog, resource, representation))))
                .ToArray();

            var random = new Random(Seed: 1);

            var length = 1000;

            var data = new[]
            {
                Enumerable
                    .Range(0, length)
                    .Select(value => random.NextDouble() * 1e4)
                    .ToArray(),

                Enumerable
                    .Range(0, length)
                    .Select(value => random.NextDouble() * -1)
                    .ToArray(),

                Enumerable
                    .Range(0, length)
                    .Select(value => random.NextDouble() * Math.PI)
                    .ToArray()
            };

            var requests = catalogItems
                .Select((catalogItem, i) => new WriteRequest(catalogItem, data[i]))
                .ToArray();

            await dataWriter.OpenAsync(begin, default, samplePeriod, catalogItems, CancellationToken.None);
            await dataWriter.WriteAsync(TimeSpan.Zero, requests, new Progress<double>(), CancellationToken.None);
            await dataWriter.WriteAsync(TimeSpan.FromSeconds(length), requests, new Progress<double>(), CancellationToken.None);
            await dataWriter.CloseAsync(CancellationToken.None);

            var actualFilePaths = Directory
                .GetFiles(targetFolder)
                .OrderBy(value => value)
                .ToArray();

            var nfi = new NumberFormatInfo()
            {
                NumberDecimalSeparator = ".",
                NumberGroupSeparator = string.Empty
            };

            var expected = Enumerable
                .Range(0, 4)
                .Select(value =>
                {
                    return rowIndexFormat switch
                    {
                        "Index" => ("index", "1999", string.Format(nfi, "{0:N0}", value)),
                        "Unix" => ("Unix time", "1577838799.00000", string.Format(nfi, "{0:N5}", (begin.AddSeconds(value) - new DateTime(1970, 01, 01)).TotalSeconds)),
                        "Excel" => ("Excel time", "43831.023136574", string.Format(nfi, "{0:N9}", begin.AddSeconds(value).ToOADate())),
                        "ISO 8601" => ("ISO 8601 time", "2020-01-01T00:33:19.0000000Z", begin.AddSeconds(value).ToString("o")),
                        _ => throw new Exception($"Row index format '{rowIndexFormat}' is not supported.")
                    };
                })
                .ToArray();

            // assert /A/B/C
            var expectedLines1 = new[]
            {
                "# format_version=1;",
                "# system_name=Nexus;",
                "# date_time=2020-01-01T00-00-00Z;",
                "# sample_period=1_s;",
                "# catalog_id=/A/B/C;",
                "# my-custom-parameter1=my-custom-value1;",
                "# my-custom-parameter2=my-custom-value2;",
                $"{expected[0].Item1};resource1;resource1;",
                "-;1_s_mean;1_s_max;",
                "-;°C;°C;",
                $"{expected[0].Item3};2486.686;-0.7557958;",
                $"{expected[1].Item3};1107.44;-0.4584072;",
                $"{expected[2].Item3};4670.107;-0.001267695;",
                $"{expected[3].Item3};7716.041;-0.09289372;"
            };

            var actualLines1 = File.ReadLines(actualFilePaths[0], Encoding.UTF8).ToList();

            Assert.Equal("A_B_C_2020-01-01T00-00-00Z_1_s.csv", Path.GetFileName(actualFilePaths[0]));
            Assert.Equal($"{expected[0].Item2};412.6589;-0.7542502;", actualLines1.Last());
            Assert.Equal(2010, actualLines1.Count);

            foreach (var (expectedLine, actualLine) in expectedLines1.Zip(actualLines1.Take(14)))
            {
                Assert.Equal(expectedLine, actualLine);
            }

            // assert /D/E/F
            var expectedLines2 = new[]
            {
                "# format_version=1;",
                "# system_name=Nexus;",
                "# date_time=2020-01-01T00-00-00Z;",
                "# sample_period=1_s;",
                "# catalog_id=/D/E/F;",
                "# my-custom-parameter3=my-custom-value3;",
                $"{expected[0].Item1};resource3;",
                "-;1_s_std;",
                "-;m/s;",
                $"{expected[0].Item3};1.573993;",
                $"{expected[1].Item3};0.4618637;",
                $"{expected[2].Item3};1.094448;",
                $"{expected[3].Item3};2.758635;"
            };

            var actualLines2 = File.ReadLines(actualFilePaths[1], Encoding.UTF8).ToList();

            Assert.Equal("D_E_F_2020-01-01T00-00-00Z_1_s.csv", Path.GetFileName(actualFilePaths[1]));
            Assert.Equal($"{expected[0].Item2};2.336974;", actualLines2.Last());
            Assert.Equal(2009, actualLines2.Count);

            foreach (var (expectedLine, actualLine) in expectedLines2.Zip(actualLines2.Take(13)))
            {
                Assert.Equal(expectedLine, actualLine);
            }
        }
    }
}