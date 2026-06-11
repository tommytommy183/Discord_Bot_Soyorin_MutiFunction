using Discord;
using Discord.Audio;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using InstagramApiSharp.Classes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MusicBot2.Service;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using RiotSharp.Endpoints.StatusEndpoint;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

public class Program
{
    #region 變數
    private DiscordSocketClient? _client;
    private CommandService? _commands;
    private IAudioClient? _audioClient = null;
    private Queue<string> _songQueue = new Queue<string>();
    private bool _isPlaying = false;
    private String _NowPlayingSongUrl = "";
    private String _NowPlayingSongID = "";
    private String _NowPlayingSongName = "";
    private bool _isSkipRequest = false;
    private string _LoopingSongUrl = "";
    private List<string> _SongBeenPlayedList = new List<string>();
    GetChampService champService;
    private bool _isRelatedOn = false;
    private SocketGuildUser? _uuser;
    private bool _RelateSwitch = true;
    private bool _isEarRapeOn = false;
    private InteractionService? _interactionService;
    private IServiceProvider? _services;
    private GoogleAIStudioService _googleAIStudioService;
    private OpenRouterService _openRouterService;
    private SetTextService _setTextService;
    private string _cookie;
    
    #endregion

    #region 基礎設定
    public static Task Main(string[] args) => new Program().RunBotAsync();
    public async Task RunBotAsync()
    {
        var config = new DiscordSocketConfig
        {
            LogLevel = LogSeverity.Debug,
            EnableVoiceDaveEncryption = true,
            GatewayIntents = GatewayIntents.GuildMessages |
                             GatewayIntents.MessageContent |
                             GatewayIntents.Guilds |
                             GatewayIntents.GuildVoiceStates |
                             GatewayIntents.GuildMessageReactions |
                             GatewayIntents.GuildMembers
        };

        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(); // 加這行

        IConfiguration configer = builder.Build();
        string token = configer["Discord:Token"];
        string googleAIStudioApiKey = configer["GoogleAIStudio:dcBotKey1"];
        string googleAIStudioApiKey2 = configer["GoogleAIStudio:dcBotKey2"];
        string openRouterApiKey = configer["Openrouter:ApiKey1"];
        _cookie = configer["YT_DLP_COOKIES"];

        string redisConn = configer["Redis:ConnectionString"];
        var setTextService = new SetTextService(redisConn);

        _client = new DiscordSocketClient(config);
        _commands = new CommandService();
        _interactionService = new InteractionService(_client);
        string elevenLabsApiKey = configer["ElevenLabs:ApiKey"];

        // 設置依賴注入
        _services = new ServiceCollection()
            .AddSingleton(_client)
            .AddSingleton(_interactionService)
            .AddSingleton(this)
            .AddSingleton<WordGuessingService>()
            .AddSingleton<MineGameService>()
            .AddSingleton<RubiksCubeService>()
            .AddSingleton<GetChampService>()
            .AddSingleton<OldMaidService>()
            .AddSingleton<Game2048Service>()
            .AddSingleton<Pick2Service>()
            .AddSingleton<RVC_Service>()
            .AddSingleton<SetTextService>(setTextService)
            .AddSingleton<ElevenLabsService>(sp =>
                new ElevenLabsService(
                    sp.GetRequiredService<DiscordSocketClient>(),
                    elevenLabsApiKey
                ))
            .AddSingleton<GoogleAIStudioService>(sp =>
                new GoogleAIStudioService(googleAIStudioApiKey, googleAIStudioApiKey2)
                )
            .AddSingleton<OpenRouterService>(sp =>
                  new OpenRouterService(openRouterApiKey)
                  )
            .AddSingleton<JikanAnimeService>()
              .BuildServiceProvider();

        _googleAIStudioService = _services.GetRequiredService<GoogleAIStudioService>();
        _openRouterService = _services.GetRequiredService<OpenRouterService>();
        _setTextService = _services.GetRequiredService<SetTextService>();

        _client.MessageReceived += MessageReceivedHandler;
        _client.Log += Log;
        _client.Ready += ClientReady;
        _client.InteractionCreated += InteractionCreated;

        _ = SetBotStatusAsync(_client);

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
        await Task.Delay(-1);
    }


    #endregion

