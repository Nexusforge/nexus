using Nexus.Api;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class FakeResourceCatalogViewModel : ResourceCatalogViewModel
{
    public FakeResourceCatalogViewModel(CatalogInfo info, string parentId, INexusClient client, IAppState appState, Task<IList<CatalogInfo>> childCatalogInfosTask)
        : base(info, parentId, appState)
    {
        var id = Id;

        Func<Task<List<ResourceCatalogViewModel>>> func = async () => 
        {
            var childCatalogInfo = await childCatalogInfosTask;
            return PrepareChildCatalogs(childCatalogInfo, id, client, appState);
        };

        ChildrenTask = new Lazy<Task<List<ResourceCatalogViewModel>>>(func);
        CatalogTask = new Lazy<Task<ResourceCatalog>>(() => Task.FromResult(new ResourceCatalog(id, default, default)));
        TimeRangeTask = new Lazy<Task<CatalogTimeRange>>(() => Task.FromResult(new CatalogTimeRange(default, default)));
    }

    private List<ResourceCatalogViewModel> PrepareChildCatalogs(
        IList<CatalogInfo> childCatalogInfos,
        string id,
        INexusClient client,
        IAppState appState)
    {
        /* This methods creates intermediate fake catalogs (marked with a *) 
         * to group child catalogs. Example:
         *
         *   /A/A/A
         *   /A/A/B
         *   /A/B
         *   -> /* + /A* (/A/A/A, /A/A/B, /A/B) + /A/A* (/A/A/A, /A/A/B) + /A/A/A, /A/A/B, /A/B
         */

        id = id == "/" ? "" : id;

        var result = new List<ResourceCatalogViewModel>();

        var groupedPublishedInfos = childCatalogInfos
            .Where(info => info.IsReleased && info.IsVisible)
            .GroupBy(childInfo => childInfo.Id.Substring(id.Length).Split('/', count: 3)[1]);

        foreach (var group in groupedPublishedInfos)
        {
            var filteredInfos = group
                .Where(info => (info.IsReleased && info.IsVisible) || info.IsOwner)
                .ToList();

            if (!filteredInfos.Any())
            {
                // do nothing
            }

            else if (filteredInfos.Count == 1)
            {
                var childInfo = filteredInfos.First();
                result.Add(new RealResourceCatalogViewModel(childInfo, id, client, appState));
            }

            else
            {
                var childId = id + "/" + group.Key;

                var childInfo = new CatalogInfo(
                    Id: childId,
                    Title: default!, 
                    Contact: default, 
                    License: default,
                    SourceProjectUrl: default,
                    SourceRepositoryUrl: default,
                    IsReadable: true,
                    IsWritable: false, 
                    IsReleased: true,
                    IsVisible: true,
                    IsOwner: false);

                var childCatalogInfosTask = Task.FromResult((IList<CatalogInfo>)filteredInfos);
                result.Add(new FakeResourceCatalogViewModel(childInfo, id, client, appState, childCatalogInfosTask));
            }
        }

        result = result
            .OrderBy(catalog => catalog.Id)
            .ToList();

        return result;
    }
}