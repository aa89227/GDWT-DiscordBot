using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace DiscordBot.Repositories;

public class MongoKogRepository
{
    private readonly AppSettings _settings;
    private readonly ILogger<MongoKogRepository> _logger;
    private readonly IMongoDatabase _database;

    private IMongoCollection<KogPlayer> KogPlayers => GetCollection<KogPlayer>("KogPlayers");
    private IMongoCollection<KogMap> KogMaps => GetCollection<KogMap>("KogMaps");
    private IMongoCollection<KogPlayerRegisteration> KogPlayerRegistrations => GetCollection<KogPlayerRegisteration>("KogPlayerRegisterations");

    public MongoKogRepository(IOptions<AppSettings> settings, ILogger<MongoKogRepository> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        try
        {
            var client = new MongoClient(_settings.MongoDBURL);
            _database = client.GetDatabase("KingOfGores");
            _logger.LogInformation("Successfully connected to mongo database KingOfGores");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to mongo database");
            throw;
        }
    }

    public IMongoCollection<T> GetCollection<T>(string name)
    {
        // Access a collection
        return _database.GetCollection<T>(name);
    }

    #region 註冊

    public async Task<RegisterationResult> RegisterUser(ulong discordUserId, string userNameInKog)
    {
        // 檢查是否已經註冊過，找 KogPlayers 及 KogPlayerRegisterations 裡面有沒有這個 DiscordUserId
        var player = KogPlayers.Find(x => x.DiscordUserId == discordUserId).FirstOrDefault();
        if (player is not null)
        {
            return new RegisterationResult("你已經註冊過了，若想重新註冊，請聯繫管理員");
        }
        var registration = KogPlayerRegistrations.Find(x => x.DiscordUserId == discordUserId).FirstOrDefault();
        if (registration is not null && registration.Registeration is null)
        {
            return new RegisterationResult("你的註冊正在審核中，請等待管理員處理");
        }
        // 檢查是否已經有人使用這個 Kog 名稱
        player = KogPlayers.Find(x => x.UserNameInKog == userNameInKog).FirstOrDefault();
        if (player is not null)
        {
            return new RegisterationResult($"名稱【{userNameInKog}】已經被使用了");
        }
        var doc = new KogPlayerRegisteration()
        {
            DiscordUserId = discordUserId,
            UserNameInKog = userNameInKog,
        };
        await KogPlayerRegistrations.InsertOneAsync(doc);
        return new()
        {
            RegisterationId = doc.Id.ToString()
        };
    }

    public async Task<RegisterationResult> UnregisterUser(ulong id)
    {
        var result = await KogPlayers.DeleteOneAsync(x => x.DiscordUserId == id);
        if (result.DeletedCount == 0)
        {
            return new RegisterationResult("你沒有註冊過");
        }
        _logger.LogInformation($"User {id} unregistered");
        return new();
    }

    public record RegisterationResult(string? ErrorMessage = null)
    {
        public bool IsSuccess => ErrorMessage is null;
        public string? RegisterationId = null;
    }

    #endregion 註冊

    #region 審核

