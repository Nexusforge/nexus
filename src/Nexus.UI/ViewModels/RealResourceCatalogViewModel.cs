using Nexus.Api;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class RealResourceCatalogViewModel : ResourceCatalogViewModel
{
    public RealResourceCatalogViewModel(string id, string parentId, INexusClient client, IAppState appState)
        : base(id, parentId, appState)
    {
        Func<Task<List<ResourceCatalogViewModel>>> func = async () => 
        {
            var childCatalogIds = await client.Catalogs.GetChildCatalogIdsAsync(id, CancellationToken.None);

            return childCatalogIds
                .Select(childId => (ResourceCatalogViewModel)new RealResourceCatalogViewModel(childId, id, client, appState)).ToList();
        };

        ChildrenTask = new Lazy<Task<List<ResourceCatalogViewModel>>>(func);
        CatalogTask = new Lazy<Task<ResourceCatalog>>(() => client.Catalogs.GetAsync(id, CancellationToken.None));
        TimeRangeTask = new Lazy<Task<CatalogTimeRange>>(() => client.Catalogs.GetTimeRangeAsync(id, CancellationToken.None));

        ReadmeTask = new Lazy<Task<string>>(async () =>
        {
            var streamResponse = await client.Catalogs.GetAttachmentStreamAsync(id, "README.md", CancellationToken.None);
            var stream = await streamResponse.GetStreamAsync(CancellationToken.None);

            return new StreamReader(stream)
                .ReadToEnd();
        });
    }
}