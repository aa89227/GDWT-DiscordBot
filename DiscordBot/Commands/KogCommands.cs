using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Repositories;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace DiscordBot.Commands;

[Group("kog", "King of Gores 相關的指令")]
public class KogCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly MongoKogRepository _repository;
    private readonly AppSettings _settings;

    public KogCommands(MongoKogRepository repository, IOptions<AppSettings> settings)
    {
        _repository = repository;
        _settings = settings.Value;
    }
    #region 更新資料
    [RequireOwner]
    [SlashCommand("update_all_user_data", "(Owner Only) 更新所有成員資料")]
    public async Task UpdateAllUserData()
    {
        await DeferAsync();
        await _repository.UpdateAllUserData();
        await ModifyOriginalResponseAsync(x => x.Content = "成員資料更新成功!");
    }

    [RequireOwner]
    [SlashCommand("update_map_data", "(Owner Only)更新地圖資料")]
    public async Task UpdateMapData()
    {
        await DeferAsync();
        await _repository.UpdateMapData();
        await ModifyOriginalResponseAsync(x => x.Content = "地圖資料更新成功!");
    }
    #endregion
    #region 註冊
    [SlashCommand("register", "註冊")]
    public async Task Register(string username_in_kog)
    {
        await DeferAsync(ephemeral:true);
        var originalResponse = await GetOriginalResponseAsync();
        var result = await _repository.RegisterUser(Context.User.Id, username_in_kog);
        if (!result.IsSuccess)
        {
            await ModifyOriginalResponseAsync(x => x.Content = $"無法註冊! {result.ErrorMessage}");
            return;
        }
        var logChannel = Context.Client.GetChannel(_settings.LogChannel) as ISocketMessageChannel;
        var kogUserData = await KogWebCrawler.GetUserDataAsync(username_in_kog);
        if (kogUserData is null)
        {
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

    private async Task HandleRegistrationFailure(string username_in_kog, MongoKogRepository.RegisterationResult result, ISocketMessageChannel? logChannel)
    {
        var deleteButton = new ButtonBuilder()
            .WithLabel("刪除註冊訊息")
            .WithCustomId($"kog-register-delete-{result.RegisterationId}")
            .WithStyle(ButtonStyle.Danger);
        var failComponent = new ComponentBuilder()
            .WithButton(deleteButton)
            .Build();
        var failEmbed = new EmbedBuilder()
            .WithTitle("錯誤的註冊")
            .WithDescription($"""
                                 RegistrationId：{result.RegisterationId}
                                 User：{Context.User.Mention}
                                 Name：{username_in_kog}
                                 """)
            .WithColor(Color.DarkRed)
            .Build();
        await logChannel!.SendMessageAsync(embed: failEmbed, components: failComponent);
    }

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
    #endregion
    #region 註冊處理
    [ComponentInteraction("kog-register-delete-*", true)]
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
    [ComponentInteraction("kog-register-*-*", true)]
    public async Task HandleRegistrationApproval(string approval, string registrationId)
    {
        bool isApproved = approval == "approve";
        await DeferAsync();
        var originEmbed = (await GetOriginalResponseAsync()).Embeds.First();
        var embedBuilder = new EmbedBuilder()
            .WithTitle("處理中")
            .WithAuthor(Context.User)
            .WithDescription(originEmbed.Description);
        // 處理中
        await ModifyOriginalResponseAsync(x =>
        {
            x.Embed = embedBuilder.Build();
            x.Components = null;
        });
        var result = isApproved
            ? await _repository.ApproveRegistration(Context.User.Id, registrationId)
            : await _repository.RejectRegistration(Context.User.Id, registrationId);
        var status = isApproved ? "已通過" : "已拒絕";
        var color = isApproved ? Color.Green : Color.Red;
        
        if (!result.IsSuccess)
        {
            color = Color.Orange;
            embedBuilder
                .WithTitle("審核失敗")
                .WithFooter($"失敗訊息：{result.ErrorMessage}");
        }
        else
        {
            embedBuilder
                .WithTitle(status);
            if (isApproved)
            {
                var role = Context.Guild!.Roles.FirstOrDefault(x => x.Name == "KoG");
                if (role != null)
                {
                    await Context.Guild.GetUser(Context.User.Id).AddRoleAsync(role);
                    // 在歡迎頻道 歡迎成員
                    var welcomeChannel = Context.Guild.GetTextChannel(_settings.KogCommandChannel);
                    if (welcomeChannel != null)
                    {
                        await welcomeChannel.SendMessageAsync($"{Context.User.Mention}, Welcome to KoG in 𝔾ძωт!");
                    }
                }
            }
        }
        embedBuilder.WithColor(color);
        await ModifyOriginalResponseAsync(x =>
        {
            x.Embed = embedBuilder.Build();
            x.Components = null;
        });
    }
    #endregion
    #region 搜尋多個玩家之間未完成的地圖
    [RequireRole("KoG")]
    [SlashCommand("unfinished_map_between_players", "(KoG Only)搜尋多個玩家之間未完成的地圖，至多可以支援25人")]
    public async Task SearchUnfinishedMapBetweenPlayer(
        [Summary(description: "難度")] Difficulty difficulty,
        [Summary(description: "星級")] Star star)
    {
        await DeferAsync(ephemeral: true);

        // 限制在某個頻道
        if (Context.Channel.Id != _settings.KogCommandChannel)
        {
            var channel = Context.Guild!.GetTextChannel(_settings.KogCommandChannel);
            await ModifyOriginalResponseAsync(x => x.Content = $"請在{channel.Mention}使用此指令"); // mention channel
            return;
        }

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
    private static string ConvertToStarRating(int num)
    {
        string stars = string.Join("", Enumerable.Repeat("★", num));
        string emptyStars = string.Join("", Enumerable.Repeat("☆", 5 - num));
        return stars + emptyStars;
    }

    private ComponentBuilder BuildSelectionMenu(string[]? players = null)
    {
        var componentBuilder = new ComponentBuilder();
        var datas = _repository.GetRegisteredPlayers()
            .Where(p => players is null || !players.Contains(p)).ToList();
        if (datas.Any() && (players?.Length ?? 0) <= 25)
        {
            datas.Select((x, i) => new { Item = x, Index = i })
                .GroupBy(x => x.Index / 25)
                .Select(g =>
                {
                    SelectMenuBuilder menu = new SelectMenuBuilder()
                                            .WithCustomId($"player_selection_{g.First().Index / 25}")
                                            .WithMinValues(1)
                                            .WithMaxValues(1)
                                            .WithPlaceholder("選擇玩家");
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

        if (players is null)
        {
            sendWithPublic.WithDisabled(true);
            sendWithPrivate.WithDisabled(true);
        }

        componentBuilder.AddRow(new ActionRowBuilder().WithButton(sendWithPublic).WithButton(sendWithPrivate));
        return componentBuilder;
    }

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
    #endregion
}