    /// <summary>
    /// 把 <paramref name="id"/> 的註冊申請通過，並把玩家加入 KogPlayers 資料庫
    /// </summary>
    /// <remarks>
    /// 1. 找到 <paramref name="id"/> 的註冊申請
    /// 2. 檢查是否已經有人使用這個 Kog 名稱
    /// 3. 檢查此名稱有沒有分數
    /// 4. 更改註冊申請，將 <see cref="KogPlayerRegisteration.Approver"/> 設為 <paramref name="approver"/>，並將 <see cref="KogPlayerRegisteration.IsApproved"/> 設為 true
    /// 5. 把玩家加入 KogPlayers 資料庫
    /// </remarks>
    /// <param name="approver"></param>
    /// <param name="id"></param>
    /// <returns></returns>
    public async Task<ReviewResult> ApproveRegistration(ulong approver, string id)
    {
        // 找到 <paramref name="id"/> 的註冊申請
        var registration = KogPlayerRegistrations.Find(x => x.Id == new ObjectId(id)).FirstOrDefault();
        if (registration is null)
        {
            return new("找不到此註冊申請");
        }
        if (registration.Registeration is not null)
        {
            return new("此註冊申請已經處理過了");
        }
        // 檢查是否已經有人使用這個 Kog 名稱
        var player = KogPlayers.Find(x => x.UserNameInKog == registration.UserNameInKog).FirstOrDefault();
        if (player is not null)
        {
            return new($"名稱【{registration.UserNameInKog}】已經被使用了");
        }
        // 檢查此名稱有沒有分數
        KogWebCrawler.KogUserData? playerData;
        try
        {
            playerData = await KogWebCrawler.GetUserDataAsync(registration.UserNameInKog);
        }
        catch
        {
            return new($"名稱【{registration.UserNameInKog}】無法被註冊");
        }
        // 更改註冊申請，將 <see cref="KogPlayerRegisteration.Approver"/> 設為 <paramref name="approver"/>，並將 <see cref="KogPlayerRegisteration.IsApproved"/> 設為 true
        UpdateDefinition<KogPlayerRegisteration> updateDefinition = Builders<KogPlayerRegisteration>.Update
            .Set(x => x.Registeration, new RegisterationObject()
            {
                IsApproved = true,
                Approver = approver,
            });
        await KogPlayerRegistrations.UpdateOneAsync(x => x.Id == new ObjectId(id), updateDefinition);

        // 把玩家加入 KogPlayers 資料庫
        await KogPlayers.InsertOneAsync(new KogPlayer()
        {
            DiscordUserId = registration.DiscordUserId,
            UserNameInKog = registration.UserNameInKog,
            FinishedMaps = playerData!.finishedMaps.ToList(),
            Points = playerData!.points,
        });
        _logger.LogInformation($"User {registration.DiscordUserId} registered as {registration.UserNameInKog}");
        return new();
    }

    public async Task<ReviewResult> RejectRegistration(ulong rejecter, string registerationId)
    {
        // 找到 <paramref name="id"/> 的註冊申請
        var registration = KogPlayerRegistrations.Find(x => x.Id == new ObjectId(registerationId)).FirstOrDefault();
        if (registration is null)
        {
            return new("找不到此註冊申請");
        }
        if (registration.Registeration is not null)
        {
            return new("此註冊申請已經處理過了");
        }
        // 更改註冊申請，將 <see cref="KogPlayerRegisteration.Approver"/> 設為 <paramref name="approver"/>，並將 <see cref="KogPlayerRegisteration.IsApproved"/> 設為 true
        UpdateDefinition<KogPlayerRegisteration> updateDefinition = Builders<KogPlayerRegisteration>.Update
            .Set(x => x.Registeration, new RegisterationObject()
            {
                IsApproved = false,
                Rejecter = rejecter,
            });
        await KogPlayerRegistrations.UpdateOneAsync(x => x.Id == new ObjectId(registerationId), updateDefinition);
        _logger.LogInformation($"User {registration.DiscordUserId} registration rejected");
        return new();
    }

    public async Task<ReviewResult> DeleteRegistration(ulong id, string registrationId)
    {
        var result = await KogPlayerRegistrations.DeleteOneAsync(x => x.Id == new ObjectId(registrationId) && x.DiscordUserId == id);
        if (result.DeletedCount == 0)
        {
            return new ReviewResult("找不到此註冊申請");
        }
        _logger.LogInformation($"User {id} registration deleted");
        return new();
    }

    public record ReviewResult(string? ErrorMessage = null)
    {
        public bool IsSuccess => ErrorMessage is null;
    }

    #endregion 審核

    #region 查詢資料

    public List<string> GetRegisteredPlayers()
    {
        var players = KogPlayers.Find(FilterDefinition<KogPlayer>.Empty).ToList();
        _logger.LogInformation($"Get {players.Count} registered players");
        return players.Select(x => x.UserNameInKog).ToList();
    }

