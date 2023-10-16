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
        services.AddSingleton<SharedCounters>();
        services.AddSingleton<SharedCache>();

        services.Configure<ArcadiaSettings>(config.GetSection(nameof(ArcadiaSettings)));
        services.Configure<FeslSettings>(config.GetSection(nameof(FeslSettings)));
        services.Configure<DnsSettings>(config.GetSection(nameof(DnsSettings)));
        services.Configure<ProxySettings>(config.GetSection(nameof(ProxySettings)));
        services.Configure<DebugSettings>(config.GetSection(nameof(DebugSettings)));

        services.AddScoped<Rc4TlsCrypto>();
        services.AddScoped<FeslHandler>();
        services.AddScoped<TheaterHandler>();

        services
            .AddHostedService<DnsHostedService>()
            .AddHostedService<FeslHostedService>()
            .AddHostedService<TheaterHostedService>();

        services.AddTransient<FeslProxy>();
        services.AddTransient<TheaterProxy>();
    })
    .Build();

await host.RunAsync();