    #region 額外的handler
    private async Task InteractionCreated(SocketInteraction interaction)
    {
        if (interaction is SocketMessageComponent component)
        {
            // 處理踩地雷按鈕
            if (component.Data.CustomId.StartsWith("mine_"))
            {
                var parts = component.Data.CustomId.Split('_');
                if (parts.Length == 4)
                {
                    ulong userId = ulong.Parse(parts[1]);
                    int x = int.Parse(parts[2]);
                    int y = int.Parse(parts[3]);

                    var mineService = _services.GetService<MineGameService>();
                    var (newComponent, embed, gameOver) = await mineService.HandleButtonClick(component, x, y);

                    await component.UpdateAsync(msg =>
                    {
                        msg.Embed = embed;
                        msg.Components = newComponent?.Build();
                    });
                }
            }
            // ✅ 新增魔術方塊按鈕處理
            else if (component.Data.CustomId.StartsWith("cube_"))
            {
                var parts = component.Data.CustomId.Split('_');
                if (parts.Length == 3)  // ✅ 改為3個部分
                {
                    // ❌ 移除這行: ulong userId = ulong.Parse(parts[1]);
                    string action = parts[1];      // ✅ action 在第二位
                    bool clockwise = parts[2] == "1"; // ✅ clockwise 在第三位

                    var cubeService = _services.GetService<RubiksCubeService>();

                    if (action == "RESET")
                    {
                        var (comp, emb) = cubeService.ResetGame(component.Channel.Id);
                        await component.UpdateAsync(msg =>
                        {
                            msg.Embed = emb;
                            msg.Components = comp.Build();
                        });
                    }
                    else if (action == "END")
                    {
                        var emb = cubeService.EndGame(component.Channel.Id);
                        await component.UpdateAsync(msg =>
                        {
                            msg.Embed = emb;
                            msg.Components = null;
                        });
                    }
                    else
                    {
                        var (comp, emb) = await cubeService.HandleRotation(component, action, clockwise);
                        await component.UpdateAsync(msg =>
                        {
                            msg.Embed = emb;
                            msg.Components = comp?.Build();
                        });
                    }
                }
            }
            // 處理 2048 遊戲按鈕
            else if (component.Data.CustomId.StartsWith("2048_"))
            {
                var parts = component.Data.CustomId.Split('_');
                if (parts.Length >= 2)
                {
                    string direction = parts[1];
                    var user = component.User as SocketGuildUser;
                    var game2048Service = _services.GetService<Game2048Service>();
                    var (newComponent, embed) = await game2048Service.HandleButtonClick(component, direction, user.DisplayName);

                    await component.UpdateAsync(msg =>
                    {
                        msg.Embed = embed;
                        msg.Components = newComponent?.Build();
                    });
                }
            }
            // 處理二選一遊戲按鈕
            else if (component.Data.CustomId.StartsWith("pick2_"))
            {
                // 立即延遲回應，避免 3 秒超時
                await component.DeferAsync();

                var parts = component.Data.CustomId.Split('_');
                if (parts.Length >= 2)
                {
                    string action = parts[1];
                    var pick2Service = _services.GetService<Pick2Service>();

                    if (action == "vote" && parts.Length >= 3)
                    {
                        int choice = int.Parse(parts[2]);
                        var (imageMessage, newComponent, embed, gameOver) = await pick2Service.HandleVoteAsync(component, choice);

                        // 先更新投票訊息（快）
                        await component.Message.ModifyAsync(msg =>
                        {
                            msg.Embed = embed;
                            msg.Components = newComponent?.Build();
                        });

                        // 再更新圖片訊息（慢，但不影響互動）
                        var gameState = pick2Service.GetGameState(component.Channel.Id);
                        if (gameState != null && gameState.ImageMessageId != 0)
                        {
                            try
                            {
                                var imageMsg = await component.Channel.GetMessageAsync(gameState.ImageMessageId);
                                if (imageMsg is IUserMessage userMsg)
                                {
                                    await userMsg.ModifyAsync(msg => msg.Content = imageMessage);
                                }
                            }
                            catch { /* 忽略圖片訊息更新失敗 */ }
                        }
                    }
                    else if (action == "finish")
                    {
                        var (imageMessage, newComponent, embed) = await pick2Service.FinishRoundAsync(component.Channel.Id);

                        // 先更新投票訊息
                        await component.Message.ModifyAsync(msg =>
                        {
                            msg.Embed = embed;
                            msg.Components = newComponent?.Build();
                        });

                        // 再更新圖片訊息
                        var gameState = pick2Service.GetGameState(component.Channel.Id);
                        if (gameState != null && gameState.ImageMessageId != 0)
                        {
                            try
                            {
                                var imageMsg = await component.Channel.GetMessageAsync(gameState.ImageMessageId);
                                if (imageMsg is IUserMessage userMsg)
                                {
                                    await userMsg.ModifyAsync(msg => msg.Content = imageMessage);
                                }
                            }
                            catch { /* 忽略圖片訊息更新失敗 */ }
                        }
                    }
                    else if (action == "reset")
                    {
                        pick2Service.ResetGame(component.Channel.Id);

                        var embed = new EmbedBuilder()
                        {
                            Title = "🔄 遊戲已重置",
                            Description = "請使用 `/pick2` 指令開始新的遊戲",
                            Color = Color.Green
                        }.Build();

                        await component.Message.ModifyAsync(msg =>
                        {
                            msg.Embed = embed;
                            msg.Components = new ComponentBuilder().Build();
                        });
                    }
                }
            }
            else if (component.Data.CustomId.StartsWith("oldmaid_draw_"))
            {
                // 立即延遲回應
                await component.DeferAsync();

                var position = int.Parse(component.Data.CustomId.Split('_')[2]);
                var oldMaidService = _services.GetService<OldMaidService>();
                var (message, newComponent, needFollowup, followupMessage) = await oldMaidService.DrawCard(
                    component.Channel,
                    component.User as SocketGuildUser,
                    position
                );

                // 使用 ModifyOriginalResponseAsync 更新原訊息，而不是發新訊息
                await component.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Content = message;
                    msg.Components = newComponent?.Build();
                });

                // 如果需要後續訊息（電腦玩家的回合）
                if (needFollowup && !string.IsNullOrEmpty(followupMessage))
                {
                    await Task.Delay(500); // 稍微延遲讓玩家看到上一個動作

                    var finalComponent = oldMaidService.GetDrawButtons(component.Channel);

                    // 再次更新同一個訊息
                    await component.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Content = message + "\n\n" + followupMessage;
                        msg.Components = finalComponent?.Build();
                    });
                }
            }
            // 處理動畫猜謎按鈕
            else if (component.Data.CustomId.StartsWith("anime_guess_"))
            {
                await component.DeferAsync();

                var parts = component.Data.CustomId.Split('_');
                if (parts.Length == 5)
                {
                    int selectedId = int.Parse(parts[2]);
                    int correctId = int.Parse(parts[3]);
                    string correctName = parts[4];

                    var jikanService = _services.GetService<JikanAnimeService>();
                    var (embed, newComponent) = await jikanService.HandleButtonClickAsync(component, selectedId, correctId, correctName);

                    await component.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Embed = embed;
                        msg.Components = newComponent?.Build();
                    });
                }
            }
        }
        else
        {
            var context = new SocketInteractionContext(_client, interaction);
            await _interactionService.ExecuteCommandAsync(context, _services);
        }
    }

    private async Task ClientReady()
    {
        // 註冊 Slash Commands
        await _interactionService.AddModuleAsync<MusicBot2.SlahCommands.SlashCommandHandler>(_services);

        // 可選：僅在特定伺服器註冊（開發用）
        // await _interactionService.RegisterCommandsToGuildAsync(YOUR_GUILD_ID);

        // 全域註冊（可能需要最多 1 小時生效）
        await _interactionService.RegisterCommandsGloballyAsync();
    }
    public Task Log(LogMessage log)
    {
        Console.WriteLine(log);
        return Task.CompletedTask;
    }

    public static async Task SetBotStatusAsync(DiscordSocketClient _client)
    {
        while (true)
        {
            await _client.SetGameAsync("搜幽林轉生☆大★爆☆誕★", null, ActivityType.CustomStatus);
            await Task.Delay(20000);
            await _client.SetGameAsync("老娘soyo上雲端啦 在頻道輸入/可以看到老娘的一堆指令", null, ActivityType.CustomStatus);
            await Task.Delay(20000);
            await _client.SetGameAsync("傻逼DISCORD加密 不如我苦來溪苦一根", null, ActivityType.CustomStatus);
            await Task.Delay(20000);
            await _client.SetGameAsync("小祥辛酸打工畫面流出", "https://www.youtube.com/watch?v=_1xcBdtwEE4&ab_channel=supanasu", ActivityType.CustomStatus);
            await Task.Delay(10000);
            await _client.SetGameAsync("正在重組CRYCHIC", null, ActivityType.CustomStatus);
            await Task.Delay(10000);
            await _client.SetGameAsync("CRYCHIC新成員演唱", "https://www.youtube.com/watch?v=f9p0HWDQHxs&ab_channel=nlnl", ActivityType.CustomStatus);
            await Task.Delay(10000);
            await _client.SetGameAsync("有考慮當貝斯手嗎 我當然有考慮當貝斯手啊，那是我的夢想耶。我跟你說：當貝斯手比當工程師……我當……我當貝斯手，是……最想當的", null, ActivityType.CustomStatus);
            await Task.Delay(10000);
            await _client.SetGameAsync("寫程式真的很莫名其妙", null, ActivityType.CustomStatus);
            await Task.Delay(10000);
            await _client.SetGameAsync("那大家得多注意健康才行了", null, ActivityType.CustomStatus);
            await Task.Delay(10000);
            await _client.SetGameAsync("知ってたら止めたし😭セトリはもう終わってたのに急に演奏しだして😭みんなを止められなくてごめんね😭祥ちゃん、怒ってるよね😭怒るのも当然だと思う😭でも信じて欲しいの。春日影、本当に演奏する予定じゃなかったの😭本当にごめんね😭もう勝手に演奏したりしないって約束するよ😭ほかの子たちにも絶対にしないって約束させるから😭少しだけ話せないかな😭私、CRYCHICのこと本当に大切に思ってる😭だから、勝手に春日影演奏されたの祥ちゃんと同じくらい辛くて😭私の気持ちわかってほしいの😭お願い。どこても行くから😭バンドやらなきゃいけなかった理由もちゃんと話すから😭会って話せたら、きっとわかってもらえると思う😭私は祥ちゃんの味方だから😭会いたいの😭", null, ActivityType.CustomStatus);
            await Task.Delay(10000);
        }
    }
    #endregion

    #region MSreceive
    public async Task MessageReceivedHandler(SocketMessage message)
    {
        if (message is not SocketUserMessage userMessage || message.Author.IsBot) return;
        bool isMentioned = message.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id);

        if (isMentioned ||
            message.Content.ToLower().Contains("soyo") ||
            message.Content.ToLower().Contains("搜幽林") ||
                message.Content.ToLower().Contains("crychic") ||
                message.Content.ToLower().Contains("長期") ||
                message.Content.ToLower().Contains("爽世") ||
                message.Content.ToLower().Contains("爽食") ||
                message.Content.ToLower().Contains("素食"))
        {
            var talker = message.Author as SocketGuildUser;
            // 用「伺服器 + 頻道」當記憶 key，避免不同頻道上下文互相污染
            var channelKey = $"{message.Channel.Id}";

            string result = string.Empty;

            if (message.Reference != null)
            {
                var repliedMessage = await message.Channel.GetMessageAsync(message.Reference.MessageId.Value);
                if (repliedMessage != null)
                {
                    result = await _openRouterService.GenerateTextAsync(message.Content, talker, true, channelKey, repliedMessage);
                    await message.Channel.SendMessageAsync(result);
                    return; // 已處理完畢，直接返回
                }
            }
            else
            {
                result = await _openRouterService.GenerateTextAsync(message.Content, talker, true, channelKey);
                await message.Channel.SendMessageAsync(result);
            }
        }

        string match = await _setTextService.Match(message.Content.ToLower());
        if (!string.IsNullOrEmpty(match))
        {
            await message.Channel.SendMessageAsync(match);
        }

        if (!message.Content.StartsWith("$$")) return;
        string cmd = message.Content.Substring(2);
        var channel = message.Channel as IMessageChannel;
        var user = message.Author as SocketGuildUser;
        _uuser = user;
        champService = new GetChampService();

        if (user == null)
            return;

        //撥放
        if (cmd.StartsWith("play"))
        {
            var query = cmd.Substring(4).Trim();
            await PlayMusicAsync(channel, user, query);
        }
        else if (cmd.ToLower().StartsWith("skill"))
        {
            var champName = cmd.Substring(5).Trim();
            await champService.GetChampSkillsAsync(channel, champName);
        }
        else if (cmd.ToLower().StartsWith("mine"))
        {
            var height = int.Parse(cmd.Substring(5).ToLower().Trim().Split(' ')[0]);
            var width = int.Parse(cmd.Substring(5).ToLower().Trim().Split(' ')[1]);
            var mineService = _services.GetService<MineGameService>();
            var (component, embed) = await mineService.StartGameAsync(user.Id, height, width);
            await channel.SendMessageAsync(embed: embed, components: component.Build());
        }
        else if (cmd.ToLower().StartsWith("guess"))
        {
            //$$guess {英雄名} {技能位置 P,Q,W,E,R} {使用者猜測的名字}
            var champName = cmd.Substring(5).ToLower().Trim().Split(' ')[0];
            var skillPos = cmd.Substring(5).ToLower().Trim().Split(' ')[1];
            var userGuess = cmd.Substring(5).ToLower().Trim().Split(' ')[2];
            await champService.GuessChampSkillAsync(channel, champName, skillPos, userGuess);
        }
        else if (cmd.ToLower().StartsWith("p"))
        {
            var query = cmd.Substring(1).Trim();
            await PlayMusicAsync(channel, user, query);
        }
        //bilibili
        else if (cmd.StartsWith("b"))
        {
            var url = cmd.Substring(1).Trim();
            await PlayBiblibiliMusicAsync(channel, user, url);
        }
        //跳過
        else if (cmd.ToLower().StartsWith("s") || cmd.StartsWith("skip"))
        {
            await SkipMusic(channel, user);
        }
        //循環和解除
        else if (cmd.ToLower().StartsWith("loop") || cmd.ToLower().StartsWith("lo"))
        {
            await LoopMusic(channel, user);
        }
        else if (cmd.ToLower().StartsWith("unloop") || cmd.ToLower().StartsWith("u"))
        {
            await UnLoopMusic(channel, user);
        }
        //推薦
        else if (cmd.ToLower().StartsWith("r"))
        {
            if (_RelateSwitch)
            {
                _RelateSwitch = false;
                await RelatedMusicAsync(channel, user);
            }
            else
            {
                _RelateSwitch = true;
                _isRelatedOn = false;
                _SongBeenPlayedList.Clear();
                await channel.SendMessageAsync("取消推薦");
            }
        }
        //查詢
        else if (cmd.ToLower().StartsWith("find"))
        {
            var query = cmd.Substring(4).Trim();
            string url = await GetYoutubeUrlByNameAsync(channel, query);
            if (url == "")
            {
                Console.WriteLine("空");
                return;
            }
            else
            {
                await PlayMusicAsync(channel, user, url);
            }
        }
        else if (cmd.ToLower().StartsWith("f"))
        {
            var query = cmd.Substring(1).Trim();
            string url = await GetYoutubeUrlByNameAsync(channel, query);
            if (url == "")
            {
                Console.WriteLine("空");
                return;
            }
            else
            {
                await PlayMusicAsync(channel, user, url);
            }
        }
        //列出清單
        else if (cmd.ToLower().StartsWith("li"))
        {
            await CalledPlayListAsync(channel, user);
        }
        //爆
        else if (cmd.ToLower().StartsWith("e") || cmd.StartsWith("爆"))
        {
            await EarRapeAsync(channel, user);
        }
        else
        {
            await channel.SendMessageAsync("亂打一通");
        }
    }
    #endregion

    #region 撥放音樂事件
    public async Task PlayMusicAsync(IMessageChannel channel, SocketGuildUser user, string query)
    {
        if (user?.VoiceChannel == null)
        {
            await channel.SendMessageAsync("不進語音房是要撥個ㄐ8? 我去妳房間撥你衣服比較快 ");
            return;
        }

        if (!await CheckYoutubeUrlAliveAsync(query, channel) && !_isRelatedOn)
        {
            await channel.SendMessageAsync($"連結已經死了: {{ {query} }}");
            return;
        }


        var voiceChannel = user.VoiceChannel;
        _songQueue.Enqueue(query);



        if (!_isPlaying)
        {
            _isPlaying = true;
            await CalledPlayListAsync(channel, user);
            _ = Task.Run(async () =>
            {
                await PlayNextSongAsync(channel, voiceChannel);
            });
        }
        else
        {
            if (!_isRelatedOn)
            {
                await CalledPlayListAsync(channel, user);
            }

        }
    }
    public async Task PlayBiblibiliMusicAsync(IMessageChannel channel, SocketGuildUser user, string url)
    {
        try
        {
            if (user?.VoiceChannel == null)
            {
                await channel.SendMessageAsync("不進語音房是要撥個ㄐ8? 我去妳房間撥你衣服比較快 ");
                return;
            }

            var voiceChannel = user.VoiceChannel;
            _songQueue.Enqueue(url);

            if (!_isPlaying)
            {
                _isPlaying = true;
                await CalledPlayListForBBAsync(channel, user);
                _ = Task.Run(async () =>
                {
                    await PlayNextSongAsync(channel, voiceChannel);
                });
            }
            else
            {
                if (!_isRelatedOn)
                {
                    await CalledPlayListAsync(channel, user);
                }

            }
        }
        catch (Exception ex)
        {
            await channel.SendMessageAsync("下載失敗摟");
            await channel.SendMessageAsync(ex.ToString());
        }
    }
    public async Task SkipMusic(IMessageChannel channel, SocketGuildUser user)
    {
        if (user?.VoiceChannel == null)
        {
            await channel.SendMessageAsync("不進語音房是要跳ㄐㄐ");
            return;
        }
        if (_isPlaying)
        {
            await channel.SendMessageAsync($"你這個人滿腦子都只想到自己呢 ");
            _isSkipRequest = true;
        }
        else
        {
            await channel.SendMessageAsync("沒歌了是要跳什麼");
        }
    }
    public async Task LoopMusic(IMessageChannel channel, SocketGuildUser user)
    {
        if (user?.VoiceChannel == null)
        {
            return;
        }
        if (_isPlaying)
        {
            await channel.SendMessageAsync($"組一輩子Crychic");
            _LoopingSongUrl = _NowPlayingSongUrl;
        }
        else
        {
            await channel.SendMessageAsync("沒歌了是要循環甚麼 戀愛嗎");
        }
    }
    public async Task UnLoopMusic(IMessageChannel channel, SocketGuildUser user)
    {
        if (user?.VoiceChannel == null)
        {
            await channel.SendMessageAsync("你不進語音是結束不掉的");
            return;
        }
        if (_isPlaying)
        {
            await channel.SendMessageAsync($"要持續一輩子是很困難的");
            _LoopingSongUrl = "";
        }
        else
        {
            await channel.SendMessageAsync("沒歌了 已經維持不下去了..");
        }
    }
    public async Task CalledPlayListAsync(IMessageChannel channel, SocketGuildUser user)
    {

        if (_songQueue.Count == 0)
        {
            await channel.SendMessageAsync("沒歌你還想要清單?");
            return;
        }

        var random = RandomColor();

        // 创建一个新的 EmbedBuilder
        var embedBuilder = new EmbedBuilder()
        {
            Title = "目前歌單資訊",
            Color = random
        };

        if (_songQueue.Count != 0)
        {
            embedBuilder.AddField("目前歌單數量", $"{_songQueue.Count.ToString()}", true);
        }


        if (!string.IsNullOrEmpty(_NowPlayingSongUrl))
        {
            embedBuilder.AddField("目前正在撥放名稱", await GetVideoIDAsync(_NowPlayingSongUrl), true);
        }
        if (!string.IsNullOrEmpty(_NowPlayingSongUrl))
        {
            embedBuilder.AddField("歌曲網址", _NowPlayingSongUrl, true);
            embedBuilder.AddField($"目前待撥清單", "=======================================================================", true);
        }
        int count = 0;
        // 添加待播放的歌曲列表
        foreach (var song in _songQueue)
        {
            count++;
            string a = song;
            string b = await GetVideoIDAsync(a);
            embedBuilder.AddField($"第 {count} 首", b, false);

        }

        // 发送 Embed 消息
        await channel.SendMessageAsync(embed: embedBuilder.Build());

    }
    public async Task CalledPlayListForBBAsync(IMessageChannel channel, SocketGuildUser user)
    {

        if (_songQueue.Count == 0)
        {
            await channel.SendMessageAsync("沒歌你還想要清單?");
            return;
        }

        var random = RandomColor();

        // 创建一个新的 EmbedBuilder
        var embedBuilder = new EmbedBuilder()
        {
            Title = "目前歌單資訊",
            Color = random
        };

        if (_songQueue.Count != 0)
        {
            embedBuilder.AddField("目前歌單數量", $"{_songQueue.Count.ToString()}", true);
        }


        if (!string.IsNullOrEmpty(_NowPlayingSongUrl))
        {
            embedBuilder.AddField("目前正在撥放名稱", await GetVideoIDAsync(_NowPlayingSongUrl), true);
        }
        if (!string.IsNullOrEmpty(_NowPlayingSongUrl))
        {
            embedBuilder.AddField("歌曲網址", _NowPlayingSongUrl, true);
            embedBuilder.AddField($"目前待撥清單", "=======================================================================", true);
        }
        int count = 0;
        // 添加待播放的歌曲列表
        foreach (var song in _songQueue)
        {
            count++;
            string a = song;
            string b = await GetBilibiliTitleAsync(a);
            embedBuilder.AddField($"第 {count} 首", b, false);

        }

        // 发送 Embed 消息
        await channel.SendMessageAsync(embed: embedBuilder.Build());

    }
    public async Task RelatedMusicAsync(IMessageChannel channel, SocketGuildUser user)
    {
        if (user?.VoiceChannel == null)
        {
            return;
        }
        string url;
        if (_isPlaying)
        {
            if (_SongBeenPlayedList.Count == 0)
            {
                _SongBeenPlayedList.Add(_NowPlayingSongID);
            }
            url = await SearchRelateVideoAsync(channel, _NowPlayingSongName);
            if (string.IsNullOrEmpty(url))
            {
                return;
            }
        }
        else
        {
            await channel.SendMessageAsync("沒點歌還想要推薦 那就聽春日影吧");
            _NowPlayingSongID = "-kZBuzsZ7Ho";
            url = "https://www.youtube.com/watch?v=-kZBuzsZ7Ho&ab_channel=MyGO%21%21%21%21%21-Topic";
            _SongBeenPlayedList.Add(_NowPlayingSongID);
        }
        _isRelatedOn = true;

        await PlayMusicAsync(channel, user, url);
    }

    // 新增公開方法供 SlashCommandHandler 使用
    public async Task HandleRelatedMusicAsync(IMessageChannel channel, SocketGuildUser user)
    {
        if (_RelateSwitch)
        {
            _RelateSwitch = false;
            await RelatedMusicAsync(channel, user);
        }
        else
        {
            _RelateSwitch = true;
            _isRelatedOn = false;
            _SongBeenPlayedList.Clear();
            await channel.SendMessageAsync("取消推薦");
        }
    }
    public async Task EarRapeAsync(IMessageChannel channel, SocketGuildUser user)
    {
        if (user?.VoiceChannel == null)
        {
            await channel.SendMessageAsync("要進語音诶 還是你想不進語音偷偷ear rape別人？ 想要的話跟我講 我改");
            return;
        }
        _isEarRapeOn = !_isEarRapeOn;
    }
    #endregion

    #region 撥放音樂
    //1可2可3不可   why??  =====> delay時間不夠長 貌似取決於電腦效能&網路
    public async Task PlayNextSongAsync(IMessageChannel channel, SocketVoiceChannel voiceChannel)
    {
        //songqueue為空 ／loop沒啟動／沒有開推薦
        if (_songQueue.Count == 0 && _LoopingSongUrl == "" && _isRelatedOn == false)
        {
            _isPlaying = false;
            await channel.SendMessageAsync("沒歌ㄌ");
            _NowPlayingSongUrl = "";
            return;
        }
        //推薦開啟／且歌單只剩一首歌時
        if (_isRelatedOn && _songQueue.Count == 1)
        {
            await RelatedMusicAsync(channel, _uuser);
        }
        _isPlaying = true;
        string songUrl;
        //正常情況
        if (_LoopingSongUrl == "")
        {
            songUrl = _songQueue.Dequeue(); // 取出下一首歌
        }
        //開啟loop時
        else
        {
            songUrl = _LoopingSongUrl;
        }
        _NowPlayingSongUrl = songUrl;
        string filepath = "";
        if (_isRelatedOn)
        {
            await CalledPlayListAsync(channel, _uuser);
        }

        try
        {
            Console.WriteLine("沒有下載前的檔案位置" + filepath);
            if (songUrl.Contains("bili"))
                filepath = await DownloadBilibiliAudioAsync(songUrl);
            else
                filepath = await DownloadAudioAsync(songUrl);
            Console.WriteLine("成功下載後的檔案位置" + filepath);

            await Task.Delay(2000);
            if (_audioClient == null || _audioClient.ConnectionState != Discord.ConnectionState.Connected)
                _audioClient = await voiceChannel.ConnectAsync(selfDeaf: false, selfMute: false);

            var output = _audioClient.CreatePCMStream(AudioApplication.Mixed);

            var ffmpegProcess = CreatePcmStreamProcess(filepath, _isEarRapeOn);

            // 檢查 FFmpeg 是否啟動成功
            if (ffmpegProcess == null)
            {
                await channel.SendMessageAsync("❌ FFmpeg 啟動失敗");
                Console.WriteLine("FFmpeg process 為 null");
                return;
            }

            // 讀取 FFmpeg 的錯誤輸出（用於除錯）
            _ = Task.Run(async () =>
            {
                var errorReader = ffmpegProcess.StandardError;
                string line;
                while ((line = await errorReader.ReadLineAsync()) != null)
                {
                    Console.WriteLine($"FFmpeg 錯誤: {line}");
                }
            });

            try
            {
                byte[] buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = await ffmpegProcess!.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (_isSkipRequest)
                    {
                        _isSkipRequest = false;
                        break;
                    }
                    await output.WriteAsync(buffer, 0, bytesRead);
                }
                await output.FlushAsync();
            }
            finally
            {
                ffmpegProcess?.Kill();
                ffmpegProcess?.Dispose();
            }

            File.Delete(filepath);
            output.Dispose();
            await PlayNextSongAsync(channel, voiceChannel);
        }
        catch (Exception ex)
        {
            await channel.SendMessageAsync($"我從來不覺得寫程式開心過:PlayNextSongAsync {ex.Message} {ex}");
        }
    }
    #endregion

    #region yt相關
    public async Task<string> GetVideoIDAsync(string url)
    {
        try
        {
            var (exitCode, output, error) = await ExecuteYtDlpAsync($"--get-title {url}");

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                return output.Trim();
            }

            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"GetVideoIDAsync 錯誤: {error}");
            }
            return "";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetVideoIDAsync 例外: {ex.Message}");
            return "";
        }
    }
    public async Task<string> DownloadAudioAsync(string url)
    {
        var tempDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
        Directory.CreateDirectory(tempDirectory);

        // 先取得影片 ID 和標題
        var videoId = await GetYoutubeVideoIdAsync(url);
        if (string.IsNullOrEmpty(videoId))
        {
            throw new Exception("連結無效");
        }

        _NowPlayingSongID = videoId;
        _NowPlayingSongName = await GetVideoIDAsync(url);

        var filePrefix = Guid.NewGuid().ToString();
        var outputTemplate = Path.Combine(tempDirectory, $"{filePrefix}.%(ext)s");

        // 使用更靈活的格式選擇：
        // 1. bestaudio[ext=m4a] - 嘗試取得 m4a 格式的最佳音質
        // 2. bestaudio - 如果沒有 m4a，取得任何格式的最佳音質
        // 3. best - 最後的備案，取得最佳品質的任何格式
        var (exitCode, output, error) = await ExecuteYtDlpAsync($"-f bestaudio[ext=m4a]/bestaudio/best -x --audio-format mp3 -o \"{outputTemplate}\" {url}");

        if (exitCode == 0)
        {
            var downloadedFile = Directory
                .EnumerateFiles(tempDirectory, $"{filePrefix}.*")
                .FirstOrDefault(f => Path.GetExtension(f).Equals(".mp3", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(downloadedFile))
                return downloadedFile;
        }

        Console.WriteLine($"YouTube 下載失敗: {error}");
        throw new Exception($"YouTube 下載失敗: {error}");
    }
    public async Task<string> GetYoutubeUrlByNameAsync(IMessageChannel channel, string query)
    {
        try
        {
            var (exitCode, output, error) = await ExecuteYtDlpAsync($"--get-id --default-search ytsearch1: \"{query}\"");

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var videoId = output.Trim();
                var videoUrl = $"https://www.youtube.com/watch?v={videoId}";
                await channel.SendMessageAsync(videoUrl);
                return videoUrl;
            }

            await channel.SendMessageAsync("找不到歌曲");
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"搜尋失敗: {error}");
            }
            return "";
        }
        catch (Exception ex)
        {
            await channel.SendMessageAsync($"我從來不覺得寫程式開心過:GetYoutubeUrlByName {ex.Message}");
            return "";
        }
    }
    public async Task<string> SearchRelateVideoAsync(IMessageChannel channel, string name)
    {
        string url = "";
        try
        {
            var modifiedTitle = GetRandomizedTitle(name, channel);

            // 使用 yt-dlp 搜尋，取得 20 個結果
            var (exitCode, output, error) = await ExecuteYtDlpAsync($"--get-id --flat-playlist --default-search ytsearch20: \"{modifiedTitle}\"");

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var random = new Random();
                var shuffledLines = lines.OrderBy(x => random.Next()).ToList();

                foreach (var videoId in shuffledLines)
                {
                    var trimmedId = videoId.Trim();
                    if (string.IsNullOrEmpty(trimmedId)) continue;

                    // 檢查是否已播放過
                    if (!_SongBeenPlayedList.Contains(trimmedId))
                    {
                        // 檢查影片長度
                        var duration = await GetVideoDurationAsync(trimmedId);
                        if (duration < TimeSpan.FromMinutes(10))
                        {
                            url = $"https://www.youtube.com/watch?v={trimmedId}";
                            _SongBeenPlayedList.Add(trimmedId);
                            break;
                        }
                    }
                }
            }
            else if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"搜尋相關影片失敗: {error}");
            }

            return url;
        }
        catch (Exception ex)
        {
            await channel.SendMessageAsync($"我從來不覺得寫程式開心過:SearchRelateVideoAsync {ex.Message}");
            return "";
        }
    }

    // 輔助方法：取得影片 ID
    private async Task<string> GetYoutubeVideoIdAsync(string url)
    {
        try
        {
            var (exitCode, output, error) = await ExecuteYtDlpAsync($"--get-id {url}");

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                return output.Trim();
            }
            return "";
        }
        catch
        {
            return "";
        }
    }

    // 輔助方法：取得影片長度
    private async Task<TimeSpan> GetVideoDurationAsync(string videoId)
    {
        try
        {
            var (exitCode, output, error) = await ExecuteYtDlpAsync($"--get-duration https://www.youtube.com/watch?v={videoId}");

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var duration = output.Trim();
                // 解析時間格式 (可能是 MM:SS 或 HH:MM:SS)
                if (TimeSpan.TryParse(duration, out var result))
                {
                    return result;
                }

                // 處理更複雜的格式 (例如 "5:30" 或 "1:05:30")
                var parts = duration.Split(':');
                if (parts.Length == 2) // MM:SS
                {
                    return new TimeSpan(0, int.Parse(parts[0]), int.Parse(parts[1]));
                }
                else if (parts.Length == 3) // HH:MM:SS
                {
                    return new TimeSpan(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
                }
            }

            return TimeSpan.MaxValue; // 如果無法取得，回傳最大值避免被選中
        }
        catch
        {
            return TimeSpan.MaxValue;
        }
    }
    #endregion

    #region
    public async Task<string> DownloadBilibiliAudioAsync(string url)
    {
        var tempDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
        Directory.CreateDirectory(tempDirectory);

        // 用 Guid 當做「檔名前綴」，但不指定副檔名
        var filePrefix = Guid.NewGuid().ToString();
        var outputTemplate = Path.Combine(tempDirectory, $"{filePrefix}.%(ext)s");

        // 使用 ExecuteYtDlpAsync 統一處理 cookie
        var (exitCode, output, error) = await ExecuteYtDlpAsync($"-f bestaudio[ext=m4a]/bestaudio/best -x --audio-format mp3 -o \"{outputTemplate}\" {url}");

        if (exitCode == 0)
{
            // 找出實際的 MP3 檔案
            var downloadedFile = Directory
                .EnumerateFiles(tempDirectory, $"{filePrefix}.*")
                .FirstOrDefault(f => Path.GetExtension(f).Equals(".mp3", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(downloadedFile))
                return downloadedFile;
        }

        Console.WriteLine($"Bilibili 下載失敗: {error}");
        throw new Exception($"Bilibili 下載失敗搂 OB一串字母女士非常不開心！Error: {error}");

    }

    public async Task<string> GetBilibiliTitleAsync(string url)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            Arguments = $"--get-title {url}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            return output.Trim();
        }
        else
        {
            return "[取得 Bilibili 標題失敗]";
        }
    }



    #endregion
    #region 自訂func

    // YouTube Cookie 處理（從環境變數讀取）
    private string GetYtDlpCookieArgument()
    {
        try
        {
            var cookieContent = _cookie;
            if (string.IsNullOrEmpty(cookieContent))
            {
                Console.WriteLine("未設定 YT_DLP_COOKIES 環境變數");
                return "";
            }

            // 建立 cookies 目錄
            var cookieDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cookies");
            Directory.CreateDirectory(cookieDir);

            var cookieFile = Path.Combine(cookieDir, "youtube_cookies.txt");

            // 寫入 cookie 檔案（覆蓋舊的）
            File.WriteAllText(cookieFile, cookieContent);

            Console.WriteLine($"已載入 YouTube cookies: {cookieFile}");
            return $"--cookies \"{cookieFile}\"";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"載入 YouTube cookies 失敗: {ex.Message}");
            return "";
        }
    }

    // 統一的 yt-dlp 執行方法
    private async Task<(int exitCode, string output, string error)> ExecuteYtDlpAsync(string arguments)
    {
        var cookieArg = GetYtDlpCookieArgument();

        // Cookie 參數需要放在前面
        // 對於非下載操作（如 --get-title, --get-id 等），添加 --skip-download
        var isDownloadCommand = arguments.Contains("-o ") || arguments.Contains("--output");
        var extraArgs = isDownloadCommand ? "" : "--skip-download ";

        var fullArguments = string.IsNullOrEmpty(cookieArg) 
            ? $"{extraArgs}{arguments}"
            : $"{cookieArg} {extraArgs}{arguments}";

        Console.WriteLine($"執行 yt-dlp 命令: yt-dlp {fullArguments}");

        var psi = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            Arguments = fullArguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            Console.WriteLine($"yt-dlp 錯誤輸出: {error}");
        }

        return (process.ExitCode, output, error);
    }

    public Process CreatePcmStreamProcess(string path, bool isEarRape = false)
    {
        string ffmpegPath;

        // 檢測作業系統類型
        if (OperatingSystem.IsWindows())
        {
            // Windows 環境：嘗試使用專案內的 ffmpeg
            string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string winFfmpeg = Path.Combine(projectRoot, "ffmpeg-master-latest-win64-gpl-shared", "bin", "ffmpeg.exe");
            ffmpegPath = File.Exists(winFfmpeg) ? winFfmpeg : "ffmpeg";
        }
        else
        {
            // Linux/Docker 環境：使用系統安裝的 ffmpeg
            ffmpegPath = "ffmpeg";
        }

        var audioFilter = isEarRape
            ? "-af \"volume=10,asetrate=48000*0.1,aresample=48000\""
            : "";

        return Process.Start(new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-hide_banner -loglevel panic -i \"{path}\" {audioFilter} -ac 2 -f s16le -ar 48000 pipe:1",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
    }

    public string GetRandomizedTitle(string title, IMessageChannel channel)
    {
        var _ignoreKeywords = new List<string>
    {
        "official", "video", "mv", "lyrics", "audio", "remastered", "hd", "live", "version", "ft.", "feat", "featuring","歌詞","拼音","ver" ,"music","movie","tv","高画質","amv","mad","1k","2k","3k","4k"
        ,"弾き語り","fps" ,"hdr" ,"ultra","實況","精華","アニメ","official youTube channel"
    };
        StringBuilder sb = new StringBuilder();
        string ai = "";
        string pattern = string.Join("|", _ignoreKeywords.Select(Regex.Escape));
        string cleanTitle = Regex.Replace(title.ToLower(), $@"({pattern})", ",", RegexOptions.IgnoreCase).Trim();
        sb.AppendLine($"移除贅字後的title：{cleanTitle}");
        sb.AppendLine("=========================");
        var parts = Regex.Split(cleanTitle, @"[-|/【】『』「」，:：《》〈〉＜＞<>‧．·，、。＊＆＃※§′‵〞〝”“’!！()（）｛｝｜  \-.,#〔＋〕@的 ‘'[\]]").Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        foreach (var p in parts)
        {
            if (p.StartsWith("ai"))
            {
                string s = RandomAI();
                ai = $"【Ai {s}唱】";
                channel.SendMessageAsync($"查詢條件:{ai}");
                return ai;
            }
            sb.Append($" {p}     ");
        }
        sb.Remove((sb.Length - 1), 1);
        sb.Append('\n');
        sb.AppendLine("=========================");
        if (parts.Count == 0)
        {
            return cleanTitle;
        }

        var random = new Random();
        int index = random.Next(parts.Count);
        sb.AppendLine($"最後選中的：{parts[index]}");
        channel.SendMessageAsync(sb.ToString());
        return (parts[index]);
    }
    public async Task<bool> CheckYoutubeUrlAliveAsync(string url, IMessageChannel channel)
    {
        try
        {
            // 使用 yt-dlp 檢查影片是否有效
            var (exitCode, output, error) = await ExecuteYtDlpAsync($"--get-title {url}");

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var title = output.Trim();
                await channel.SendMessageAsync($"有取得標題 {title}");
                _NowPlayingSongName = title;
                return true;
            }

            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"检查视频有效性时发生错误: {error}");
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"检查视频有效性时发生错误: {ex.Message}");
            return false;
        }
    }
    public Color RandomColor()
    {
        var colors = new List<Color>
{
    Color.Blue,
    Color.Purple,
    Color.DarkBlue,
    Color.DarkerGrey,
    Color.DarkPurple,
    Color.DarkMagenta
};

        // 创建一个随机数生成器
        var random = new Random();

        // 随机选择一个颜色
        var randomColor = colors[random.Next(colors.Count)];
        return randomColor;
    }
    public string RandomAI()
    {
        var random = new Random();
        List<string> singer = new List<string>();
        singer.Add("NL");
        singer.Add("Roger");
        singer.Add("羅傑");
        singer.Add("統神");
        singer.Add("toyz");
        singer.Add("RB");

        string s = "";
        s = singer[random.Next(singer.Count)];

        return s;
    }

    #endregion
}
