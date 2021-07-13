using Microsoft.Extensions.Logging;
using Moq;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Core.Tests
{
    public class BashRpcDataSourceTests
    {
        private ILogger _logger;

        public BashRpcDataSourceTests(ITestOutputHelper xunitLogger)
        {
            _logger = new XunitLoggerProvider(xunitLogger).CreateLogger(nameof(BashRpcDataSourceTests));
        }

        [Fact]
        public async Task CanProvideTimeRange()
        {
            var expectedBegin = new DateTime(2019, 12, 31, 12, 00, 00, DateTimeKind.Utc);
            var expectedEnd = new DateTime(2020, 01, 02, 09, 50, 00, DateTimeKind.Utc);

            var dataSource = new RpcDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
                Configuration = new Dictionary<string, string>()
                {
                    ["command"] = "BashRpcDataSource.sh",
                    ["arguments"] = "55556",
                    ["listen-address"] = "127.0.0.1",
                    ["listen-port"] = "55556",
                },
                Logger = _logger,
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            var actual = await dataSource.GetTimeRangeAsync("/A/B/C", CancellationToken.None);

            Assert.Equal(expectedBegin, actual.Begin);
            Assert.Equal(expectedEnd, actual.End);
        }
    }
}