namespace DiscordBot.Repositories;

public class PointType
{
    public int Rank { get; set; }
    public string Name { get; set; } = default!;
    public int TPoints { get; set; }
    public float PvPpoints { get; set; }
    public int Points { get; set; }
    public int Seasonpoints { get; set; }
    public int RewardIndex { get; set; }
    public string Powers { get; set; } = default!;
}

public class FinishedMap
{
    public string Map { get; set; } = default!;
    public double Time { get; set; }
    public string Timestamp { get; set; } = default!;
}

public class KogMap
{
    public string MapName { get; set; } = default!;
    public string Difficulty { get; set; } = default!;
    public int Star { get; set; }
    public int Points { get; set; }
    public string Author { get; set; } = default!;
    public string ReleasedTime { get; set; } = default!;
}

public record KogPlayerInfo(string Name, int Rank, int Points, int SeasonPoints);