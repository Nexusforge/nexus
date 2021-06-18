using Microsoft.AspNetCore.Components;
using Nexus.Core;

namespace Nexus.Shared
{
    public partial class MainLayout
    {
        [Inject]
        public NexusOptionsOld Options { get; set; }
    }
}
