namespace Nexus.DataModel
{
    public class SparseCatalog : Catalog
    {
        #region "Constructors"

        public SparseCatalog(string id, CatalogLicense license) : base(id)
        {
            this.License = license;
        }

        #endregion

        #region Properties

        public CatalogLicense License { get; }

        #endregion
    }
}
