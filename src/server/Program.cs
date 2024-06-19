using Arcadia;
using Arcadia.Handlers;
using Arcadia.Tls.Crypto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Arcadia.Hosting;
using Microsoft.Extensions.Hosting;
using Arcadia.EA;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Arcadia.Storage;
using NReco.Logging.File;

var host = Host.CreateDefaultBuilder()
    .ConfigureAppConfiguration((_, config) => config
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    )
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;
        
        services
            .Configure<ArcadiaSettings>(config.GetSection(nameof(ArcadiaSettings)))
            .Configure<DnsSettings>(config.GetSection(nameof(DnsSettings)))
            .Configure<DebugSettings>(config.GetSection(nameof(DebugSettings)));

        services
            .AddSingleton<ProtoSSL>()
            .AddSingleton<SharedCounters>()
            .AddSingleton<SharedCache>();

        services
            .AddScoped<Rc4TlsCrypto>()
            .AddScoped<IEAConnection, EAConnection>()
            .AddScoped<FeslServerHandler>()
            .AddScoped<FeslClientHandler>()
            .AddScoped<TheaterHandler>();

        services
            .AddHostedService<DnsHostedService>()
            .AddHostedService<PlasmaHostedService>();

        services.AddLogging(log =>
        {
            log.ClearProviders();
            log.AddSimpleConsole(x => {
                x.IncludeScopes = true;
                x.SingleLine = true;
                x.TimestampFormat = "[HH:mm:ss::fff] ";
                x.ColorBehavior = LoggerColorBehavior.Enabled;
            });

            services.Configure<LoggerFilterOptions>(x => x.AddFilter(nameof(Microsoft), LogLevel.Warning));

            var debugSettings = config.GetSection(nameof(DebugSettings)).Get<DebugSettings>()!;
            if (debugSettings.EnableFileLogging)
            {
                services.Configure<LoggerFilterOptions>(x =>
                {
                    x.AddFilter("Arcadia.Handlers.*", LogLevel.Trace);
                    x.AddFilter("Arcadia.EA.EAConnection", LogLevel.Trace);
                });

                var startTs = DateTime.Now.Ticks;
                log.AddFile($"logs/arcadia.{startTs}.log", append: true);
            }
        });
    })
    .Build();

await host.RunAsync();