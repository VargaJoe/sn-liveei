using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SenseNet.Client;
using SenseNet.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SnLiveExportImport
{
    public class AppConfigRepository
    {
        private readonly IConfiguration _config;
        public string AuthURL { get; set; }
        public string BaseURL { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string ApiKey { get; set; }
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
        public string XsltPath { get; set; }
        public string[] FileTypes { get; set; }
        public string[] ExcludedContentTypes { get; set; }
        public string[] ExcludedImportFields { get; set; }
        public string[] ExcludedExportFields { get; set; }
        public IRepository Repository { get; private set; }


        public AppConfigRepository(IConfiguration config)
        {
            _config = config;

            Initalize();

            AppLogin().GetAwaiter().GetResult();
        }

        private void Initalize()
        {
            this.ApiSecretToken = _config["ApiSecretToken"];
            this.ApiKey = _config["sensenet:Authentication:ApiKey"];
            if (string.IsNullOrWhiteSpace(this.ApiKey))
                this.ApiKey = this.ApiSecretToken;

            this.AuthURL = _config["sensenet:Authentication:Authority"];
            this.BaseURL = _config["sensenet:RepositoryUrl"];
            this.ClientId = _config["sensenet:Authentication:ClientId"];
            this.ClientSecret = _config["sensenet:Authentication:ClientSecret"];
            this.Scope = _config["sensenet:Authentication:Scope"];

            this.Mode = _config["sensenet:LiveExportImport:Mode"];
            this.RepoPath = _config["sensenet:LiveExportImport:RepoPath"];
            this.LocalPath = _config["sensenet:LiveExportImport:LocalPath"];
            this.XsltPath = _config["sensenet:LiveExportImport:XsltPath"];

            if (bool.TryParse(_config["sensenet:LiveExportImport:SyncMode"], out bool syncmode))
                this.SyncMode = syncmode;

            if (bool.TryParse(_config["sensenet:LiveExportImport:TreeExport"], out bool all))
                this.TreeExport = all;

            this.FileTypes = _config.GetSection("sensenet:LiveExportImport:FileTypes").Get<string[]>();
            this.ExcludedContentTypes = _config.GetSection("sensenet:LiveExportImport:ExcludedContentTypes").Get<string[]>() ?? Array.Empty<string>();
            var configuredExcludedImportFields = _config.GetSection("sensenet:LiveExportImport:ExcludedImportFields").Get<string[]>() ?? Array.Empty<string>();
            var defaultExcludedImportFields = new[]
            {
                // Frequently server-managed fields that may cause save/update issues during migration import.
                "Hidden",
                "Index",
                "EnableLifespan",
                "ValidFrom",
                "ValidTill"
            };
            this.ExcludedImportFields = configuredExcludedImportFields
                .Concat(defaultExcludedImportFields)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            this.ExcludedExportFields = _config.GetSection("sensenet:LiveExportImport:ExcludedExportFields").Get<string[]>();
            
            var requireHttpsMetadataString = _config["RequireHttpsMetadata"];

            if (bool.TryParse(requireHttpsMetadataString, out bool requireHttpsMetadata))
            {
                this.RequireHttpsMetadata = requireHttpsMetadata;
            }
        }

        private Task AppLogin()
        {
            var server = new ServerContext()
            {
                Url = this.BaseURL
            };

            if (string.IsNullOrWhiteSpace(this.ApiKey))
                throw new InvalidOperationException("Missing authentication configuration. Set sensenet:Authentication:ApiKey (or legacy ApiSecretToken) in appsettings/environment variables.");

            server.Authentication.ApiKey = this.ApiKey;

            // Build a repository instance through the supported DI pipeline.
            var services = new ServiceCollection();
            services.AddSenseNetClient()
                .ConfigureSenseNetRepository(options =>
                {
                    options.Url = this.BaseURL;
                    options.Authentication.ApiKey = this.ApiKey;
                });
            var serviceProvider = services.BuildServiceProvider();
            var repositoryCollection = serviceProvider.GetRequiredService<IRepositoryCollection>();
            this.Repository = repositoryCollection.GetRepositoryAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();

            ClientContext.Current.ChunkSizeInBytes = 512000;
            ClientContext.Current.AddServer(server);

            return Task.CompletedTask;
        }
    }
}
