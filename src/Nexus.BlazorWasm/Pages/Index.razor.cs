using Nexus.Client;

namespace Nexus.BlazorWasm.Pages
{
    public partial class Index
    {
        private string Catalog { get; set; } = null!;

        protected override async Task OnInitializedAsync()
        {
            var httpClient = new HttpClient();
            var client = new NexusOpenApiClient("https://localhost:8443", httpClient);

            await client.PasswordSignInAsync("root@nexus.localhost", "#root0/User1");
            //var catalog = await client.Catalogs.GetCatalogAsync("/IN_MEMORY/TEST/RESTRICTED");
            //this.Catalog = JsonSerializer.Serialize(catalog);

            var job = await client.Jobs.LoadPackagesAsync();
            JobStatus? jobStatus;

            do
            {
                jobStatus = await client.Jobs.GetJobStatusAsync(job.Id);
                Console.WriteLine($"Progress = {jobStatus.Progress * 100} %");
            } while (jobStatus.Status != Client.TaskStatus.RanToCompletion);

            Console.WriteLine("Done");
        }
    }
}
