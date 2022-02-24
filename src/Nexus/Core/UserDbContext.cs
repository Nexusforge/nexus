using Microsoft.EntityFrameworkCore;

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
