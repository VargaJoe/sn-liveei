using Microsoft.Extensions.Configuration;
using SenseNet.Client;
using System.Net.Http;
using System.Threading.Tasks;
using IdentityModel.Client;

namespace SnLiveExportImport
{
    public class AppConfigRepository
    {
        private readonly IConfiguration _config;
        public string AuthURL { get; set; }
        public string BaseURL { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string Scope { get; set; }
        public string ApiSecretToken { get; set; }
        public bool RequireHttpsMetadata { get; set; }

        public AppConfigRepository(IConfiguration config)
        {
            _config = config;

            Initalize();

            AppLogin().GetAwaiter().GetResult();
        }

        private void Initalize()
        {
            this.ApiSecretToken = _config["ApiSecretToken"];
            this.AuthURL = _config["sensenet:Authentication:Authority"];
            this.BaseURL = _config["sensenet:RepositoryUrl"];
            this.ClientId = _config["sensenet:Authentication:ClientId"];
            this.ClientSecret = _config["sensenet:Authentication:ClientSecret"];
            this.Scope = _config["sensenet:Authentication:Scope"];

            var requireHttpsMetadataString = _config["RequireHttpsMetadata"];

            if (bool.TryParse(requireHttpsMetadataString, out bool requireHttpsMetadata))
            {
                this.RequireHttpsMetadata = requireHttpsMetadata;
            }
        }

        private async Task AppLogin()
        {
            var server = new ServerContext()
            {
                Url = _config["sensenet:RepositoryUrl"]
            };

            server.Authentication.AccessToken = await GetTokenAsync(_config);

            ClientContext.Current.ChunkSizeInBytes = 512000;
            ClientContext.Current.AddServer(server);
        }

        private async Task<string> GetTokenAsync(IConfiguration config)
        {
            var authority = config["sensenet:Authentication:Authority"];

            var client = new HttpClient();
            var disco = await client.GetDiscoveryDocumentAsync(authority);
            if (disco.IsError)
            {
                return string.Empty;
            }

            // request token
            var tokenResponse = await client.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
            {
                Address = disco.TokenEndpoint,

                ClientId = config["sensenet:Authentication:ClientId"],
                ClientSecret = config["sensenet:Authentication:ClientSecret"],
                Scope = config["sensenet:Authentication:Scope"]
            });

            if (tokenResponse.IsError)
            {
                return string.Empty;
            }

            return tokenResponse.AccessToken;
        }
    }
}
