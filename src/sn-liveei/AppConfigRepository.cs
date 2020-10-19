using Microsoft.Extensions.Configuration;
using SenseNet.Client;
using System.Net.Http;
using System.Threading.Tasks;
using IdentityModel.Client;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;

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

        public const string _contentTypesFolderPath = "/Root/System/Schema/ContentTypes";
        public string ContentTypesFolderPath { get { return _contentTypesFolderPath;  } }

        public string Mode { get; set; }
        public string RepoPath { get; set; }
        public string LocalPath { get; set; }
        public bool SyncMode { get; set; }
        public bool TreeExport { get; set; }
        public string ContinueFrom { get; set; }
        public string[] FileTypes { get; set; }
        public string[] ExcludedImportFields { get; set; }
        public string[] ExcludedExportFields { get; set; }


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

            this.Mode = _config["sensenet:LiveExportImport:Mode"];
            this.RepoPath = _config["sensenet:LiveExportImport:RepoPath"];
            this.LocalPath = _config["sensenet:LiveExportImport:LocalPath"];

            if (bool.TryParse(_config["sensenet:LiveExportImport:SyncMode"], out bool syncmode))
                this.SyncMode = syncmode;

            if (bool.TryParse(_config["sensenet:LiveExportImport:TreeExport"], out bool all))
                this.TreeExport = all;

            this.FileTypes = _config.GetSection("sensenet:LiveExportImport:FileTypes").Get<string[]>();
            this.ExcludedImportFields = _config.GetSection("sensenet:LiveExportImport:ExcludedImportFields").Get<string[]>();
            this.ExcludedExportFields = _config.GetSection("sensenet:LiveExportImport:ExcludedExportFields").Get<string[]>();
            
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
