using Markdig;
using Microsoft.AspNetCore.Components;
using System.Threading.Tasks;

namespace Nexus.Shared
{
    public partial class MarkdownRenderer
    {
        private static MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        [Parameter]
        public string MarkdownString { get; set; }

        private string MarkupString { get; set; }

        protected override Task OnInitializedAsync()
        {
            this.MarkupString = Markdown.ToHtml(this.MarkdownString, _pipeline);
            return Task.CompletedTask;
        }
    }
}
