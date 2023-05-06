using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace DiscordBot.Commands;

[Group("kog", "King of Gores 相關的指令")]
public class KogCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly MongoKogRepository _repository;
    private readonly AppSettings _settings;
    private readonly ILogger<KogCommands> _logger;

    public KogCommands(MongoKogRepository repository, IOptions<AppSettings> settings, ILogger<KogCommands> logger)
    {
        _repository = repository;
        _settings = settings.Value;
        _logger = logger;
    }

    #region 更新資料

    /// <summary>
    /// 更新所有成員資料，此指令只限擁有者使用
    /// </summary>
    [RequireOwner]
    [SlashCommand("update_all_user_data", "(Owner Only) 更新所有成員資料")]
    public async Task UpdateAllUserData()
    {
        await DeferAsync();
        await _repository.UpdateAllUserData();
        await ModifyOriginalResponseAsync(x => x.Content = "成員資料更新成功!");
    }

    /// <summary>
    /// 更新地圖資料，此指令只限擁有者使用
    /// </summary>
    [RequireOwner]
    [SlashCommand("update_map_data", "(Owner Only)更新地圖資料")]
    public async Task UpdateMapData()
    {
        await DeferAsync();
        await _repository.UpdateMapData();
        await ModifyOriginalResponseAsync(x => x.Content = "地圖資料更新成功!");
    }

    #endregion 更新資料

    #region 註冊

    /// <summary>
    /// 註冊指令，用於註冊使用者
    /// </summary>
    /// <param name="username_in_kog">註冊者在遊戲中的名稱</param>
    [SlashCommand("register", "註冊")]
    public async Task Register(string username_in_kog)
    {
        await DeferAsync(ephemeral: true);
        var originalResponse = await GetOriginalResponseAsync();
        var result = await _repository.RegisterUser(Context.User.Id, username_in_kog);
        if (!result.IsSuccess)
        {
            await ModifyOriginalResponseAsync(x => x.Content = $"無法註冊! {result.ErrorMessage}");
            return;
        }
        var logChannel = Context.Client.GetChannel(_settings.LogChannelId) as ISocketMessageChannel;
        KogWebCrawler.KogUserData? kogUserData;
        try
        {
            kogUserData = await KogWebCrawler.GetUserDataAsync(username_in_kog);
        }
        catch (Exception ex) { 
            _logger.LogError(ex, "無法取得 KOG 使用者資訊");
            await ModifyOriginalResponseAsync(x => x.Content = $"註冊失敗! 請確認您的名稱是否正確，並等待管理員處理註冊資料");
            await HandleRegistrationFailure(username_in_kog, result, logChannel);
            return;
        }
        await ModifyOriginalResponseAsync(x =>
        {
            x.Content = $"註冊成功! 請等候管理員通過。\n註冊名稱: {username_in_kog}";
        });
        await HandleRegistrationSuccess(username_in_kog, result, logChannel, kogUserData);
    }

    /// <summary>
    /// 處理註冊成功後的相關操作，包括發送訊息到 <paramref name="logChannel"/> 和建立待審核資料。
    /// </summary>
    /// <param name="username_in_kog">KOG 名稱</param>
    /// <param name="result">註冊結果</param>
    /// <param name="logChannel">註冊日誌頻道</param>
    /// <param name="kogUserData">KOG 使用者資訊</param>
    private async Task HandleRegistrationSuccess(string username_in_kog, MongoKogRepository.RegisterationResult result, ISocketMessageChannel? logChannel, KogWebCrawler.KogUserData? kogUserData)
    {
        // 發送訊息到 LogChannel
        var approveButton = new ButtonBuilder()
            .WithLabel("通過")
            .WithCustomId($"kog-register-approve-{result.RegisterationId}")
            .WithStyle(ButtonStyle.Success);
        var rejectButton = new ButtonBuilder()
            .WithLabel("拒絕")
            .WithCustomId($"kog-register-reject-{result.RegisterationId}")
            .WithStyle(ButtonStyle.Danger);
        var checkButton = new ButtonBuilder()
            .WithUrl($"https://kog.tw/#p=players&player={username_in_kog}")
            .WithLabel("查看玩家資訊")
            .WithStyle(ButtonStyle.Link);
        var components = new ComponentBuilder()
            .WithButton(approveButton)
            .WithButton(rejectButton)
            .WithButton(checkButton)
            .Build();
        var embed = new EmbedBuilder()
            .WithTitle("未審核")
            .WithDescription($"""
                            RegistrationId：{result.RegisterationId}
                            User：{Context.User.Mention}
                            Name：{username_in_kog}
                            Rank：{kogUserData!.points.Rank}
                            Points：{kogUserData.points.TPoints}（{kogUserData.points.Points} + {kogUserData.points.Seasonpoints}）
                            """)
            .WithColor(Color.Blue)
            .Build();

        await logChannel!.SendMessageAsync(embed: embed, components: components);
    }

    /// <summary>
    /// 處理註冊失敗的事件，為了防止使用者頻繁的進行失敗的註冊，必須等管理員刪除註冊資料才可以重新進行註冊
    /// </summary>
    /// <param name="username_in_kog">在 KOG 上的使用者名稱</param>
    /// <param name="result">註冊結果</param>
    /// <param name="logChannel">日誌頻道</param>
    private async Task HandleRegistrationFailure(string username_in_kog, MongoKogRepository.RegisterationResult result, ISocketMessageChannel? logChannel)
    {
        // 建立刪除註冊訊息的按鈕
        var deleteButton = new ButtonBuilder()
            .WithLabel("刪除註冊訊息")
            .WithCustomId($"kog-delete-registeration-{result.RegisterationId}")
            .WithStyle(ButtonStyle.Danger);
        // 建立失敗時的組件
        var failComponent = new ComponentBuilder()
            .WithButton(deleteButton)
            .Build();
        // 建立失敗時的嵌入式訊息
        var failEmbed = new EmbedBuilder()
            .WithTitle("錯誤的註冊")
            .WithDescription($"""
                                 RegistrationId：{result.RegisterationId}
                                 User：{Context.User.Mention}
                                 Name：{username_in_kog}
                                 """)
            .WithColor(Color.DarkRed)
            .Build();
        // 發送失敗時的訊息到日誌頻道
        await logChannel!.SendMessageAsync(embed: failEmbed, components: failComponent);
    }

    /// <summary>
    /// 取消註冊。使用者必須擁有 "KoG" 角色才能執行此指令。
    /// </summary>
    [RequireRole("KoG")]
    [SlashCommand("unregister", "取消註冊")]
    public async Task Unregister()
    {
        await DeferAsync(ephemeral: true);
        var result = await _repository.UnregisterUser(Context.User.Id);
        if (!result.IsSuccess)
        {
            await ModifyOriginalResponseAsync(x => x.Content = $"無法取消註冊! {result.ErrorMessage}");
            return;
        }
        // remove role，
        var guild = Context.Guild;
        var role = Context.Guild!.Roles.FirstOrDefault(x => x.Name == "KoG");
        var user = guild.GetUser(Context.User.Id);
        await user!.RemoveRoleAsync(role);
        await ModifyOriginalResponseAsync(x => x.Content = $"取消註冊成功!");
    }

    #endregion 註冊

    #region 註冊處理

    /// <summary>
    /// 刪除註冊訊息 ComponentInteraction。當使用者按下特定自訂 ID 的按鈕時觸發。
    /// </summary>
    /// <param name="registrationId">註冊編號</param>
    [ComponentInteraction("kog-delete-registeration-*", true)]
    public async Task DeleteRegisteration(string registrationId)
    {
        await DeferAsync();
        var result = await _repository.DeleteRegistration(Context.User.Id, registrationId);
        var originEmbed = (await GetOriginalResponseAsync()).Embeds.First();
        var embedBuilder = new EmbedBuilder()
            .WithAuthor(Context.User)
            .WithDescription(originEmbed.Description);
        if (!result.IsSuccess)
        {
            embedBuilder
                .WithTitle("刪除失敗")
                .WithFooter($"失敗訊息：{result.ErrorMessage}");
        }
        else
        {
            embedBuilder
                .WithTitle("已刪除")
                .WithColor(Color.DarkRed);
        }
        await ModifyOriginalResponseAsync(x =>
        {
            x.Embeds = new[] { embedBuilder.Build() };
            x.Components = null;
        });
    }

    /// <summary>
    /// 處理註冊審核的方法。
    /// </summary>
    /// <param name="approval">核准或拒絕的指示字串，值為 "approve" 或 "reject"。</param>
    /// <param name="registrationId">註冊編號。</param>
    [ComponentInteraction("kog-register-*-*", true)]
    public async Task HandleRegistrationApproval(string approval, string registrationId)
    {
        bool isApproved = approval == "approve";
        await DeferAsync();
        // 取得原始回應中的嵌入物件
        var originEmbed = (await GetOriginalResponseAsync()).Embeds.First();

        // 建立新的嵌入物件
        var embedBuilder = new EmbedBuilder()
            .WithTitle("處理中")
            .WithAuthor(Context.User)
            .WithDescription(originEmbed.Description);

        // 修改原始回應，顯示「處理中」的狀態
        await ModifyOriginalResponseAsync(x =>
        {
            x.Embed = embedBuilder.Build();
            x.Components = null;
        });

        // 根據是否核准，呼叫不同的方法進行註冊審核
        var result = isApproved
            ? await _repository.ApproveRegistration(Context.User.Id, registrationId)
            : await _repository.RejectRegistration(Context.User.Id, registrationId);

        // 根據結果決定回應的訊息內容與顏色
        var status = isApproved ? "已通過" : "已拒絕";
        var color = isApproved ? Color.Green : Color.Red;

        // 如果審核失敗，顯示失敗訊息
        if (!result.IsSuccess)
        {
            color = Color.Orange;
            embedBuilder
                .WithTitle("審核失敗")
                .WithFooter($"失敗訊息：{result.ErrorMessage}");
        }
        else
        {
            // 如果審核成功，顯示「已通過」的訊息
            embedBuilder.WithTitle(status);
            if (isApproved)
            {
                // 給註冊者加上 "KoG" 的身分組
                var role = Context.Guild!.Roles.FirstOrDefault(x => x.Name == "KoG");
                if (role != null)
                {
                    var user = Context.Guild.GetUser(_repository.GetDiscordUserIdByRegistrationId(registrationId));
                    await user.AddRoleAsync(role);

                    // 在歡迎頻道 歡迎成員
                    var welcomeChannel = Context.Guild.GetTextChannel(_settings.KogCommandChannelId);
                    if (welcomeChannel != null)
                    {
                        await welcomeChannel.SendMessageAsync($"{user.Mention}, Welcome to KoG in 𝔾ძωт!");
                    }
                }
            }
        }

        // 設定嵌入物件的顏色，並修改原始回應
        embedBuilder.WithColor(color);
        await ModifyOriginalResponseAsync(x =>
        {
            x.Embed = embedBuilder.Build();
            x.Components = null;
        });
    }

    #endregion 註冊處理

    #region 搜尋玩家資料

    [RequireRole("KoG")]
    [SlashCommand("player-info", "搜尋玩家資料")]
    public async Task SearchPlayerInfo(string username_in_kog)
    {
        var playerInfo = await _repository.GetPlayerInfoByUserNameInKog(username_in_kog);
        if (playerInfo == null)
        {
            await RespondAsync("找不到玩家資料，有可能是該玩家未註冊 𝔾ძωт KoG", ephemeral: true);
            return;
        }
        var embed = new EmbedBuilder()
                .WithTitle(username_in_kog)
                .WithDescription($"""
                                    Rank：{playerInfo.Rank}
                                    Points：{playerInfo.Points + playerInfo.SeasonPoints}（{playerInfo.Points} + {playerInfo.SeasonPoints}）
                                    """
                )
                .WithColor(Color.Purple)
                .Build();
        await RespondAsync(embed: embed);
    }

    #endregion 搜尋玩家資料

    #region 搜尋多個玩家之間未完成的地圖

    /// <summary>
    /// 搜尋多個玩家之間未完成的地圖，至多可以支援25人。
    /// 需要具備 "KoG" 角色。
    /// </summary>
    /// <param name="difficulty">難度</param>
    /// <param name="star">星級</param>
    [RequireRole("KoG")]
    [SlashCommand("unfinished_map_between_players", "搜尋多個玩家之間未完成的地圖，至多可以支援25人")]
    public async Task SearchUnfinishedMapBetweenPlayer(
        [Summary(description: "難度")] Difficulty difficulty,
        [Summary(description: "星級")] Star star)
    {
        await DeferAsync(ephemeral: true);

        await ModifyOriginalResponseAsync(x =>
        {
            x.Content = $"""
                        目前選擇的難度：{difficulty}
                        目前選擇的星數：{(star == 0 ? "not limited" : ConvertToStarRating((int)star))}
                        目前選擇的玩家：
                        """;
            x.Components = BuildSelectionMenu().Build();
        });
    }

    public enum Difficulty
    {
        Easy,
        Main,
        Hard,
        Insane,
        Extreme,
        Mod
    }

    public enum Star
    {
        [ChoiceDisplay("不指定")] None = 0,
        [ChoiceDisplay("1")] One = 1,
        [ChoiceDisplay("2")] Two = 2,
        [ChoiceDisplay("3")] Three = 3,
        [ChoiceDisplay("4")] Four = 4,
        [ChoiceDisplay("5")] Five = 5
    }

    /// <summary>
    /// 處理當使用者新增玩家時的回應
    /// </summary>
    /// <param name="id">該 Component Interaction 的自訂 ID</param>
    /// <param name="players">新增的玩家名稱</param>
    [ComponentInteraction("player_selection_*", true)]
    public async Task AddPlayer(string id, string[] players)
    {
        await DeferAsync();
        IUserMessage message = await GetOriginalResponseAsync();
        int lineCount = message.Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
        await ModifyOriginalResponseAsync(x =>
        {
            var (difficulty, star, playerlist) = ParseMessage(message.Content);

            x.Content = $"""
                        {message.Content}
                        {lineCount - 2}. {players[0]}
                        """;

            x.Components = BuildSelectionMenu(playerlist.Concat(new[] { players[0] }).ToArray()).Build();
        });
    }

    ///<summary>
    /// 執行查詢未完成地圖的動作，並依照指定條件篩選地圖資訊，顯示於訊息中。
    ///</summary>
    ///<param name="id">代表此次查詢是否為私人，若為 "private" 則只有查詢的使用者本人可以看到訊息，否則會顯示於公共頻道中。</param>
    [ComponentInteraction("search_unfinished_map:*", true)]
    public async Task SendResult(string id)
    {
        await DeferAsync();

        // 顯示已選擇的難度與玩家
        IUserMessage message = await GetOriginalResponseAsync();
        await ModifyOriginalResponseAsync(x =>
        {
            x.Content = $"""
                        【查詢完成】
                        """;
            x.Components = null;
        });
        bool ephemeral = id == "private";
        var (difficulty, star, players) = ParseMessage(message.Content);

        var m = await FollowupAsync($"""
                        {Context.User.Mention} 查詢玩家之間未完成的地圖
                        ```
                        選擇的難度：{difficulty}
                        選擇的星數：{(star == 0 ? "not limited" : ConvertToStarRating((int)star))}
                        選擇的玩家：
                        {string.Join("\n", players.Select((x, i) => $"{i + 1}. {x}"))}
                        ```
                        """, ephemeral: ephemeral);
        var unfinishedMaps = _repository.GetUnfinishedMapsBetweenPlayers(players, difficulty);

        // 依照星數排序後，30筆顯示一個訊息
        var unfinishedMapsGroup = star == 0 ? unfinishedMaps.OrderBy(x => x.Star) : unfinishedMaps.Where(x => x.Star == star);

        // 每 30 筆顯示一個訊息
        var unfinishedMapsGroupBy30 = unfinishedMapsGroup.Select((x, i) => new { Item = x, Index = i })
            .GroupBy(x => x.Index / 30)
            .Select(g => g.Select(x => x.Item).ToList())
            .ToList();
        foreach (var group in unfinishedMapsGroupBy30)
        {
            var mapinfo = string.Join('\n', group.Select(x => $"{ConvertToStarRating(x.Star)}({x.Points}) {x.MapName}"));
            if (ephemeral)
            {
                await FollowupAsync(mapinfo, ephemeral: ephemeral);
            }
            else
            {
                await m.ReplyAsync(mapinfo);
            }
        }
    }

    /// <summary>
    /// 將數字轉換為星號評分，評分總共5顆星，用實心星號表示評分數量，用空心星號表示未評分數量。
    /// </summary>
    /// <param name="num">評分數量，範圍從0到5。</param>
    /// <returns>轉換後的星號評分字串。</returns>
    private static string ConvertToStarRating(int num)
    {
        string stars = string.Join("", Enumerable.Repeat("★", num));
        string emptyStars = string.Join("", Enumerable.Repeat("☆", 5 - num));
        return stars + emptyStars;
    }

    /// <summary>
    /// 建立玩家選擇菜單
    /// </summary>
    /// <param name="players">已選擇的玩家列表</param>
    /// <returns>ComponentBuilder</returns>
    private ComponentBuilder BuildSelectionMenu(string[]? players = null)
    {
        var componentBuilder = new ComponentBuilder();

        // 取得所有已註冊的玩家
        var datas = _repository.GetRegisteredPlayers()
            .Where(p => players is null || !players.Contains(p)).ToList();

        // 判斷是否有可選擇的玩家且已選玩家數小於等於25
        if (datas.Any() && (players?.Length ?? 0) <= 25)
        {
            // 每25個玩家分成一個群組，建立選單
            datas.Select((x, i) => new { Item = x, Index = i })
                .GroupBy(x => x.Index / 25)
                .Select(g =>
                {
                    SelectMenuBuilder menu = new SelectMenuBuilder()
                                            .WithCustomId($"player_selection_{g.First().Index / 25}")
                                            .WithMinValues(1)
                                            .WithMaxValues(1)
                                            .WithPlaceholder("選擇玩家");

                    // 將玩家名稱加入選項
                    g.ToList().ForEach(x => menu.AddOption(x.Item, x.Item));

                    return menu;
                })
                .ToList()
                .ForEach(menuBuilder => componentBuilder.WithSelectMenu(menuBuilder));
        }
        // 兩種送出按鈕，一種會全公開，另一種不會
        var sendWithPublic = new ButtonBuilder()
            .WithCustomId("search_unfinished_map:public")
            .WithLabel("送出並公開")
            .WithStyle(ButtonStyle.Success);

        var sendWithPrivate = new ButtonBuilder()
            .WithCustomId("search_unfinished_map:private")
            .WithLabel("送出但不公開")
            .WithStyle(ButtonStyle.Success);

        // 若已選擇的玩家列表為空，則禁用送出按鈕
        if (players is null)
        {
            sendWithPublic.WithDisabled(true);
            sendWithPrivate.WithDisabled(true);
        }

        // 將送出按鈕加入行中
        componentBuilder.AddRow(new ActionRowBuilder().WithButton(sendWithPublic).WithButton(sendWithPrivate));
        return componentBuilder;
    }

    /// <summary>
    /// 解析收到的訊息字串，取出難度、星數、玩家清單
    /// </summary>
    /// <param name="message">訊息字串</param>
    /// <returns>包含難度、星數、玩家清單的元組</returns>
    private static (string, int, string[]) ParseMessage(string message)
    {
        string[] contents = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var difficulty = contents[0].Replace("目前選擇的難度：", "");
        // 找到字串中的 ★，取出星數
        int star = contents[1].Where(c => c == '★').Count();

        // 第三行之後的都是player，每一行都是 X. player，要把.後面的切出來
        var players = contents
            .Skip(3)
            .Select(x => x[(x.IndexOf(".") + 2)..])
            .ToArray();

        return (difficulty, star, players);
    }

    #endregion 搜尋多個玩家之間未完成的地圖
}