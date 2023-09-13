using Arcadia;
using Arcadia.Fesl;
using Arcadia.Tls.Crypto;
using Arcadia.Theater;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Arcadia.Services;
using Microsoft.Extensions.Hosting;
using Arcadia.EA;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .Build();

var host = Host.CreateDefaultBuilder()
    .ConfigureServices((_, services) =>
    {
        services.AddLogging(log =>
        {
            log.ClearProviders();
            log.AddSimpleConsole(x => {
                x.IncludeScopes = false;
                x.SingleLine = true;
                x.TimestampFormat = "[HH:mm:ss::fff] ";
                x.ColorBehavior = LoggerColorBehavior.Enabled;
            });
        });

        services.AddSingleton<CertGenerator>();

        services.Configure<ArcadiaSettings>(config.GetSection(nameof(ArcadiaSettings)));
        services.Configure<FeslSettings>(config.GetSection(nameof(FeslSettings)));

        services.AddScoped(_ => new Rc4TlsCrypto(true));
        services.AddScoped<FeslHandler>();
        services.AddScoped<TheaterHandler>();

        services
            .AddHostedService<FeslHostedService>()
            .AddHostedService<TheaterHostedService>();
    })
    .Build();

await host.RunAsync();