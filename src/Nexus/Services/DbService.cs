using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nexus.Core;
using System.Security.Claims;

namespace Nexus.Services
{
    internal interface IDBService
    {
        IQueryable<NexusUser> GetUsers();
        Task<NexusUser?> FindByIdAsync(string userId);
        Task<NexusUser?> FindByTokenAsync(string token);
        Task<bool> IsEmailConfirmedAsync(NexusUser user);
        Task<SignInResult> CheckPasswordSignInAsync(NexusUser user, string password, bool lockoutOnFailure);
        Task<IdentityResult> UpdateAsync(NexusUser user);
        Task<IList<Claim>> GetClaimsAsync(NexusUser user);
    }

    internal class DbService : IDBService
    {
        ApplicationDbContext _context;
        SignInManager<NexusUser> _signInManager;

        public DbService(
            ApplicationDbContext context,
            SignInManager<NexusUser> signInManager)
        {
            _context = context;
            _signInManager = signInManager;
        }

        public Task<SignInResult> CheckPasswordSignInAsync(NexusUser user, string password, bool lockoutOnFailure)
        {
            return _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure);
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

        public Task<IList<Claim>> GetClaimsAsync(NexusUser user)
        {
            return _signInManager.UserManager.GetClaimsAsync(user);
        }

        public IQueryable<NexusUser> GetUsers()
        {
            return _signInManager.UserManager.Users;
        }

        public Task<bool> IsEmailConfirmedAsync(NexusUser user)
        {
            return _signInManager.UserManager.IsEmailConfirmedAsync(user);
        }

        public Task<IdentityResult> UpdateAsync(NexusUser user)
        {
            return _signInManager.UserManager.UpdateAsync(user);
        }
    }
}
