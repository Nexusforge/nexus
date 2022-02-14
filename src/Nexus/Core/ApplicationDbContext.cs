using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Nexus.Core
{
    internal class ApplicationDbContext : DbContext
    {
        private PathsOptions _pathsOptions;

        public ApplicationDbContext(IOptions<PathsOptions> pathsOptions)
        {
            _pathsOptions = pathsOptions.Value;
        }

        public DbSet<NexusUser> Users { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var filePath = Path.Combine(_pathsOptions.Config, "users.db");
            optionsBuilder.UseSqlite($"Data Source={filePath}");
            base.OnConfiguring(optionsBuilder);
        }
    }
}
