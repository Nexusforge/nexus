using System.Collections.Generic;

namespace Nexus.Core
{
    public static class Constants
    {
        public static List<string> HiddenProjects
            => new List<string>() { "/IN_MEMORY/TEST/ACCESSIBLE", "/IN_MEMORY/TEST/RESTRICTED" };
    }
}
