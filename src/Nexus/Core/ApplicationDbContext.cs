using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Nexus.Core
{
    internal class ApplicationDbContext : IdentityDbContext<NexusUser, IdentityRole, string>
    {
        private PathsOptions _pathsOptions;

        public ApplicationDbContext(IOptions<PathsOptions> pathsOptions)
        {
            _pathsOptions = pathsOptions.Value;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var filePath = Path.Combine(_pathsOptions.Config, "users.db");
            optionsBuilder.UseSqlite($"Data Source={filePath}");
            base.OnConfiguring(optionsBuilder);
        }
    }
}
