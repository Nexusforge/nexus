using Nexus.Api;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class FakeResourceCatalogViewModel : ResourceCatalogViewModel
{
    public FakeResourceCatalogViewModel(string id, string parentId, INexusClient client, IAppState appState, Task<IList<string>> childCatalogIdsTask)
        : base(id, parentId, appState)
    {
        Func<Task<List<ResourceCatalogViewModel>>> func = async () => 
        {
            var childCatalogIds = await childCatalogIdsTask;
            return PrepareChildCatalogs(childCatalogIds, id, client, appState);
        };

        ChildrenTask = new Lazy<Task<List<ResourceCatalogViewModel>>>(func);
        CatalogTask = new Lazy<Task<ResourceCatalog>>(() => Task.FromResult(new ResourceCatalog(id, default, default)));
        TimeRangeTask = new Lazy<Task<CatalogTimeRange>>(() => Task.FromResult(new CatalogTimeRange(default, default)));
    }

    private List<ResourceCatalogViewModel> PrepareChildCatalogs(
        IList<string> 
        childCatalogIds,
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
        var groupedIds = childCatalogIds.GroupBy(childId => childId.Substring(id.Length).Split('/', count: 3)[1]);

        foreach (var group in groupedIds)
        {
            if (group.Count() == 1)
            {
                var childId = group.First();
                result.Add(new RealResourceCatalogViewModel(childId, id, client, appState));
            }

            else
            {
                var childId = id + "/" + group.Key;
                var childCatalogIdsTask = Task.FromResult((IList<string>)group.ToList());
                result.Add(new FakeResourceCatalogViewModel(childId, id, client, appState, childCatalogIdsTask));
            }
        }

        result = result
            .OrderBy(catalog => catalog.Id)
            .ToList();

        return result;
    }
}