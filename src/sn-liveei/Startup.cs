using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace SnLiveExportImport
{
    public class Startup
    {
        public IWebHostEnvironment Environment { get; }
        public IConfiguration Configuration { get; }

        public Startup(IWebHostEnvironment environment, IConfiguration configuration)
        {
            Environment = environment;
            Configuration = configuration;
        }

        

    }
}