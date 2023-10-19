using Arcadia;
using Arcadia.Handlers;
using Arcadia.Tls.Crypto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Arcadia.Services;
using Microsoft.Extensions.Hosting;
using Arcadia.EA;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Arcadia.Storage;
using Arcadia.EA.Proxy;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .Build();

var host = Host.CreateDefaultBuilder()
    .ConfigureServices((_, services) =>
    {
        services
            .Configure<ArcadiaSettings>(config.GetSection(nameof(ArcadiaSettings)))
            .Configure<FeslSettings>(config.GetSection(nameof(FeslSettings)))
            .Configure<DnsSettings>(config.GetSection(nameof(DnsSettings)))
            .Configure<ProxySettings>(config.GetSection(nameof(ProxySettings)))
            .Configure<DebugSettings>(config.GetSection(nameof(DebugSettings)));

        services
            .AddSingleton<CertGenerator>()
            .AddSingleton<SharedCounters>()
            .AddSingleton<SharedCache>();

        services
            .AddScoped<Rc4TlsCrypto>()
            .AddScoped<FeslHandler>()
            .AddScoped<TheaterHandler>();

        services
            .AddTransient<FeslProxy>()
            .AddTransient<TheaterProxy>();

        services
            .AddHostedService<DnsHostedService>()
            .AddHostedService<FeslHostedService>()
            .AddHostedService<TheaterHostedService>();

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
    })
    .Build();

await host.RunAsync();