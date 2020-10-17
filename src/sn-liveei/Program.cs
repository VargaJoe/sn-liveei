using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace SnLiveExportImport
{
    public class Program
    {
        public static IConfigurationRoot configuration;
        public static AppConfigRepository _appConfig;
        public static string LogFolderPath = System.IO.Directory.GetCurrentDirectory() + "/LiveEIAppLog";
        public static string LogFileName = "liveei_log_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".txt";

        public static int Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.File(LogFileName)
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}", theme: AnsiConsoleTheme.Literate)
                .CreateLogger();

            configuration = new ConfigurationBuilder()
              .SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName)
              .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
              .AddEnvironmentVariables().Build();
            
            _appConfig = new AppConfigRepository(configuration);

            try
            {
                Log.Information("Starting host...");

                switch (_appConfig.Mode)
                {
                    case "import":
                        LiveImport.ImportContent(); 
                        break;
                    case "export":
                        LiveExport.StartExport();
                        break;
                    default:
                        break;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly.");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}

