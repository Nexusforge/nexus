using Microsoft.AspNetCore.Components;
using Nexus.Core;

namespace Nexus.Shared
{
    public partial class MainLayout
    {
        [Inject]
        public NexusOptions Options { get; set; }
    }
}
