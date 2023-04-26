namespace DiscordBot;

public class AppSettings
{
    public string BotToken { get; set; } = default!;
    public string MongoDBURL { get; set; } = default!;
    public ulong LogChannelId { get; set; }
    public ulong KogCommandChannelId { get; set; }

}
