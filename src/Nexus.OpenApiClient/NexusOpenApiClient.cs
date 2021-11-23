using System.Net.Http;

namespace Nexus.Client
{
    public class NexusOpenApiClient
    {
        private CatalogsClient _catalogs;
        private DataClient _data;
        private JobsClient _jobs;
        private UsersClient _users;

        public NexusOpenApiClient(string baseUrl, HttpClient httpClient)
        {
            _catalogs = new CatalogsClient(baseUrl, httpClient);
            _data = new DataClient(baseUrl, httpClient);
            _jobs = new JobsClient(baseUrl, httpClient);
            _users = new UsersClient(baseUrl, httpClient);
        }

        public void SetBearerToken(string token)
        {
            _catalogs.SetBearerToken(token);
            _data.SetBearerToken(token);
            _jobs.SetBearerToken(token);
            _users.SetBearerToken(token);
        }

        /// <summary>
        /// Gets the catalogs client.
        /// </summary>
        public ICatalogsClient Catalogs => _catalogs;

        /// <summary>
        /// Gets the data client.
        /// </summary>
        public IDataClient Data => _data;

        /// <summary>
        /// Gets the jobs client.
        /// </summary>
        public IJobsClient Jobs => _jobs;

        /// <summary>
        /// Gets the account client.
        /// </summary>
        public IUsersClient Users => _users;
    }
}