using Arcadia;
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
using Discord.WebSocket;
using Arcadia.Handlers;

var host = Host.CreateDefaultBuilder()
    .ConfigureAppConfiguration((_, config) => config
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .AddJsonFile("dev.appsettings.json", optional: true, reloadOnChange: false)
    )
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        services
            .Configure<ArcadiaSettings>(config.GetSection(nameof(ArcadiaSettings)))
            .Configure<FileServerSettings>(config.GetSection(nameof(FileServerSettings)))
            .Configure<DiscordSettings>(config.GetSection(nameof(DiscordSettings)))
            .Configure<DebugSettings>(config.GetSection(nameof(DebugSettings)))
            .Configure<DnsSettings>(config.GetSection(nameof(DnsSettings)))
            .Configure<HostOptions>(x => x.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

        services
            .AddSingleton<ProtoSSL>()
            .AddSingleton<SharedCounters>()
            .AddSingleton<ConnectionManager>()
            .AddSingleton<StatsStorage>()
            .AddSingleton<DiscordSocketConfig>(x => new() { GatewayIntents = Discord.GatewayIntents.Guilds | Discord.GatewayIntents.GuildMessages })
            .AddSingleton<DiscordSocketClient>();

        services
            .AddScoped<Rc4TlsCrypto>()
            .AddScoped<IEAConnection, EAConnection>()
            .AddScoped<FeslHandler>()
            .AddScoped<TheaterHandler>()
            .AddScoped<MessengerHandler>();

        services
            .AddHostedService<PlasmaHostedService>()
            .AddHostedService<StaticFileHostedService>()
            .AddHostedService<DiscordHostedService>()
            .AddHostedService<DnsHostedService>();

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
                    x.AddFilter("Arcadia.EA.Handlers.*", LogLevel.Trace);
                    x.AddFilter("Arcadia.EA.EAConnection", LogLevel.Trace);
                    x.AddFilter("Arcadia.Hosting.StaticFileHostedService", LogLevel.Trace);
                });

                var startTs = DateTime.Now.Ticks;
                log.AddFile($"logs/arcadia.{startTs}.log", append: true);
            }
        });
    })
    .Build();

await host.RunAsync();