    /// <summary>
    /// 找到 <paramref name="players"/> 之間未完成的地圖，且 <paramref name="difficulty"/> 為指定難度
    /// </summary>
    /// <param name="players"></param>
    /// <param name="difficulty"></param>
    /// <returns></returns>
    public Repositories.KogMap[] GetUnfinishedMapsBetweenPlayers(string[] players, string difficulty)
    {
        var mapslist = players.Select(x => GetUnfinishedMapsByPlayer(x, difficulty)).ToList();
        // 找出所有玩家未完成的地圖
        var allUnfinishedMaps = mapslist.SelectMany(x => x).ToList();
        // 找出所有玩家未完成的地圖中，重複的地圖
        var repeatedMaps = allUnfinishedMaps.GroupBy(x => x.MapName)
                                            .Where(x => x.Count() == players.Length)
                                            .Select(x =>
                                            {
                                                var map = x.First();
                                                return new Repositories.KogMap()
                                                {
                                                    Author = map.Author,
                                                    MapName = map.MapName,
                                                    Difficulty = difficulty,
                                                    Star = map.Star,
                                                    Points = map.Points,
                                                    ReleasedTime = map.ReleasedTime,
                                                };
                                            })
                                            .ToArray();
        _logger.LogInformation($"Get {repeatedMaps.Length} repeated maps");
        return repeatedMaps;
    }

    private List<KogMap> GetUnfinishedMapsByPlayer(string playerName, string difficulty)
    {
        // 取得指定玩家的 KogPlayer 物件
        var player = GetKogPlayerByName(playerName);
        if (player is null)
        {
            return new List<KogMap>();
        }
        // 找出指定難度的指定玩家未完成的地圖
        var unfinishedMaps = KogMaps.Find(x => x.Difficulty == difficulty && !player.FinishedMaps.Any(m => m.Map == x.MapName))
                                    .ToList();
        return unfinishedMaps;
    }

    private KogPlayer GetKogPlayerByName(string playerName)
    {
        var playerFound = KogPlayers.Find(p => p.UserNameInKog == playerName).FirstOrDefault();
        if (playerFound is not null)
        {
            return playerFound;
        }
        throw new ArgumentException($"找不到名為 {playerName} 的玩家");
    }

    #endregion 查詢資料

    #region 更新資料

    public async Task UpdateAllUserData()
    {
        var players = GetRegisteredPlayers();

        var tasks = players.Select(async playerName =>
        {
            await UpdateUserDataByPlayerName(playerName);
        });
        await Task.WhenAll(tasks);
    }

    private async Task UpdateUserDataByPlayerName(string playerName)
    {
        var userData = await KogWebCrawler.GetUserDataAsync(playerName) ?? throw new ArgumentException($"無法拿到名稱【{playerName}】的玩家資料");
        var filter = Builders<KogPlayer>.Filter.Eq(p => p.UserNameInKog, playerName);
        var update = Builders<KogPlayer>.Update
            .Set(p => p.Points, userData.points)
            .Set(p => p.FinishedMaps, userData.finishedMaps.ToList());
        var result = await KogPlayers.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
        if (result != null)
        {
            _logger.LogInformation($"Player {playerName} data has been updated.");
        }
        else
        {
            _logger.LogWarning($"Player {playerName} not found in database.");
        }
    }

    public async Task UpdateMapData()
    {
        var newMaps = await KogWebCrawler.GetAllMapAsync();

        foreach (var map in newMaps)
        {
            var filter = Builders<KogMap>.Filter.Eq(m => m.MapName, map.MapName);
            var update = Builders<KogMap>.Update
                .Set(m => m.Difficulty, map.Difficulty)
                .Set(m => m.Star, map.Star)
                .Set(m => m.Points, map.Points)
                .Set(m => m.Author, map.Author)
                .Set(m => m.ReleasedTime, map.ReleasedTime);
            await KogMaps.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
            _logger.LogInformation($"Map {map.MapName} data has been updated.");
        }
    }

    #endregion 更新資料

    #region 資料庫物件

    private class KogPlayer
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public ulong DiscordUserId { get; set; }
        public string UserNameInKog { get; set; } = default!;
        public List<FinishedMap> FinishedMaps { get; set; } = default!;
        public PointType Points { get; set; } = default!;
    }

    private class KogMap
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public string MapName { get; set; } = default!;
        public string Difficulty { get; set; } = default!;
        public int Star { get; set; }
        public int Points { get; set; }
        public string Author { get; set; } = default!;
        public string ReleasedTime { get; set; } = default!;
    }

    private class KogPlayerRegisteration
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public ulong DiscordUserId { get; set; }
        public string UserNameInKog { get; set; } = default!;
        public RegisterationObject? Registeration { get; set; } = default!;
    }

    private class RegisterationObject
    {
        public bool IsApproved { get; set; }
        public ulong? Rejecter { get; set; }
        public ulong? Approver { get; set; }
    }

    #endregion 資料庫物件
}