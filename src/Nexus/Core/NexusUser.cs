#pragma warning disable CS1591 // Fehledes XML-Kommentar für öffentlich sichtbaren Typ oder Element

using Nexus.Services;
using System.ComponentModel.DataAnnotations;

namespace Nexus.Core
{
    public class NexusUser
    {
        [Key]
        public string Id { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string Scheme { get; set; } = default!;
        public List<NexusClaim> Claims { get; set; } = default!;
        public List<RefreshToken> RefreshTokens { get; set; } = default!;
    }
}

#pragma warning restore CS1591 // Fehledes XML-Kommentar für öffentlich sichtbaren Typ oder Element
