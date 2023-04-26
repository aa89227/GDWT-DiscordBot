using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;

public class InteractionHandler
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _commands;
    private readonly IServiceProvider _services;
    private readonly ILogger<InteractionHandler> _logger;
    private readonly AppSettings _settings;

    public InteractionHandler(DiscordSocketClient client, InteractionService commands, IServiceProvider services, ILogger<InteractionHandler> logger, IOptions<AppSettings> settings)
    {
        _client = client;
        _commands = commands;
        _services = services;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task InitializeAsync()
    {
        await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

        _client.InteractionCreated += HandleInteractionAsync;
    }

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(_client, interaction);

            // 限制在某個頻道
            if (interaction.ChannelId != _settings.KogCommandChannelId)
            {
                var channel = context.Guild!.GetTextChannel(_settings.KogCommandChannelId);
                await interaction.RespondAsync($"請在{channel.Mention}使用此指令", ephemeral: true); // mention channel
                return;
            }
            var result = await _commands.ExecuteCommandAsync(context, _services);
            if (!result.IsSuccess)
            {
                _logger.LogError(result.ErrorReason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while executing command");

            if (interaction.Type == InteractionType.ApplicationCommand)
                await interaction.GetOriginalResponseAsync().ContinueWith(async (msg) => await msg.Result.DeleteAsync());
        }
    }
}