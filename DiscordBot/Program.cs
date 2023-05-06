using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot;
using DiscordBot.Repositories;
using DiscordBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        services.AddSingleton<MongoKogRepository>();
        services.AddLogging();
        services.AddHostedService<ScheduledService>();
    })
    .Build();

await RunAsync(host);

async Task RunAsync(IHost host)
{
    using IServiceScope scope = host.Services.CreateScope();
    IServiceProvider services = scope.ServiceProvider;
    AppSettings settings = services.GetRequiredService<IOptions<AppSettings>>().Value;

    var client = services.GetRequiredService<DiscordSocketClient>();
    var slashCommands = services.GetRequiredService<InteractionService>();
    await services.GetRequiredService<InteractionHandler>().InitializeAsync();

    var logger = services.GetRequiredService<ILogger<Program>>();
    Task LogAsync(LogMessage message)
    {
        if (message.Severity == LogSeverity.Error)
            logger.LogError("[General/{MessageSeverity}] {Message}", message.Severity, message);
        else if (message.Severity == LogSeverity.Warning)
            logger.LogWarning("[General/{MessageSeverity}] {Message}", message.Severity, message);
        else
            logger.LogInformation("[General/{MessageSeverity}] {Message}", message.Severity, message);

        return Task.CompletedTask;
    }

    client.Log += LogAsync;
    slashCommands.Log += LogAsync;
    client.Ready += async () =>
    {
        try
        {
#if DEBUG
            await slashCommands.RegisterCommandsToGuildAsync(1030466547325607936);
#else
            await slashCommands.RegisterCommandsGloballyAsync();
#endif
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
        }
    };
    await client.LoginAsync(TokenType.Bot, settings.BotToken);
    await client.StartAsync();
    host.Run();
}