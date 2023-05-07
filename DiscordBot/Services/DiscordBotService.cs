using Discord.Interactions;
using Discord.WebSocket;
using Discord;
using DiscordBot.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

internal class DiscordBotService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly AppSettings _appSettings;
    private readonly InteractionService _interactionService;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly InteractionHandler _interactionHandler;

    public DiscordBotService(DiscordSocketClient client,
                             IOptions<AppSettings> settings,
                             InteractionService interactionService,
                             ILogger<DiscordBotService> logger,
                             InteractionHandler interactionHandler)
    {
        _client = client;
        _appSettings = settings.Value;
        _interactionService = interactionService;
        _logger = logger;
        _interactionHandler = interactionHandler;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _interactionHandler.InitializeAsync();

        _client.Log += LogAsync;
        _interactionService.Log += LogAsync;
        _client.Ready += async () =>
        {
            try
            {
#if DEBUG
                await _interactionService.RegisterCommandsToGuildAsync(1030466547325607936);
#else
            await _interactionService.RegisterCommandsGloballyAsync();
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError("{Error Message}", ex.Message);
            }
        };
        await _client.LoginAsync(TokenType.Bot, _appSettings.BotToken);
        await _client.StartAsync();
        _logger.LogInformation("Discord 機器人啟動");
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    private Task LogAsync(LogMessage message)
    {
        if (message.Severity == LogSeverity.Error)
            _logger.LogError("[General/{MessageSeverity}] {Message}", message.Severity, message);
        else if (message.Severity == LogSeverity.Warning)
            _logger.LogWarning("[General/{MessageSeverity}] {Message}", message.Severity, message);
        else
            _logger.LogInformation("[General/{MessageSeverity}] {Message}", message.Severity, message);

        return Task.CompletedTask;
    }
}
