using Markdig;
using Microsoft.AspNetCore.Components;
using System.Threading.Tasks;

namespace Nexus.Shared
{
    public partial class MarkdownRenderer
    {
        [Parameter]
        public string MarkdownString { get; set; }

        protected override Task OnInitializedAsync()
        {
            this.MarkdownString = Markdown.ToHtml(this.MarkdownString);
            return Task.CompletedTask;
        }
    }
}
