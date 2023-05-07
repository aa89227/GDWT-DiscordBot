using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot;
using DiscordBot.Common;
using DiscordBot.Repositories;
using DiscordBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var config = new ConfigurationBuilder()
#if DEBUG
            .AddUserSecrets<Program>()
#else
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
#endif
            .Build();

        services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
        {
            AlwaysDownloadUsers = true,
        }));
        services.Configure<AppSettings>(config);
        services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));
        services.AddSingleton<InteractionHandler>();
        services.AddSingleton<CommandService>();
        services.AddSingleton(x =>
        {
            var appSettings = x.GetRequiredService<IOptions<AppSettings>>().Value;
            var _logger = x.GetRequiredService<ILogger<Program>>();
            try
            {
                var client = new MongoClient(appSettings.MongoDBURL);
                _logger.LogInformation("嘗試連結到資料庫");
                client.StartSession();
                _logger.LogInformation("成功連結到資料庫");
                return client;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "無法連結到資料庫");
                throw;
            }
        });
        services.AddSingleton<MongoKogRepository>();
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.AddFile(o =>
            {
                var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
                if (!Directory.Exists(logsDirectory))
                {
                    Directory.CreateDirectory(logsDirectory);
                }
                o.RootPath = AppContext.BaseDirectory;
                o.BasePath = "logs";
                o.MaxFileSize = 10_000_000;
                o.FileAccessMode = Karambolo.Extensions.Logging.File.LogFileAccessMode.KeepOpenAndAutoFlush;
                o.Files = new[]
                {
                    new Karambolo.Extensions.Logging.File.LogFileOptions { Path = "<date:yyyy-MM-dd>.log",  }
                };
            });
        });
        services.AddHostedService<ScheduledService>();
        services.AddHostedService<DiscordBotService>();
    })
    .Build();

await host.RunAsync();
await Task.Delay(-1);
