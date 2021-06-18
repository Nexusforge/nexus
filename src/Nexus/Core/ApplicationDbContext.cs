using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace Nexus.Core
{
    public class ApplicationDbContext : IdentityDbContext
    {
        private NexusOptionsOld _options;

        public ApplicationDbContext(NexusOptionsOld options)
        {
            _options = options;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var filePath = Path.Combine(_options.DataBaseFolderPath, "users.db");
            optionsBuilder.UseSqlite($"Data Source={filePath}");
            base.OnConfiguring(optionsBuilder);
        }
    }
}
