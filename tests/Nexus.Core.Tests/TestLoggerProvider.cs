using Microsoft.Extensions.Logging;

namespace Nexus.Core.Tests
{
    public class TestLoggerProvider : ILoggerProvider
    {
        private ILogger _logger;

        public TestLoggerProvider(ILogger logger)
        {
            _logger = logger;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _logger;
        }

        public void Dispose()
        {
            //
        }
    }
}
