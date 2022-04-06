namespace Nexus.Services
{
    internal interface ICacheService
    {
        Task<bool> IsInCacheAsync(
            DateTime begin,
            DateTime end);

        Task LoadAsync(
            Memory<double> targetBuffer);
    }

    internal class CacheService : ICacheService
    {
        public Task<bool> IsInCacheAsync(DateTime begin, DateTime end)
        {
            throw new NotImplementedException();
        }

        public Task LoadAsync(Memory<double> targetBuffer)
        {
            throw new NotImplementedException();
        }
    }
}
