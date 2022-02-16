using Microsoft.EntityFrameworkCore;
using Nexus.Core;

namespace Nexus.Services
{
    internal interface IDBService
    {
        IQueryable<NexusUser> GetUsers();
        Task<NexusUser?> FindByIdAsync(string userId);
        Task<NexusUser?> FindByTokenAsync(string token);
        void UpdateUser(NexusUser user);
    }

    internal class DbService : IDBService
    {
        ApplicationDbContext _context;

        public DbService(
            ApplicationDbContext context)
        {
            _context = context;
        }

        public Task<NexusUser?> FindByIdAsync(string userId)
        {
           return _context.Users
                .Include(user => user.RefreshTokens)
                .FirstOrDefaultAsync(user => user.UserId == userId);
        }

        public Task<NexusUser?> FindByTokenAsync(string token)
        {
            return _context.Users
                 .Include(user => user.RefreshTokens)
                 .FirstOrDefaultAsync(user => user.RefreshTokens.Any(current => current.Token == token));
        }

        public IQueryable<NexusUser> GetUsers()
        {
            return _context.Users;
        }

        public void UpdateUser(NexusUser user)
        {
            _context.Users.Update(user);
        }
    }
}
