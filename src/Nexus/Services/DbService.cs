using Microsoft.EntityFrameworkCore;
using Nexus.Core;

namespace Nexus.Services
{
    internal interface IDBService
    {
        IQueryable<NexusUser> GetUsers();
        Task<NexusUser?> FindUserAsync(string userId);
        Task<RefreshToken?> FindTokenAsync(string token);
        Task UpdateUserAsync(NexusUser user);
        Task DeleteUserAsync(string userId);
    }

    internal class DbService : IDBService
    {
        private UserDbContext _context;

        public DbService(
            UserDbContext context)
        {
            _context = context;
        }

        public IQueryable<NexusUser> GetUsers()
        {
            return _context.Users
                .Include(user => user.RefreshTokens);
        }

        public Task<NexusUser?> FindUserAsync(string userId)
        {
            return _context.Users
                .Include(user => user.RefreshTokens)
                .FirstOrDefaultAsync(user => user.Id == userId);
        }

        public async Task<RefreshToken?> FindTokenAsync(string token)
        {
            var userId = Uri.UnescapeDataString(token.Split('@')[0]);

            var user = await _context.Users
               .Where(user => user.Id == userId)
               .Include(user => user.RefreshTokens)
               .FirstOrDefaultAsync();

            if (user is not null)
            {
                var refreshToken = user.RefreshTokens
                    .FirstOrDefault(current => current.Token == token);

                return refreshToken;
            }

            return default;
        }

        public Task UpdateUserAsync(NexusUser user)
        {
            _context.Users.Update(user);
            return _context.SaveChangesAsync();
        }

        public async Task DeleteUserAsync(string userId)
        {
            var user = await FindUserAsync(userId);

            if (user is not null)
                _context.Users.Remove(user);

            await _context.SaveChangesAsync();
        }
    }
}
