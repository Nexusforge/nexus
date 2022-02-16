using Microsoft.EntityFrameworkCore;

namespace Nexus.Core
{
    internal class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
            //
        }

        public DbSet<NexusUser> Users { get; set; } = null!;
    }
}
