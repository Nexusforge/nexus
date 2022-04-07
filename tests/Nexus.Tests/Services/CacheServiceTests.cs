using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Services;
using Xunit;

namespace Services
{
    public class CacheServiceTests
    {
        [Fact]
        public void CanProvideCachedData()
        {
            // Arrange

            var options = Options.Create(new PathsOptions());
            var cacheService = new CacheService(options);

            // Act
            

            // Assert
            
        }
    }
}
