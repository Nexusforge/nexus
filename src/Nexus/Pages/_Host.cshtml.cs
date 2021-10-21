using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Nexus.Core;

namespace Nexus.Pages
{
    public class HostModel : PageModel
    {
        [Inject]
        private IOptions<GeneralOptions> GeneralOptions { get; set; }

        public string Language => this.GeneralOptions.Value.Language;
    }
}
