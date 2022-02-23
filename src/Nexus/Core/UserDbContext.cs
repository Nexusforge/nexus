using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace Nexus.Core
{
    internal class UserDbContext : DbContext
    {
        public UserDbContext(DbContextOptions<UserDbContext> options)
            : base(options)
        {
            //
        }

        public DbSet<NexusUser> Users { get; set; } = null!;
    }
}
