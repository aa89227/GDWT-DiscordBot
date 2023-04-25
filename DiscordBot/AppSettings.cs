namespace DiscordBot;

public class AppSettings
{
    public string BotToken { get; set; } = default!;
    public string MongoDBURL { get; set; } = default!;
    public ulong LogChannel { get; set; }
    public ulong KogCommandChannel { get; set; }

}
