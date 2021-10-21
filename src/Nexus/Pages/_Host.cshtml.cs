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

#warning repair null references
        public string Language => "en"; //this.GeneralOptions.Value.Language;
    }
}
