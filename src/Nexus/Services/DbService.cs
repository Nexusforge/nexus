using Microsoft.EntityFrameworkCore;
using Nexus.Core;

namespace Nexus.Services
{
    internal interface IDBService
    {
        IQueryable<NexusUser> GetUsers();
        Task<NexusUser?> FindByIdAsync(string userId);
        Task<NexusUser?> FindByTokenAsync(string token);
        Task UpdateUserAsync(NexusUser user);
    }

    internal class DbService : IDBService
    {
        UserDbContext _context;

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

        public Task<NexusUser?> FindByIdAsync(string userId)
        {
            return _context.Users
                .Include(user => user.RefreshTokens)
                .FirstOrDefaultAsync(user => user.Id == userId);
        }

        public Task<NexusUser?> FindByTokenAsync(string token)
        {
            return _context.Users
                .Include(user => user.RefreshTokens)
                .FirstOrDefaultAsync(user => user.RefreshTokens.Any(current => current.Token == token));
        }

        public Task UpdateUserAsync(NexusUser user)
        {
            _context.Users.Update(user);
            return _context.SaveChangesAsync();
        }
    }
}
