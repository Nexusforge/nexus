using Nexus.Api;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class RealResourceCatalogViewModel : ResourceCatalogViewModel
{
    public RealResourceCatalogViewModel(CatalogInfo info, string parentId, INexusClient client, IAppState appState)
        : base(info, parentId, appState)
    {
        var id = Id;

        Func<Task<List<ResourceCatalogViewModel>>> func = async () => 
        {
            var childCatalogInfos = await client.Catalogs.GetChildCatalogInfosAsync(id, CancellationToken.None);

            return childCatalogInfos
                .Where(info => (info.IsReleased && info.IsVisible) || info.IsOwner)
                .Select(childInfo => (ResourceCatalogViewModel)new RealResourceCatalogViewModel(childInfo, id, client, appState))
                .ToList();
        };

        ChildrenTask = new Lazy<Task<List<ResourceCatalogViewModel>>>(func);
        CatalogTask = new Lazy<Task<ResourceCatalog>>(() => client.Catalogs.GetAsync(id, CancellationToken.None));
        TimeRangeTask = new Lazy<Task<CatalogTimeRange>>(() => client.Catalogs.GetTimeRangeAsync(id, CancellationToken.None));
    }
}