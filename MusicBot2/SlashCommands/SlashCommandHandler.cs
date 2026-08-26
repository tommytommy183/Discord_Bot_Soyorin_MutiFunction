using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using ElevenLabs.Models;
using ElevenLabs.Voices;
using InstagramApiSharp.Classes;
using Microsoft.VisualBasic;
using MusicBot2.Helpers;
using MusicBot2.Models;
using MusicBot2.Service;
using RiotSharp.Misc;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;
using static System.Net.WebRequestMethods;

namespace MusicBot2.SlahCommands
{
    public class SlashCommandHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly Program _program;
        private readonly WordGuessingService _wordService;
        private readonly MineGameService _mineGameService;
        private readonly ElevenLabsService _elevenLabsService;
        private readonly OldMaidService _oldMaidService;
        private readonly RubiksCubeService _rubiksCubeService;
        private readonly GoogleAIStudioService _googleAIStudioService;
        private readonly OpenRouterService _openRouterService;
        private readonly RVC_Service _rVC_Service;
        private readonly SetTextService _setTextService;
        private readonly Game2048Service _game2048Service;
        private readonly Game1A2BService _game1A2BService;
        private readonly Pick2Service _pick2Service;
        private readonly JikanAnimeService _animeService;
        private readonly PokeService _pokeService;
        private readonly PokeGameService _pokeGameService;
        private readonly ValorantService _valorantService;
        private readonly TRPGService _trpgService;
        private readonly LyrisService _lyrisService;
        private readonly LyricsDisplayService _lyricsDisplayService;
        private readonly UselessApiService _uselessApiService;
        private readonly NekoBotService _nekoBotService;
        private readonly WaifuImService _waifuImService;
        private readonly WaifuPicsService _waifuPicsService;
        private readonly AIImageService _aiImageService;
        private readonly GroqWhisperService _groqWhisperService;
        private readonly FishAudioService _fishAudioService;
        private readonly PokeTowerService _pokeTowerService;
        private readonly FgoGuessService _fgoGuessService;
        private readonly HolyGrailTowerService _holyGrailTowerService;
        private readonly YgoDuelService _ygoService;
        private readonly FreeDuelService _freeDuelSvc;

        public SlashCommandHandler(Program program, WordGuessingService wordService, MineGameService mineGameService, ElevenLabsService elevenLabsService, OldMaidService oldMaidService, RubiksCubeService rubiksCubeService, GoogleAIStudioService googleAIStudioService, OpenRouterService openRouterService, RVC_Service rVC_Service, SetTextService setTextService, Game2048Service game2048Service, Game1A2BService game1A2BService, Pick2Service pick2Service, JikanAnimeService animeService, PokeService pokeService, PokeGameService pokeGameService, ValorantService valorantService, TRPGService trpgService, LyrisService lyrisService, LyricsDisplayService lyricsDisplayService, UselessApiService uselessApiService, NekoBotService nekoBotService, WaifuImService waifuImService, WaifuPicsService waifuPicsService, AIImageService aiImageService, GroqWhisperService groqWhisperService, FishAudioService fishAudioService, PokeTowerService pokeTowerService, FgoGuessService fgoGuessService, HolyGrailTowerService holyGrailTowerService, YgoDuelService ygoService, FreeDuelService freeDuelService)
        {
            _program = program;
            _wordService = wordService;
            _elevenLabsService = elevenLabsService;
            _mineGameService = mineGameService;
            _setTextService = setTextService;
            _oldMaidService = oldMaidService;
            _rubiksCubeService = rubiksCubeService;
            _googleAIStudioService = googleAIStudioService;
            _openRouterService = openRouterService;
            _rVC_Service = rVC_Service;
            _game2048Service = game2048Service;
            _game1A2BService = game1A2BService;
            _pick2Service = pick2Service;
            _animeService = animeService;
            _pokeService = pokeService;
            _pokeGameService = pokeGameService;
            _valorantService = valorantService;
            _trpgService = trpgService;
            _lyrisService = lyrisService;
            _lyricsDisplayService = lyricsDisplayService;
            _uselessApiService = uselessApiService;
            _nekoBotService = nekoBotService;
            _waifuImService = waifuImService;
            _waifuPicsService = waifuPicsService;
            _aiImageService = aiImageService;
            _groqWhisperService = groqWhisperService;
            _fishAudioService = fishAudioService;
            _pokeTowerService = pokeTowerService;
            _fgoGuessService = fgoGuessService;
            _holyGrailTowerService = holyGrailTowerService;
            _ygoService = ygoService;
            _freeDuelSvc = freeDuelService;
        }
        #region 音樂撥放相關 > 先拿掉，要撥放音樂用$$就好
        //[SlashCommand("播放音樂", "播放音樂")]
        //public async Task PlayCommand([Summary("查詢", "YouTube URL 或搜尋關鍵字")] string query)
        //{
        //    await DeferAsync();
        //    var user = Context.User as SocketGuildUser;
        //    await _program.PlayMusicAsync(Context.Channel, user, query);
        //    await FollowupAsync("-", ephemeral: true);
        //}

        //[SlashCommand("撥放bilibili", "播放 Bilibili 音樂")]
        //public async Task BilibiliCommand([Summary("網址", "Bilibili 影片網址")] string url)
        //{
        //    await DeferAsync();
        //    var user = Context.User as SocketGuildUser;
        //    await _program.PlayBiblibiliMusicAsync(Context.Channel, user, url);
        //    await FollowupAsync("-", ephemeral: true);
        //}

        //[SlashCommand("跳過目前歌曲", "跳過目前歌曲")]
        //public async Task SkipCommand()
        //{
        //    await DeferAsync();
        //    var user = Context.User as SocketGuildUser;
        //    await _program.SkipMusic(Context.Channel, user);
        //    await FollowupAsync("-", ephemeral: true);
        //}

        //[SlashCommand("循環播放目前歌曲", "循環播放目前歌曲")]
        //public async Task LoopCommand()
        //{
        //    await DeferAsync();
        //    var user = Context.User as SocketGuildUser;
        //    await _program.LoopMusic(Context.Channel, user);
        //    await FollowupAsync("-", ephemeral: true);
        //}

        //[SlashCommand("取消循環撥放", "取消循環播放")]
        //public async Task UnloopCommand()
        //{
        //    await DeferAsync();
        //    var user = Context.User as SocketGuildUser;
        //    await _program.UnLoopMusic(Context.Channel, user);
        //    await FollowupAsync("-", ephemeral: true);
        //}

        //[SlashCommand("開關推薦音樂", "開啟/關閉推薦音樂")]
        //public async Task RelatedCommand()
        //{
        //    await DeferAsync();
        //    var user = Context.User as SocketGuildUser;
        //    await _program.HandleRelatedMusicAsync(Context.Channel, user);
        //    await FollowupAsync("-", ephemeral: true);
        //}

        //[SlashCommand("搜尋並播放音樂", "搜尋並播放音樂")]
        //public async Task FindCommand([Summary("關鍵字", "搜尋關鍵字")] string query)
        //{
        //    await DeferAsync();
        //    var user = Context.User as SocketGuildUser;
        //    string url = await _program.GetYoutubeUrlByNameAsync(Context.Channel, query);
        //    if (!string.IsNullOrEmpty(url))
        //    {
        //        await _program.PlayMusicAsync(Context.Channel, user, url);
        //    }
        //    await FollowupAsync("-", ephemeral: true);
        //}

        //[SlashCommand("顯示目前播放清單", "顯示目前播放清單")]
        //public async Task ListCommand()
        //{
        //    await DeferAsync();
        //    var user = Context.User as SocketGuildUser;
        //    await _program.CalledPlayListAsync(Context.Channel, user);
        //    await FollowupAsync("-", ephemeral: true);
        //}

        //[SlashCommand("開關earrape", "開啟/關閉 Ear Rape 模式")]
        //public async Task EarRapeCommand()
        //{
        //    await DeferAsync();
        //    var user = Context.User as SocketGuildUser;
        //    await _program.EarRapeAsync(Context.Channel, user);
        //    await FollowupAsync("-", ephemeral: true);
        //}
        #endregion

        #region Riot相關
        [SlashCommand("查詢英雄技能", "查詢英雄技能")]
        public async Task SkillCommand([Summary("英雄名", "英雄名稱")] string champName)
        {
            await DeferAsync();
            var champService = new GetChampService();
            await champService.GetChampSkillsAsync(Context.Channel as IMessageChannel, champName);
            await FollowupAsync("-", ephemeral: true);
        }

        [SlashCommand("猜測英雄技能", "猜測英雄技能")]
        public async Task GuessCommand(
            [Summary("英雄名", "英雄名稱")] string champName,
            [Summary("技能位置", "P, Q, W, E, 或 R")][Choice("P", "p"), Choice("Q", "q"), Choice("W", "w"), Choice("E", "e"), Choice("R", "r")] string skillPos,
            [Summary("猜測名稱", "你猜測的技能名稱")] string userGuess)
        {
            await DeferAsync();
            var champService = new GetChampService();
            var user = Context.User as SocketGuildUser;
            await champService.GuessChampSkillAsync(Context.Channel as IMessageChannel, champName.ToLower(), skillPos.ToLower(), userGuess.ToLower(), user);
            await FollowupAsync("-", ephemeral: true);
        }
        #endregion

        #region 不外接api的簡單遊戲
        [SlashCommand("猜單字", "猜單字")]
        public async Task Guess(string word, [Summary("難度", "不選則隨便選一個")][Choice("1~5個字", "easy")][Choice("6~7個字", "normal")][Choice("8~9個字", "hard")][Choice("10個字以上", "發kinghard")] string diff = "")
        {
            try
            {
                var user = Context.User as SocketGuildUser;
                string res = await _wordService.Guess(Context.Channel as IMessageChannel, word, user,diff);
                if (!string.IsNullOrEmpty(res))
                {
                    await RespondAsync(res);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

        }

        [SlashCommand("開始踩地雷遊戲", "開始踩地雷遊戲")]
        public async Task MineCommand()
        {
            await DeferAsync();

            var (component, embed) = await _mineGameService.StartGameAsync(Context.User.Id, 5, 5);

            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("開始超大踩地雷遊戲", "開始超大踩地雷遊戲")]
        public async Task CustomizedMineCommand(
            [Summary("寬度", "地圖寬度")] int width,
            [Summary("高度", "地圖高度")] int height)
        {
            await DeferAsync();

            var (component, embed) = await _mineGameService.StartBiggerGameAsync(Context.User.Id, width, height);

            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("超大踩地雷遊戲開牌", "超大踩地雷遊戲開牌")]
        public async Task OpenBox(
            [Summary("x座標", "x座標")] int x,
            [Summary("y座標", "y座標")] int y)
        {
            await DeferAsync();

            var embed = await _mineGameService.HandleTextCoordinate(Context.User.Id, x, y);
            await FollowupAsync(embed: embed);
        }

        [SlashCommand("開始魔術方塊遊戲", "開始魔術方塊遊戲")]
        public async Task RubiksCubeCommand(
    [Summary("難度", "打亂步數 (預設20步)")] int scrambleMoves = 20)
        {
            await DeferAsync();

            if (scrambleMoves < 5 || scrambleMoves > 100)
            {
                await FollowupAsync("❌ 難度必須在 5-100 步之間！", ephemeral: true);
                return;
            }

            var (component, embed) = _rubiksCubeService.StartGame(Context.Channel.Id, scrambleMoves);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("開始魔術方塊遊戲短版", "開始魔術方塊遊戲 (簡短版)")]
        public async Task CubeCommand()
        {
            await DeferAsync();
            var (component, embed) = _rubiksCubeService.StartGame(Context.Channel.Id, 20);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("開始2048遊戲", "開始 2048 遊戲")]
        public async Task Game2048Command()
        {
            await DeferAsync();

            try
            {
                var channelId = Context.Channel.Id;

                var (component, embed) = await _game2048Service.StartGameAsync(channelId);

                await FollowupAsync(embed: embed, components: component?.Build());
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                {
                    Title = "❌ 錯誤",
                    Description = $"發生錯誤: {ex.Message}",
                    Color = Color.Red
                }.Build();
                await FollowupAsync(embed: errorEmbed, components: new ComponentBuilder().Build());
            }

        }

        [SlashCommand("開始單人抽鬼牌遊戲", "開始抽鬼牌遊戲(測試模式)")]
        public async Task GhostStartCommand()
        {
            await DeferAsync();

            var user = Context.User as SocketGuildUser;
            var result = await _oldMaidService.StartTestGame(Context.Channel, user);

            // 同時發送按鈕
            var component = _oldMaidService.GetDrawButtons(Context.Channel);

            await FollowupAsync(result, components: component?.Build());
        }

        [SlashCommand("開始多人抽鬼牌遊戲", "開始多人抽鬼牌遊戲")]
        public async Task GhostPlayCommand(
            [Summary("玩家2", "第二位玩家")] SocketGuildUser player2,
            [Summary("玩家3", "第三位玩家（選填）")] SocketGuildUser player3 = null,
            [Summary("玩家4", "第四位玩家（選填）")] SocketGuildUser player4 = null,
            [Summary("玩家5", "第五位玩家（選填）")] SocketGuildUser player5 = null,
            [Summary("玩家6", "第六位玩家（選填）")] SocketGuildUser player6 = null)
        {
            await DeferAsync();

            var user = Context.User as SocketGuildUser;
            var players = new List<SocketGuildUser> { user, player2 };

            if (player3 != null) players.Add(player3);
            if (player4 != null) players.Add(player4);
            if (player5 != null) players.Add(player5);
            if (player6 != null) players.Add(player6);

            var result = await _oldMaidService.StartGame(Context.Channel, players);
            var component = _oldMaidService.GetDrawButtons(Context.Channel);

            await FollowupAsync(result, components: component?.Build());
        }

        [SlashCommand("查看你的手牌", "查看你的手牌")]
        public async Task GhostHandsCommand()
        {
            var user = Context.User as SocketGuildUser;
            var embed = _oldMaidService.GetPlayerHand(Context.Channel, user);

            // ephemeral: true 表示只有執行指令的人看得到
            await RespondAsync(embed: embed, ephemeral: true);
        }

        [SlashCommand("查看抽鬼牌遊戲狀態", "查看抽鬼牌遊戲狀態")]
        public async Task GhostStatusCommand()
        {
            await DeferAsync();

            var status = _oldMaidService.GetStatus(Context.Channel);
            var component = _oldMaidService.GetDrawButtons(Context.Channel);

            await FollowupAsync(status, components: component?.Build());
        }

        [SlashCommand("重置抽鬼牌遊戲", "重置抽鬼牌遊戲")]
        public async Task GhostResetCommand()
        {
            await DeferAsync();

            var result = _oldMaidService.ResetGame(Context.Channel);

            await FollowupAsync(result, ephemeral: true);
        }

        [SlashCommand("1a2b遊戲", "1A2B遊戲")]
        public async Task SetGames1A2BAsync([Summary("你要設定的數字", "你要設定的數字(4位數) 不輸入則soyo隨機設一筆")] string number = "")
        {
            await DeferAsync();

            // 驗證輸入
            if (!string.IsNullOrEmpty(number))
            {
                if (number.Length != 4 || !number.All(char.IsDigit))
                {
                    await FollowupAsync("請輸入4位數字！", ephemeral: true);
                    return;
                }
            }

            var userId = Context.User.Id;
            var (component, embed) = await _game1A2BService.StartGameAsync(userId, number);

            var message = await FollowupAsync(embed: embed, components: component?.Build());

            // 儲存訊息ID到session中
            var session = _game1A2BService.GetSession(userId);
            if (session != null)
            {
                session.MessageId = message.Id;
            }
        }
        #endregion

        #region neuro功能相關
        [SlashCommand("透過elevenlabs說話", "透過ElevenLabs說話")]
        public async Task ElevenLabsTalk(
    [Summary("text", "要讓他說的話")] string text,
    [Summary("model", "選擇需要使用的模型")][Choice("品質最好", "eleven_v3"), Choice("最穩定", "eleven_multilingual_v2"), Choice("最低延遲", "eleven_flash_v2_5"), Choice("平衡", "eleven_turbo_v2_5")] string model,
    [Summary("voiceID", "請輸入要使用的voiceID，不填入則預設")] string voiceID = "pNInz6obpgDQGcFmaJgB")
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            var voiceChannel = user.VoiceChannel;
            await _elevenLabsService.SpeakAsync(voiceChannel, text, model, voiceID);
            await FollowupAsync("已接收", ephemeral: true);
        }

        [SlashCommand("聊天測試中", "聊天(測試中)")]
        public async Task Talk(
            [Summary("text", "要讓他說的話")] string text,
            [Summary("speaker", "選擇要讓誰說")][Choice("soyo", "soyo"), Choice("tomori", "tomori"), Choice("anon", "anon")] string speaker,
            [Summary("tts-model", "使用的tts模型")][Choice("tw成熟女聲", "zh-TW-HsiaoChenNeural"), Choice("tw活潑女聲", "zh-TW-HsiaoYuNeura"), Choice("tw男聲", "zh-TW-YunJheNeural"),
            Choice("cn-AI助理風", "zh-CN-XiaoxiaoNeural"), Choice("cn-廣播風", "zh-CN-YunxiNeural"), Choice("cn-男聲", "zh-CN-XiaoyiNeural")] string tts_model,
            [Summary("pitch_shift", "音高 (0.5-2之間)")] double pitch = 0
            )
        {
            var user = Context.User as SocketGuildUser;

            //先用google ai studio取得回復
            //string result = await _googleAIStudioService.GenerateTextAsync(text, user, true);
            //再用elevenlabs說出來 (免費仔哭哭)
            //var user = Context.User as SocketGuildUser;
            //var voiceChannel = user.VoiceChannel;
            //await _elevenLabsService.SpeakAsync(voiceChannel, text, "eleven_v3", "pNInz6obpgDQGcFmaJgB");
            using var httpClient = new HttpClient();

            await _rVC_Service.SendTextToSpeach(
                Context.Channel as ITextChannel,
                text,
                speaker,
                tts_model,
                pitch
            );
        }

        [SlashCommand("上傳音檔來換聲音", "上傳音檔，選擇聲音模型與參數以改變聲音")]
        public async Task ChangeVoice(
            [Summary("file", "要上傳的音樂檔案 (mp3, wav)")] IAttachment file,
            [Summary("speaker", "選擇要讓誰說")][Choice("soyo", "soyo"), Choice("tomori", "tomori"), Choice("anon", "anon")] string speaker,
            [Summary("pitch_shift", "音高 (0.5-2之間)")] double pitch = 0,
            [Summary("index_rate", "音色相似度 (0-1之間)")] double indexRate = 0.75,
            [Summary("protect", "原聲保護度 (0-1之間)")] double protect = 0.33
        )
        {
            using var httpClient = new HttpClient();
            using var stream = await httpClient.GetStreamAsync(file.Url);

            await _rVC_Service.SendConvertedAudioToChannelAsync(
                Context.Channel as ITextChannel,
                stream,
                file.Filename,
                speaker,
                pitch,
                indexRate,
                protect
            );
        }

        [SlashCommand("soyo記憶消除", "清除 Soyo 的記憶（包含對話摘要）")]
        public async Task ClearSoyoMemory(
    [Summary("頻道", "要清除記憶的頻道（留空 = 全部）")] string channelKey = null
)
        {
            await DeferAsync();

            await _googleAIStudioService.ClearMemoryAsync(channelKey);
            await _openRouterService.ClearMemoryAsync(channelKey);

            await FollowupAsync($"已清除 Soyo 的記憶與對話摘要 ({channelKey ?? "全部頻道"})");
        }

        [SlashCommand("soyo對話摘要", "查看目前整理出來的對話摘要")]
        public async Task GetSoyoSummary()
        {
            await DeferAsync();

            string channelKey = Context.Guild?.Id.ToString() ?? "global";
            var summary = _openRouterService.GetChannelSummary(channelKey);

            if (string.IsNullOrEmpty(summary))
            {
                await FollowupAsync($"頻道 `{channelKey}` 目前沒有對話摘要（對話量還不夠多，或尚未觸發摘要整理）");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("📝 Soyo 的對話摘要")
                .WithDescription(summary)
                .WithColor(Color.Purple)
                .WithFooter($"頻道 key: {channelKey}")
                .WithCurrentTimestamp()
                .Build();

            await FollowupAsync(embed: embed);
        }
        #endregion

        #region 設定相關
        [SlashCommand("設置文字", "設置文字")]
        public async Task SetTextCommand(
    [Summary("if", "如果有這個文字")] string key,
    [Summary("then", "會跳出下面這段，如果不填就是刪除")] string value = ""
    )
        {
            await DeferAsync();

            _setTextService.Set(key, value);

            await FollowupAsync("設置完成", ephemeral: true);
        }

        [SlashCommand("檢查所有的設置文字", "檢查所有的設置文字")]
        public async Task SetTextCheckCommand()
        {
            await DeferAsync();

            var result = await _setTextService.GetAll();

            string formattedResult = string.Join("\n", result.Select(kv => $"**{kv.Key}**: {kv.Value}"));

            await FollowupAsync(formattedResult, ephemeral: true);
        }

        [SlashCommand("上傳文字for馬又only", "上傳文字(for 豬頭馬又only)")]
        public async Task WordsUploadCommand(
    [Summary("file", "要上傳的文字檔案 (txt)")] IAttachment file
    )
        {
            await DeferAsync();
            var result = await _wordService.SetWord(file);
            if (!result)
            {
                await FollowupAsync("上傳失敗，請確保檔案格式正確且內容不為空", ephemeral: true);
                return;
            }
            await FollowupAsync("上傳成功", ephemeral: true);
        }
        #endregion

        #region 互動用
        [SlashCommand("送光", "送光")]
        public async Task SendLightAsync(
    [Summary("你的代名", "你想用的名字")] string sender,
    [Summary("想送的對象", "請選擇對象")] IUser target,
    [Summary("自訂訊息", "你想要附加的訊息，選填，如果要的話，幫我以/me代表自己，/target代表你要發送的對象")] string message = ""
)
        {
            //var channel = Context.Client.GetChannel(592716175461580800) as ISocketMessageChannel;
            var channel = Context.Channel as IMessageChannel;
            if (string.IsNullOrEmpty(message))
            {
                await channel.SendMessageAsync($"{sender} 送光給 {target.Mention} ", allowedMentions: AllowedMentions.All);
            }
            else
            {
                message = message.Replace("/me", sender).Replace("/target", target.Mention);
                await channel.SendMessageAsync(message, allowedMentions: AllowedMentions.All);
            }
            await RespondAsync("發送成功", ephemeral: true);
        }

        [SlashCommand("開啟投票", "開啟投票")]
        public async Task VoteAsync(
    [Summary("標題", "標題")] string title,
    [Summary("投票選項", "選項，以,區隔，ex:1,2,3...")] string item,
    [Summary("role", "要@的群組")] IRole? role = null
)
        {
            await DeferAsync();
            string emoteString = CommonHelper.AddEmoji(item);
            string mention = role != null ? $"{role.Mention}\n" : "";

            string result = $"{mention}**{title}**\n\n{emoteString}";
            await FollowupAsync(result);

            var message = await GetOriginalResponseAsync();

            await CommonHelper.AddEmojiToMessageAsync(message, item.Split(',').Length);
        }
        #endregion

        #region 殘酷二選一
        [SlashCommand("輸入殘酷二選一id開啟遊戲", "輸入殘酷二選一ID開啟遊戲")]
        public async Task Pick2TitleAsync(
    [Summary("遊戲id", "要開啟的遊戲ID")] string gameID,
    [Summary("選擇總量", "要選擇的項目總量")] int count)
        {
            await DeferAsync();

            var channelId = Context.Channel.Id;

            try
            {
                var (imageMessage, component, embed) = await _pick2Service.StartGameAsync(channelId, gameID, count);

                // 先發送圖片訊息
                var imageMsg = await Context.Channel.SendMessageAsync(imageMessage);

                // 再發送投票訊息
                var voteMsg = await FollowupAsync(embed: embed, components: component?.Build());

                // 儲存訊息 ID
                _pick2Service.SetMessageIds(channelId, imageMsg.Id, voteMsg.Id);
            }
            catch (Exception ex)
            {
                await FollowupAsync($"啟動遊戲時發生錯誤: {ex.Message}", ephemeral: true);
            }
        }
        #endregion

        #region 動漫相關 > 等待jikan能被打再開啟
        [SlashCommand("猜動漫角色", "猜動漫角色")]
        public async Task GuessAnimeCharaAsync(
    [Summary("模式", "模式")][Choice("角色猜角色", "ctc"), Choice("角色猜動畫", "cta")] string mode,
    [Summary("是否查詢熱門", "是否查詢熱門")] bool isTop
)
        {
            await DeferAsync();

            var result = await _animeService.StartGameAsync(mode, isTop);

            await FollowupAsync(embed: result.embed, components: result.component?.Build());
        }

        [SlashCommand("隨機抽取一部幸運動畫", "隨機抽取一部幸運動畫")]
        public async Task GetSomeRandomAnime(
            [Summary("種類", "種類")][Choice("TV", "TV"), Choice("OVA", "OVA"), Choice("Movie", "Movie"), Choice("Special", "Special"), Choice("ONA", "ONA"), Choice("Music", "Music"), Choice("CM", "CM"), Choice("PV", "PV"), Choice("TV Special", "TV Special")] string type = "",
            [Summary("分級", "分級")][Choice("G", "G"), Choice("pg", "pg"), Choice("pg13", "pg13"), Choice("r17", "r17"), Choice("r", "r"), Choice("rx", "rx")] string ratings = ""
        )
        {
            await DeferAsync();

            var result = await _animeService.GetSomeRandomAnime(type, ratings);

            await FollowupAsync(embed: result.Item1.embed, components: result.Item1.component?.Build());

            if (!string.IsNullOrEmpty(result.imageUrl))
            {
                using var http = new HttpClient();
                var imageBytes = await http.GetByteArrayAsync(result.imageUrl);
                var stream = new MemoryStream(imageBytes);
                var attachment = new FileAttachment(stream, "SPOILER_anime.jpg");

                await Context.Channel.SendFileAsync(attachment);
            }
        }

        [SlashCommand("隨機抽取一部幸運書籍", "隨機抽取一部幸運書籍")]
        public async Task GetSomeRandomManga(
            [Summary("種類", "種類")][Choice("manga", "manga"), Choice("novel", "novel"), Choice("lightnovel", "lightnovel"), Choice("oneshot", "oneshot"), Choice("doujin", "doujin"), Choice("manhwa", "manhwa"), Choice("manhua", "manhua")] string type = "",
            [Summary("標籤", "標籤")][Choice("Hentai", "12"), Choice("Horror", "14"), Choice("Ecchi", "9"), Choice("Adventure", "2"), Choice("Boys Love", "28"), Choice("Comedy", "4")] string genres = ""
        )
        {
            await DeferAsync();

            var result = await _animeService.GetSomeRandomManga(type, genres);

            await FollowupAsync(embed: result.Item1.embed, components: result.Item1.component?.Build());

            if (!string.IsNullOrEmpty(result.imageUrl))
            {
                using var http = new HttpClient();
                var imageBytes = await http.GetByteArrayAsync(result.imageUrl);
                var stream = new MemoryStream(imageBytes);
                var attachment = new FileAttachment(stream, "SPOILER_manga.jpg");

                await Context.Channel.SendFileAsync(attachment);
            }
        }
        #endregion

        #region pokemon相關
        [SlashCommand("猜pokemon", "猜pokemon")]
        public async Task GuessPokemonAsync(
[Summary("模式", "模式")][Choice("猜pokemon名稱", "name"), Choice("猜pokemon技能", "move"), Choice("我是誰", "who")] string mode)
        {
            await DeferAsync();
            var ((component, embed), silhouette) = await _pokeService.StartPokeGameAsync(mode);

            if (silhouette != null)
                await FollowupWithFileAsync(silhouette, "mystery.png", embed: embed, components: component.Build());
            else
                await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("抓pokemon", "每天抓一隻隨機pokemon")]
        public async Task CatchPokemonAsync()
        {
            await DeferAsync();
            var (embed, component) = await _pokeGameService.CatchPokemonAsync(Context.User.Id, Context.User.Username);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("我的pokemon", "查看你的pokemon列表")]
        public async Task MyPokemonAsync([Summary("秀給大家看", "想秀給大家看的pokemon編號")] int index = 0)
        {
            if (index == 0)
            {
                await DeferAsync(ephemeral: true);
                var (embed, component) = await _pokeGameService.ListPokemonAsync(Context.User.Id, Context.User.Username);
                await FollowupAsync(embed: embed, components: component.Build(), ephemeral: true);
            }
            else
            {
                await DeferAsync();
                var channel = Context.Channel as IMessageChannel;
                var (embed, component) = await _pokeGameService.ShowOnePokemon(Context.User.Id, Context.User.Username, index, channel);
                await FollowupAsync(embed: embed, components: component.Build());
            }
        }

        [SlashCommand("自定義pokemon", "自定義你的pokemon名稱")]
        public async Task CustomizePokemonAsync(
            [Summary("編號", "pokemon的編號（從1開始）")] int index,
            [Summary("自訂名稱", "新的名稱")] string customName)
        {
            await DeferAsync();
            var (embed, component) = await _pokeGameService.CustomizePokemonAsync(Context.User.Id, Context.User.Username, index, customName);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("pokemon對戰", "尋找對手進行pokemon對戰")]
        public async Task BattlePokemonAsync()
        {
            await DeferAsync(ephemeral: true);
            var (embed, component) = await _pokeGameService.ShowBattleSelectMenuAsync(Context.User.Id, Context.User.Username);
            await FollowupAsync(embed: embed, components: component.Build(), ephemeral: true);
        }

        [SlashCommand("pokemon對戰2v2", "尋找對手進行pokemon2v2對戰")]
        public async Task BattlePokemon2x2Async()
        {
            await DeferAsync(ephemeral: true);
            var (embed, component) = await _pokeGameService.Show2v2Step1MenuAsync(Context.User.Id, Context.User.Username);
            await FollowupAsync(embed: embed, components: component.Build(), ephemeral: true);
        }

        [SlashCommand("測試對戰", "生成假對手進行測試對戰")]
        public async Task TestBattlePokemonAsync()
        {
            await DeferAsync(ephemeral: true);
            var (embed, component) = await _pokeGameService.ShowBattleSelectMenuAsync(Context.User.Id, Context.User.Username);
            await FollowupAsync(embed: embed, components: component.Build(), ephemeral: true);
        }

        [SlashCommand("蛋雕一隻pokemon", "蛋雕一隻pokemon")]
        public async Task ReleasePokemonAsync()
        {
            await DeferAsync(ephemeral: true);
            var (embed, component) = await _pokeGameService.ShowReleasePokemonMenuAsync(Context.User.Id, Context.User.Username);
            await FollowupAsync(embed: embed, components: component.Build(), ephemeral: true);
        }

        [SlashCommand("交換一隻pokemon", "交換一隻pokemon(兩方都使用此指令才會成功交換)")]
        public async Task ExchangePokemonAsync(
            [Summary("編號", "要交換的pokemon編號（從1開始）")] int index,
            [Summary("要交換的人", "要交換的人")] IUser target)
        {
            await DeferAsync();
            var (embed, component) = await _pokeGameService.InitiateExchangeAsync(
                Context.User.Id,
                Context.User.Username,
                index,
                target,
                Context.Channel);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("開始傳說pokemon團戰", "當前所有參與團戰的人來開始對戰")]
        public async Task StartPokemonTeamFightAsync()
        {
            await DeferAsync();
            var channel = Context.Channel;
            var (embed, component) = await _pokeGameService.StartTeamFightBattleAsync(channel);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("參與或開啟pokemon團戰", "參與已存在尚未開始的團戰，如果當前沒有則開啟新的一團")]
        public async Task JoinPokemonTeamFightAsync(
            [Summary("編號", "要出戰的pokemon編號（從1開始）")] int index)
        {
            await DeferAsync();

            var channel = Context.Channel;
            var (embed, component) = await _pokeGameService.JoinOrCreateTeamFightAsync(Context.User.Id, Context.User.Username, index - 1, channel.Id);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("pokemon爬塔", "用你的Pokemon挑戰爬塔")]
        public async Task PokeTowerAsync()
        {
            await DeferAsync();
            var player = await _pokeGameService.GetPlayerAsync(Context.User.Id, Context.User.Username);
            var pokemons = player?.CaughtPokemon ?? new();
            var (embed, component) = _pokeTowerService.ShowPokemonSelection(
                Context.Channel.Id,
                Context.User.Id,
                Context.User.GlobalName ?? Context.User.Username,
                pokemons);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("爬塔狀態", "把目前爬塔的操作介面重新發到最下方（找不到按鈕時使用）")]
        public async Task PokeTowerRefreshAsync()
        {
            await DeferAsync();
            var run = _pokeTowerService.GetRun(Context.Channel.Id);
            if (run == null)
            {
                await FollowupAsync(embed: new EmbedBuilder()
                    .WithTitle("❓ 此頻道目前沒有爬塔")
                    .WithColor(Color.Orange).Build(), ephemeral: true);
                return;
            }
            var (embed, component) = _pokeTowerService.BuildCurrentStateEmbed(Context.Channel.Id);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("取消pokemon爬塔", "取消此頻道進行中的爬塔（本人才能使用）")]
        public async Task CancelPokeTowerAsync()
        {
            await DeferAsync();
            var run = _pokeTowerService.GetRun(Context.Channel.Id);
            if (run == null)
            {
                await FollowupAsync(embed: new EmbedBuilder()
                    .WithTitle("❓ 此頻道目前沒有爬塔")
                    .WithColor(Color.Orange).Build());
                return;
            }

            // 只允許本人或開發者（豬頭馬又）取消
            bool isOwner = run.PlayerId == Context.User.Id;
            bool isDev = Context.User.Username.Contains("zu_tomayo") || Context.User.Username.Contains("豬頭馬又");
            if (!isOwner && !isDev)
            {
                await FollowupAsync(embed: new EmbedBuilder()
                    .WithTitle("❌ 只有爬塔的本人或開發者才能取消")
                    .WithDescription($"目前爬塔的是 **{run.PlayerName}**。")
                    .WithColor(Color.Red).Build(), ephemeral: true);
                return;
            }

            await _pokeTowerService.CancelRunAsync(Context.Channel.Id);
            await FollowupAsync(embed: new EmbedBuilder()
                .WithTitle("🚫 爬塔已取消")
                .WithDescription($"**{run.PlayerName}** 的爬塔（第 {run.CurrentFloor} 層）已被取消。")
                .WithColor(Color.DarkOrange).Build());
        }
        #endregion

        #region Valorant相關
        [SlashCommand("猜猜我是誰瓦學弟ver", "根據角色圖片輪廓猜Valorant角色")]
        public async Task GuessValorantAgentAsync()
        {
            await DeferAsync();
            var ((component, embed), silhouette) = await _valorantService.StartGuessAgentImageAsync(Context.Channel.Id);

            if (silhouette != null)
            {
                var message = await FollowupWithFileAsync(silhouette, "silhouette.png", embed: embed, components: component.Build());
                _valorantService.SetMessageId(Context.Channel.Id, message.Id, true);
            }
            else
            {
                await FollowupAsync(embed: embed, components: component.Build());
            }
        }

        [SlashCommand("猜猜這哪招瓦學弟ver", "根據技能圖示和描述猜Valorant技能名稱")]
        public async Task GuessValorantAbilityAsync()
        {
            await DeferAsync();
            var (component, embed) = await _valorantService.StartGuessAbilityNameAsync(Context.Channel.Id);
            var message = await FollowupAsync(embed: embed, components: component.Build());
            _valorantService.SetMessageId(Context.Channel.Id, message.Id, false);
        }

        [SlashCommand("回答valorant角色", "回答猜角色遊戲")]
        public async Task AnswerValorantAgentAsync([Summary("答案", "輸入角色名稱（中文或英文都可以）")] string answer)
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            var (isCorrect, embed, correctName) = await _valorantService.HandleAgentAnswerAsync(Context.Channel.Id, answer, user, Context.Channel);

            if (isCorrect)
            {
                // 答對了 → 公開發送訊息
                await FollowupAsync(embed: embed);
            }
            else
            {
                // 答錯了 → 公開發送錯誤訊息
                await FollowupAsync(embed: embed);
            }
        }

        [SlashCommand("回答valorant技能", "回答猜技能遊戲")]
        public async Task AnswerValorantAbilityAsync([Summary("答案", "輸入技能名稱（中文或英文都可以）")] string answer)
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            var (isCorrect, wrongAnswer, correctAnswer, agentName) = await _valorantService.HandleAbilityAnswerAsync(Context.Channel.Id, answer);

            if (isCorrect)
            {
                // 答對了 → 公開發送訊息
                var rewardText = await RewardsHelpers.GetRandomRewards(Context.Channel, user);

                var successEmbed = new EmbedBuilder()
                    .WithTitle("🎉 答對了！")
                    .WithColor(Color.Green)
                    .WithDescription($"**{user.Mention}**{CommonHelper.GetUserFace(user.Id.ToString())} 答對了！")
                    .AddField("正確答案", correctAnswer, inline: true)
                    .AddField("所屬角色", agentName, inline: true)
                    .AddField("獎勵", rewardText)
                    .WithTimestamp(DateTimeOffset.Now)
                    .Build();

                await FollowupAsync(embed: successEmbed);
            }
            else if (wrongAnswer != null)
            {
                // 答錯了 → 公開發送錯誤訊息
                await FollowupAsync($"❌ **{user.Mention}** {CommonHelper.GetUserFace(user.Id.ToString())} 猜錯了！答案：**{wrongAnswer}**");
            }
            else
            {
                // 找不到遊戲
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("⚠️ 提示")
                    .WithColor(Color.Orange)
                    .WithDescription("找不到遊戲，請先使用 `/猜猜這哪招瓦學弟ver` 開始遊戲！")
                    .Build();
                await FollowupAsync(embed: errorEmbed, ephemeral: true);
            }
        }

        [SlashCommand("隨機抽一把幸運造型", "隨機抽一把幸運造型")]
        public async Task RandomDrawWeaponSkinAsync([Summary("武器名稱", "武器名稱")] string name = "")
        {
            await DeferAsync();
            var (weaponName, skinName, skinImageUrl) = await _valorantService.RandomWeaponSkin(name);

            var channel = Context.Channel as IMessageChannel;
            await channel.SendMessageAsync($"🎁 {Context.User.Mention} {CommonHelper.GetUserFace(Context.User.Id.ToString())} 屌抽一把\n**{weaponName}** - **{skinName}**");
            await channel.SendMessageAsync(skinImageUrl);

            await FollowupAsync();
        }
        #endregion

        #region TRPG 黑暗奇幻冒險
        [SlashCommand("開始冒險", "開始一個黑暗奇幻 TRPG 冒險（此頻道所有訊息將成為遊戲內容）")]
        public async Task StartAdventureAsync([Summary("職業", "選擇職業：戰士、盜賊、法師、牧師、遊俠")]
        [Choice("戰士", "1")]
        [Choice("盜賊", "2")]
        [Choice("法師", "3")]
        [Choice("牧師", "4")]
        [Choice("遊俠", "5")]
        string classChoice)
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            if (user == null)
            {
                await FollowupAsync("❌ 無法取得使用者資訊", ephemeral: true);
                return;
            }

            var result = await _trpgService.StartAdventureAsync(Context.Channel.Id, user, classChoice);
            await FollowupAsync(result);
        }

        [SlashCommand("加入冒險", "加入當前頻道進行中的 TRPG 冒險")]
        public async Task JoinAdventureAsync([Summary("職業", "選擇職業：戰士、盜賊、法師、牧師、遊俠")]
        [Choice("戰士", "1")]
        [Choice("盜賊", "2")]
        [Choice("法師", "3")]
        [Choice("牧師", "4")]
        [Choice("遊俠", "5")]
        string classChoice)
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            if (user == null)
            {
                await FollowupAsync("❌ 無法取得使用者資訊", ephemeral: true);
                return;
            }

            var result = await _trpgService.JoinAdventureAsync(Context.Channel.Id, user, classChoice);
            await FollowupAsync(result);
        }

        [SlashCommand("投骰", "投擲 20 面骰來判定行動結果")]
        public async Task RollDiceAsync()
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            if (user == null)
            {
                await FollowupAsync("❌ 無法取得使用者資訊", ephemeral: true);
                return;
            }

            var (rollText, rollImagePrompt) = await _trpgService.RollDiceAsync(Context.Channel.Id, user);
            await FollowupAsync(rollText);

            // 異步生成場景圖片
            if (!string.IsNullOrWhiteSpace(rollImagePrompt))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var stream = await _aiImageService.GenerateImageAsync(rollImagePrompt);
                        if (stream != null)
                            await Context.Channel.SendFileAsync(stream, "scene.png", $"🖼️ *{rollImagePrompt}*");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TRPG Image] 擲骰場景圖片生成失敗: {ex.Message}");
                    }
                });
            }
        }

        [SlashCommand("結束冒險", "結束當前頻道的 TRPG 冒險")]
        public async Task EndAdventureAsync()
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            if (user == null)
            {
                await FollowupAsync("❌ 無法取得使用者資訊", ephemeral: true);
                return;
            }

            var result = await _trpgService.EndAdventureAsync(Context.Channel.Id, user);
            await FollowupAsync(result);
        }

        [SlashCommand("冒險狀態", "查看當前冒險的狀態")]
        public async Task AdventureStatusAsync()
        {
            await DeferAsync();
            var result = await _trpgService.GetAdventureStatusAsync(Context.Channel.Id);
            await FollowupAsync(result, ephemeral: true);
        }

        [SlashCommand("查看背包", "查看你的背包物品")]
        public async Task ViewInventoryAsync()
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            if (user == null)
            {
                await FollowupAsync("❌ 無法取得使用者資訊", ephemeral: true);
                return;
            }

            var result = await _trpgService.GetInventoryAsync(Context.Channel.Id, user);
            await FollowupAsync(result, ephemeral: true);
        }
        #endregion

        #region 歌詞相關
        [SlashCommand("查歌詞", "根據歌名和歌手查詢歌詞")]
        public async Task SearchLyricsAsync(
            [Summary("歌名", "歌曲名稱")] string trackName,
            [Summary("歌手", "歌手名稱(選填)")] string artistName = null)
        {
            await DeferAsync();

            try
            {
                var results = await _lyrisService.SearchLyricsAsync(trackName, artistName);

                if (results == null || results.Count == 0)
                {
                    await FollowupAsync($"❌ 找不到歌曲: {trackName}" + (artistName != null ? $" - {artistName}" : ""));
                    return;
                }

                var firstResult = results[0];
                var embed = new EmbedBuilder()
                    .WithTitle($"🎵 {firstResult.trackName}")
                    .WithDescription(_lyrisService.FormatLyrics(firstResult.plainLyrics, 4000))
                    .WithColor(Color.Blue)
                    .AddField("歌手", firstResult.artistName ?? "未知", true)
                    .AddField("專輯", firstResult.albumName ?? "未知", true)
                    .AddField("長度", $"{(int)(firstResult.duration / 60)}:{(int)(firstResult.duration % 60):D2}", true)
                    .WithFooter($"歌詞 ID: {firstResult.id}");

                if (firstResult.instrumental)
                {
                    embed.AddField("⚠️", "此曲為純音樂，無歌詞");
                }

                await FollowupAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                await FollowupAsync($"❌ 發生錯誤: {ex.Message}");
            }
        }

        [SlashCommand("查歌手歌曲", "根據歌手查詢所有歌曲")]
        public async Task SearchArtistSongsAsync(
            [Summary("歌手", "歌手名稱")] string artistName)
        {
            await DeferAsync();

            try
            {
                var results = await _lyrisService.SearchLyricsAsync("", artistName);

                if (results == null || results.Count == 0)
                {
                    await FollowupAsync($"❌ 找不到歌手: {artistName} 的歌曲");
                    return;
                }

                var songList = string.Join("\n", results.Take(15).Select((r, i) =>
                    $"{i + 1}. **{r.trackName}** - {r.artistName}" +
                    (r.albumName != null ? $" ({r.albumName})" : "")));

                var embed = new EmbedBuilder()
                    .WithTitle($"🎤 {artistName} 的歌曲")
                    .WithDescription(songList)
                    .WithColor(Color.Purple)
                    .WithFooter($"共找到 {results.Count} 首歌曲，顯示前 15 首");

                await FollowupAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                await FollowupAsync($"❌ 發生錯誤: {ex.Message}");
            }
        }

        [SlashCommand("顯示同步歌詞", "顯示帶時間戳的歌詞")]
        public async Task ShowSyncedLyricsAsync(
            [Summary("歌名", "歌曲名稱")] string trackName,
            [Summary("歌手", "歌手名稱")] string artistName = "")
        {
            await DeferAsync();

            try
            {
                var result = await _lyrisService.GetLyricsAsync(trackName, artistName);

                if (result == null)
                {
                    await FollowupAsync($"❌ 找不到歌曲: {trackName} - {artistName}");
                    return;
                }

                if (string.IsNullOrWhiteSpace(result.syncedLyrics))
                {
                    await FollowupAsync($"❌ 此歌曲沒有同步歌詞，請使用 /查歌詞 查看一般歌詞");
                    return;
                }

                var syncedLines = _lyrisService.ParseSyncedLyrics(result.syncedLyrics);
                var formattedLyrics = string.Join("\n", syncedLines.Take(50).Select(l =>
                    $"`[{l.timestamp:mm\\:ss}]` {l.line}"));

                var embed = new EmbedBuilder()
                    .WithTitle($"🎵 {result.trackName} - {result.artistName}")
                    .WithDescription(_lyrisService.FormatLyrics(formattedLyrics, 4000))
                    .WithColor(Color.Green)
                    .WithFooter($"同步歌詞 | 共 {syncedLines.Count} 行");

                await FollowupAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                await FollowupAsync($"❌ 發生錯誤: {ex.Message}");
            }
        }

        //[SlashCommand("測試播放同步歌詞", "播放同步歌詞(手動控制模式，使用按鈕切換前後句)")]
        //public async Task TestPlaySyncedLyricsAsync(
        //    [Summary("歌名", "歌曲名稱")] string trackName,
        //    [Summary("歌手", "歌手名稱")] string artistName = "")
        //{
        //    await DeferAsync();

        //    try
        //    {
        //        var success = await _lyricsDisplayService.StartLyricsDisplayAsync(
        //            Context.Channel.Id,
        //            trackName,
        //            artistName,
        //            Context.Channel);

        //        if (success)
        //        {
        //            await FollowupAsync($"✅ 已開始播放 **{trackName}** 的同步歌詞（使用按鈕控制前後句）", ephemeral: true);
        //        }
        //        else
        //        {
        //            await FollowupAsync($"❌ 無法開始播放歌詞", ephemeral: true);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        await FollowupAsync($"❌ 發生錯誤: {ex.Message}", ephemeral: true);
        //    }
        //}

        //[SlashCommand("停止歌詞顯示", "停止當前頻道的歌詞顯示")]
        //public async Task StopLyricsDisplayAsync()
        //{
        //    _lyricsDisplayService.StopLyricsDisplay(Context.Channel.Id);
        //    await RespondAsync("✅ 已停止歌詞顯示", ephemeral: true);
        //}
        #endregion

        #region 無用api
        [SlashCommand("無用小功能", "各種無用小功能")]
        public async Task UselessApiAsync(
            [Summary("功能", "功能_不輸入則隨機")]
        [Choice("隨機查克莫里士史詩", 1)]
        [Choice("隨機貓咪冷知識", 2)]
        [Choice("隨機狗勾", 3)]
        [Choice("隨機中文名言", 4)]
        [Choice("隨機鴨子", 5)]
        [Choice("隨機狐狸", 6)]
        [Choice("隨機冷知識", 7)]
        [Choice("隨機動畫句子", 8)]
        [Choice("隨機遊戲句子", 9)]
        int type = 0)
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            string res = await _uselessApiService.GetUselessApiAsync(type);
            await FollowupAsync(res);
        }

        [SlashCommand("今晚你想來點", "幫你屌決定一波今天吃甚麼")]
        public async Task GetFoodAsync(
    [Summary("類型", "類型")]
    [Choice("台式", 1)]
    [Choice("中式", 2)]
    [Choice("日式", 3)]
    [Choice("韓式", 4)]
    [Choice("西式", 5)]
    [Choice("港式", 6)]
    [Choice("東南亞", 7)]
    [Choice("鍋物", 8)]
    [Choice("甜點", 9)]
    [Choice("神秘食物", 10)]
        int type = 0)
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            string res = await _uselessApiService.GetRandomFoodApIAsync(user, type);
            await FollowupAsync(res);
        }
        #endregion

        #region NekoBot 功能 被雲端封鎖
        //[SlashCommand("nekobot-ship", "產生兩個使用者的 Ship 配對圖片")]
        //public async Task NekoBotShipAsync(
        //    [Summary("使用者1", "第一個使用者")] IUser user1,
        //    [Summary("使用者2", "第二個使用者")] IUser user2)
        //{
        //    await DeferAsync();

        //    string? user1Url = user1.GetAvatarUrl() ?? user1.GetDefaultAvatarUrl();
        //    string? user2Url = user2.GetAvatarUrl() ?? user2.GetDefaultAvatarUrl();

        //    var embed = await _nekoBotService.GetShipImageAsync(user1Url, user2Url);
        //    await FollowupAsync(embed: embed);
        //}

        //[SlashCommand("nekobot-whowouldwin", "產生兩個使用者的對決圖片")]
        //public async Task NekoBotWhoWouldWinAsync(
        //    [Summary("使用者1", "第一個使用者")] IUser user1,
        //    [Summary("使用者2", "第二個使用者")] IUser user2)
        //{
        //    await DeferAsync();

        //    string? user1Url = user1.GetAvatarUrl() ?? user1.GetDefaultAvatarUrl();
        //    string? user2Url = user2.GetAvatarUrl() ?? user2.GetDefaultAvatarUrl();

        //    var embed = await _nekoBotService.GetWhoWouldWinImageAsync(
        //        user1Url,
        //        user2Url
        //    );

        //    await FollowupAsync(embed: embed);
        //}

        //[SlashCommand("nekobot-圖片", "取得 NekoBot 的各種圖片")]
        //public async Task NekoBotImageAsync(
        //    [Summary("類型", "圖片類型")]
        //    [Choice("Neko (貓娘)", "neko")]
        //    [Choice("Kitsune (狐娘)", "kitsune")]
        //    [Choice("Waifu (老婆)", "waifu")]
        //    [Choice("Husbando (老公)", "husbando")]
        //    [Choice("遊戲角色", "gecg")]
        //    [Choice("頭像", "avatar")]
        //    [Choice("桌布", "wallpaper")]
        //    [Choice("狐娘 2", "foxgirl")]
        //    [Choice("蜥蜴", "lizard")]
        //    [Choice("鵝", "goose")]
        //    [Choice("咖啡", "coffee")]
        //    [Choice("食物", "food")]
        //    string type)
        //{
        //    await DeferAsync();
        //    var embed = await _nekoBotService.GetImageAsync(type);
        //    await FollowupAsync(embed: embed);
        //}

        //[SlashCommand("nekobot-nsfw", "取得 NekoBot 的 NSFW 圖片 (18+)")]
        //[RequireNsfw]
        //public async Task NekoBotNsfwImageAsync(
        //    [Summary("類型", "NSFW 圖片類型")]
        //    [Choice("NSFW - Ass", "hass")]
        //    [Choice("NSFW - Midriff", "hmidriff")]
        //    [Choice("NSFW - GIF", "pgif")]
        //    [Choice("NSFW - 4K", "4k")]
        //    [Choice("NSFW - Hentai", "hentai")]
        //    [Choice("NSFW - Holo", "holo")]
        //    [Choice("NSFW - Kitsune", "hkitsune")]
        //    [Choice("NSFW - Kemonomimi", "kemonomimi")]
        //    [Choice("NSFW - Anal", "hanal")]
        //    [Choice("NSFW - Gonewild", "gonewild")]
        //    [Choice("NSFW - Kanna", "kanna")]
        //    [Choice("NSFW - Ass 2", "ass")]
        //    [Choice("NSFW - Pussy", "pussy")]
        //    [Choice("NSFW - Thigh", "thigh")]
        //    [Choice("NSFW - Thigh 2", "hthigh")]
        //    [Choice("NSFW - Paizuri", "paizuri")]
        //    [Choice("NSFW - Tentacle", "tentacle")]
        //    [Choice("NSFW - Boobs", "boobs")]
        //    [Choice("NSFW - Boobs 2", "hboobs")]
        //    string type)
        //{
        //    await DeferAsync();
        //    var embed = await _nekoBotService.GetImageAsync(type);
        //    await FollowupAsync(embed: embed);
        //}
        #endregion

        #region Waifu.im 功能
        //[SlashCommand("waifu-標籤列表", "顯示所有可用的 Waifu.im 標籤")]
        //public async Task WaifuTagsAsync()
        //{
        //    await DeferAsync();
        //    var embed = await _waifuImService.GetAllTagsAsync();
        //    await FollowupAsync(embed: embed);
        //}

        //[SlashCommand("waifu-自訂", "使用自訂標籤取得 Waifu 圖片")]
        //public async Task WaifuCustomAsync(
        //    [Summary("標籤", "標籤名稱（可用逗號分隔多個標籤，例如：waifu,maid）")] string tags,
        //    [Summary("動畫", "是否只要 GIF 動畫")] bool isAnimated = false)
        //{
        //    await DeferAsync();
        //    var imageUrl = await _waifuImService.GetImageByTagAsync(tags, isNsfw: false, isAnimated);
        //    if (string.IsNullOrEmpty(imageUrl))
        //        await FollowupAsync("❌ 無法取得圖片，請確認標籤名稱是否正確（使用 `/waifu-標籤列表` 查看可用標籤）");
        //    else
        //        await FollowupAsync(imageUrl);
        //}

        [SlashCommand("waifu-自訂-nsfw", "使用自訂標籤取得 NSFW Waifu 圖片 (18+)")]
        [RequireNsfw]
        public async Task WaifuCustomNsfwAsync(
            [Summary("標籤", "NSFW 標籤名稱（可用逗號分隔多個標籤）")] string tags,
            [Summary("動畫", "是否只要 GIF 動畫")] bool isAnimated = false)
        {
            await DeferAsync();
            var imageUrl = await _waifuImService.GetImageByTagAsync(tags, isNsfw: true, isAnimated);
            if (string.IsNullOrEmpty(imageUrl))
                await FollowupAsync("❌ 無法取得圖片，請確認標籤名稱是否正確（使用 `/waifu-標籤列表` 查看可用標籤）");
            else
                await FollowupAsync(imageUrl);
        }

        //[SlashCommand("waifu", "取得隨機 Waifu 圖片（快速選項）")]
        //public async Task WaifuImageAsync(
        //    [Summary("標籤", "圖片標籤（可選）")]
        //    [Choice("Waifu", "waifu")]
        //    [Choice("Maid", "maid")]
        //    [Choice("Marin Kitagawa", "marin-kitagawa")]
        //    [Choice("Mori Calliope", "mori-calliope")]
        //    [Choice("Raiden Shogun", "raiden-shogun")]
        //    [Choice("Oppai", "oppai")]
        //    [Choice("Selfies", "selfies")]
        //    [Choice("Uniform", "uniform")]
        //    string tag = "waifu",
        //    bool isAnimated = false)
        //{
        //    await DeferAsync();
        //    var imageUrl = await _waifuImService.GetImageByTagAsync(tag, isNsfw: false, isAnimated);
        //    if (string.IsNullOrEmpty(imageUrl))
        //        await FollowupAsync("❌ 無法取得圖片，請稍後再試");
        //    else
        //        await FollowupAsync(imageUrl);
        //}

        [SlashCommand("waifu-nsfw", "取得隨機 NSFW Waifu 圖片 (18+)")]
        [RequireNsfw]
        public async Task WaifuNsfwImageAsync(
            [Summary("標籤", "NSFW 圖片標籤")]
            [Choice("Ero", "ero")]
            [Choice("Hentai", "hentai")]
            [Choice("Ass", "ass")]
            [Choice("Ecchi", "ecchi")]
            [Choice("Oral", "oral")]
            [Choice("Paizuri", "paizuri")]
            [Choice("Milf", "milf")]
            string tag = "ero",
            bool isAnimated = false)
        {
            await DeferAsync();
            var imageUrl = await _waifuImService.GetImageByTagAsync(tag, isNsfw: true, isAnimated);
            if (string.IsNullOrEmpty(imageUrl))
                await FollowupAsync("❌ 無法取得圖片，請稍後再試");
            else
                await FollowupAsync(imageUrl);
        }
        #endregion

        #region Waifu.pics 功能 暫時無法使用
        //[SlashCommand("anime", "取得隨機動漫圖片")]
        //public async Task AnimePicsAsync(
        //    [Summary("類型", "圖片類型")]
        //    [Choice("Waifu (老婆)", "waifu")]
        //    [Choice("Neko (貓娘)", "neko")]
        //    [Choice("Shinobu", "shinobu")]
        //    [Choice("Megumin (惠惠)", "megumin")]
        //    string category = "waifu")
        //{
        //    await DeferAsync();
        //    var embed = await _waifuPicsService.GetSfwImageAsync(category);
        //    await FollowupAsync(embed: embed);
        //}

        //[SlashCommand("anime-互動", "取得動漫互動圖片")]
        //public async Task AnimeActionAsync(
        //    [Summary("動作", "互動動作")]
        //    [Choice("Hug (抱抱)", "hug")]
        //    [Choice("Kiss (親親)", "kiss")]
        //    [Choice("Pat (摸頭)", "pat")]
        //    [Choice("Cuddle (擁抱)", "cuddle")]
        //    [Choice("Slap (巴掌)", "slap")]
        //    [Choice("Bonk (敲頭)", "bonk")]
        //    [Choice("Kick (踢)", "kick")]
        //    [Choice("Bite (咬)", "bite")]
        //    [Choice("Lick (舔)", "lick")]
        //    [Choice("Poke (戳)", "poke")]
        //    [Choice("Bully (霸凌)", "bully")]
        //    [Choice("Yeet (丟飛)", "yeet")]
        //    [Choice("Glomp (撲抱)", "glomp")]
        //    [Choice("Kill (殺)", "kill")]
        //    [Choice("Handhold (牽手)", "handhold")]
        //    [Choice("Highfive (擊掌)", "highfive")]
        //    string action = "hug")
        //{
        //    await DeferAsync();
        //    var embed = await _waifuPicsService.GetSfwImageAsync(action);
        //    await FollowupAsync(embed: embed);
        //}

        //[SlashCommand("anime-表情", "取得動漫表情圖片")]
        //public async Task AnimeEmoteAsync(
        //    [Summary("表情", "表情類型")]
        //    [Choice("Smile (微笑)", "smile")]
        //    [Choice("Blush (臉紅)", "blush")]
        //    [Choice("Happy (開心)", "happy")]
        //    [Choice("Cry (哭泣)", "cry")]
        //    [Choice("Smug (得意)", "smug")]
        //    [Choice("Wink (眨眼)", "wink")]
        //    [Choice("Wave (揮手)", "wave")]
        //    [Choice("Cringe (尷尬)", "cringe")]
        //    [Choice("Dance (跳舞)", "dance")]
        //    [Choice("Nom (吃東西)", "nom")]
        //    [Choice("Awoo (狼嚎)", "awoo")]
        //    string emote = "smile")
        //{
        //    await DeferAsync();
        //    var embed = await _waifuPicsService.GetSfwImageAsync(emote);
        //    await FollowupAsync(embed: embed);
        //}

        //[SlashCommand("anime-nsfw", "取得隨機 NSFW 動漫圖片 (18+)")]
        //[RequireNsfw]
        //public async Task AnimeNsfwAsync(
        //    [Summary("類型", "NSFW 類型")]
        //    [Choice("Waifu", "waifu")]
        //    [Choice("Neko", "neko")]
        //    [Choice("Trap", "trap")]
        //    [Choice("Blowjob", "blowjob")]
        //    string category = "waifu")
        //{
        //    await DeferAsync();
        //    var embed = await _waifuPicsService.GetNsfwImageAsync(category);
        //    await FollowupAsync(embed: embed);
        //}
        #endregion

        #region 產出圖片相關
        [SlashCommand("產出圖片", "產出圖片")]
        public async Task GenerateAIImageAsync(
            [Summary("提示詞", "提示詞")] string prompt
            )
        {
            await DeferAsync();

            try
            {
                using var imageStream = await _aiImageService.GenerateImageAsync(prompt);

                await FollowupWithFileAsync(
                    imageStream,
                    "ai-image.png"
                );
            }
            catch (Exception ex)
            {
                await FollowupAsync($"產生圖片失敗：{ex.Message}");
            }
        }
        #endregion

        #region stt & tts相關
        [SlashCommand("tts", "開關 TTS 語音回覆功能")]
        public async Task TtsToggleCommand()
        {
            var user = Context.User as SocketGuildUser;
            if (user?.VoiceChannel == null)
            {
                await RespondAsync("你不在語音頻道中", ephemeral: true);
                return;
            }

            // 透過 Program 切換 TTS
            _program.ToggleTts();
            var status = _program.IsTtsEnabled ? "✅ TTS 已啟用" : "❌ TTS 已關閉";
            await RespondAsync(status);
        }

        [SlashCommand("監聽", "開始監聽語音頻道（語音轉文字）")]
        public async Task ListenCommand()
        {
            var user = Context.User as SocketGuildUser;
            if (user?.VoiceChannel == null)
            {
                await RespondAsync("你不在語音頻道中", ephemeral: true);
                return;
            }

            await DeferAsync();
            var voiceChannelName = user.VoiceChannel.Name;
            var started = await _program.StartVoiceListeningAsync(user);
            await FollowupAsync(started
                ? $"👂 開始監聽語音頻道: {voiceChannelName}"
                : "❌ 無法開始監聽（可能已在監聽中，或語音連線失敗）");
        }

        [SlashCommand("取消監聽", "停止監聽語音頻道")]
        public async Task UnlistenCommand()
        {
            var user = Context.User as SocketGuildUser;
            if (user?.VoiceChannel == null)
            {
                await RespondAsync("你不在語音頻道中", ephemeral: true);
                return;
            }

            await DeferAsync();
            var voiceChannelId = user.VoiceChannel.Id;
            await _groqWhisperService.StopListeningAsync(voiceChannelId);
            await FollowupAsync("🔇 已停止監聽語音頻道");
        }
        #endregion

        #region 音色切換
        [SlashCommand("切換音色", "切換 Soyo 說話的音色")]
        public async Task ChangeVoiceStyle(
            [Summary("音色", "選擇想要的音色")]
            [Choice("SOYO", "SOYO")]
            [Choice("預設", "預設")]
            [Choice("ANON", "ANON")]
            [Choice("更高級的soyo", "更高級的soyo")]
            [Choice("tomo", "tomo")]
            [Choice("中文anon", "中文anon")]
            [Choice("是我迪奧", "是我迪奧")]
            [Choice("中文tomo", "中文tomo")]
            [Choice("中文soyo", "中文soyo")]
            [Choice("中文祥子", "中文祥子")]
            string voiceName)
        {
            var result = _fishAudioService.SetVoice(voiceName);
            if (result != null)
            {
                await RespondAsync($"✅ 音色已切換為: **{result}**");
            }
            else
            {
                await RespondAsync("❌ 找不到該音色", ephemeral: true);
            }
        }

        [SlashCommand("目前音色", "查看目前使用的音色")]
        public async Task CurrentVoiceStyle()
        {
            var name = _fishAudioService.GetCurrentVoiceName();
            await RespondAsync($"🎙️ 目前音色: **{name}**");
        }
        #endregion

        #region FGO 猜謎
        [SlashCommand("fgo我是誰", "猜 FGO 從者")]
        public async Task FgoSilhouetteAsync()
        {
            await DeferAsync();
            var (component, embed) = await _fgoGuessService.StartSilhouetteGameAsync(Context.Channel.Id);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("fgo猜寶具", "看角色猜 FGO 寶具名稱")]
        public async Task FgoNpGuessAsync()
        {
            await DeferAsync();
            var (component, embed) = await _fgoGuessService.StartNpGameAsync(Context.Channel.Id);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("fgo猜階段", "看從者圖猜是第幾升階階段（1～4）")]
        public async Task FgoAscensionGuessAsync()
        {
            await DeferAsync();
            var (component, embed) = await _fgoGuessService.StartAscensionGameAsync(Context.Channel.Id);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        #endregion

        #region 聖杯塔 Roguelike
        [SlashCommand("fate聖杯塔註冊", "註冊成為聖杯塔的御主")]
        public async Task TowerRegisterAsync()
        {
            await DeferAsync();
            var (embed, component) = await _holyGrailTowerService.RegisterPlayerAsync(Context.User.Id, Context.User.Username);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("fate聖杯塔資訊", "查看你的御主資訊")]
        public async Task TowerInfoAsync()
        {
            var (embed, component) = _holyGrailTowerService.GetPlayerInfo(Context.User.Id);
            await RespondAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("fate聖杯塔召喚", "使用召喚券抽取從者")]
        public async Task TowerSummonAsync([Summary("五連抽", "是否開啟五連抽（預設單抽）")] bool multip = false)
        {
            await DeferAsync();
            if (multip)
            {
                var results = await _holyGrailTowerService.SummonMultipleAsync(Context.User.Id, Context.User.Username, 5);
                foreach (var (embed, component) in results)
                    await FollowupAsync(embed: embed, components: component.Build());
            }
            else
            {
                var (embed, component) = await _holyGrailTowerService.SummonServantAsync(Context.User.Id, Context.User.Username);
                await FollowupAsync(embed: embed, components: component.Build());
            }
        }

        [SlashCommand("fate聖杯塔圖鑑", "查看你的從者圖鑑")]
        public async Task TowerServantsAsync([Summary("從者編號", "輸入 No. 查詢單一從者；不填則顯示全部")] int? collectionNo = null)
        {
            if (collectionNo.HasValue)
            {
                var (embed, component) = _holyGrailTowerService.GetServantDetail(Context.User.Id, collectionNo.Value);
                await RespondAsync(embed: embed, components: component.Build());
            }
            else
            {
                var (embed, component) = _holyGrailTowerService.ListServants(Context.User.Id);
                await RespondAsync(embed: embed, components: component.Build());
            }
        }

        [SlashCommand("fate聖杯塔丟棄從者", "批量丟棄從者（顯示選單）")]
        public async Task TowerReleaseServantAsync()
        {
            var (embed, component) = _holyGrailTowerService.ShowBatchReleaseMenuAsync(Context.User.Id);
            await RespondAsync(embed: embed, components: component.Build(), ephemeral: true);
        }

        [SlashCommand("fate聖杯塔每日", "領取每日獎勵（3 張召喚券）")]
        public async Task TowerDailyAsync()
        {
            await DeferAsync();
            var (embed, component) = await _holyGrailTowerService.ClaimDailyRewardAsync(Context.User.Id, Context.User.Username);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("fate聖杯塔開始爬塔", "開始聖杯塔挑戰")]
        public async Task StartTowerAsync()
        {
            await DeferAsync();
            var (embed, component) = await _holyGrailTowerService.StartTowerRunAsync(Context.Channel.Id, Context.User.Id, Context.User.Username);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("fate聖杯塔取消爬塔", "放棄或取消現在頻道的聖杯塔挑戰")]
        public async Task CancelTowerAsync()
        {
            await DeferAsync();
            var (embed, component) = await _holyGrailTowerService.CancelTowerRunAsync(Context.Channel.Id, Context.User.Id);
            await FollowupAsync(embed: embed, components: component.Build());
        }
        #endregion

        #region 遊戲王決鬥

        private static readonly string[] YgoDeckChoices = new[] { "yugi", "kaiba", "joey", "jaden", "yusei", "yuya" };

        [SlashCommand("決鬥牌組列表", "查看所有可用的牌組")]
        public async Task YgoDecksAsync()
        {
            var (embed, component) = _ygoService.ListDecks();
            await RespondAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("決鬥場地", "顯示當前決鬥場地")]
        public async Task YgoBoardAsync()
        {
            await DeferAsync();
            var (embed, component) = await _ygoService.GetBoardAsync(Context.Channel.Id);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("決鬥ai", "用動漫牌組挑戰AI決鬥")]
        public async Task YgoDuelAiAsync(
            [Summary("我的牌組", "你使用的牌組")]
            [Choice("🔮 武藤遊戲 (DM)", "yugi")]
            [Choice("🐉 海馬瀨人 (DM)", "kaiba")]
            [Choice("🃏 城之內克也 (DM)", "joey")]
            [Choice("🦅 孔雀舞 (DM)", "mai")]
            [Choice("☀️ 馬立克 (DM)", "marik")]
            [Choice("👁️ 佩加瑟斯 (DM)", "pegasus")]
            [Choice("💀 獏良了 (DM)", "bakura")]
            [Choice("⚡ 遊城十代 (GX)", "jaden")]
            [Choice("🏆 万丈目準 (GX)", "chazz")]
            [Choice("🌸 天上院明日香 (GX)", "alexis")]
            [Choice("⚙️ 丸藤亮 (GX)", "zane")]
            string myDeck = "yugi",
            [Summary("ai牌組", "AI 使用的牌組")]
            [Choice("🔮 武藤遊戲 (DM)", "yugi")]
            [Choice("🐉 海馬瀨人 (DM)", "kaiba")]
            [Choice("🃏 城之內克也 (DM)", "joey")]
            [Choice("🦅 孔雀舞 (DM)", "mai")]
            [Choice("☀️ 馬立克 (DM)", "marik")]
            [Choice("👁️ 佩加瑟斯 (DM)", "pegasus")]
            [Choice("💀 獏良了 (DM)", "bakura")]
            [Choice("⚡ 遊城十代 (GX)", "jaden")]
            [Choice("🏆 万丈目準 (GX)", "chazz")]
            [Choice("🌸 天上院明日香 (GX)", "alexis")]
            [Choice("⚙️ 丸藤亮 (GX)", "zane")]
            string aiDeck = "kaiba")
        {
            await DeferAsync();
            var player = Context.User as Discord.WebSocket.SocketGuildUser;
            var (embed, component) = await _ygoService.StartPvAiDuelAsync(
                Context.Channel.Id, player!, myDeck.ToLower(), aiDeck.ToLower());
            await FollowupAsync(embed: embed, components: component.Build());
        }

        [SlashCommand("查詢卡片", "查詢遊戲王卡片資訊")]
        public async Task YgoCardInfoAsync([Summary("卡名", "英文卡名")] string name)
        {
            await DeferAsync();
            var (embed, component) = await _ygoService.ShowCardInfoAsync(name);
            await FollowupAsync(embed: embed, components: component.Build());
        }

        #endregion

        #region FreeDuel

        [SlashCommand("freeduel", "開始自由決鬥（對話式，無規則限制）")]
        public async Task FreeDuelAsync(
            [Summary("我的牌組"), Discord.Interactions.Choice("武藤遊戲","yugi"), Discord.Interactions.Choice("海馬瀬人","kaiba"),
             Discord.Interactions.Choice("城之內克也","joey"), Discord.Interactions.Choice("孔雀舞","mai"), Discord.Interactions.Choice("馬立克","marik"),
             Discord.Interactions.Choice("佩加瑟斯","pegasus"), Discord.Interactions.Choice("獏良了","bakura"),
             Discord.Interactions.Choice("遊城十代","jaden"), Discord.Interactions.Choice("萬丈目準","chazz"),
             Discord.Interactions.Choice("天上院明日香","alexis"), Discord.Interactions.Choice("丸藤亮","zane")]
            string myDeck = "yugi",
            [Summary("對手"), Discord.Interactions.Choice("武藤遊戲","yugi"), Discord.Interactions.Choice("海馬瀬人","kaiba"),
             Discord.Interactions.Choice("城之內克也","joey"), Discord.Interactions.Choice("孔雀舞","mai"), Discord.Interactions.Choice("馬立克","marik"),
             Discord.Interactions.Choice("佩加瑟斯","pegasus"), Discord.Interactions.Choice("獏良了","bakura"),
             Discord.Interactions.Choice("遊城十代","jaden"), Discord.Interactions.Choice("萬丈目準","chazz"),
             Discord.Interactions.Choice("天上院明日香","alexis"), Discord.Interactions.Choice("丸藤亮","zane")]
            string opponent = "kaiba")
        {
            await DeferAsync();
            var user = Context.User as Discord.WebSocket.SocketGuildUser;
            string playerName = user?.DisplayName ?? Context.User.Username;

            if (await _freeDuelSvc.IsDuelActiveAsync(Context.Channel.Id))
            {
                await FollowupAsync("❌ 此頻道已有決鬥進行中！請先使用 `/endduel` 結束。");
                return;
            }

            var (embed, component, message) = await _freeDuelSvc.StartDuelAsync(
                Context.Channel.Id, Context.User.Id, playerName, myDeck, opponent);

            await FollowupAsync(text: message, embed: embed, components: component.Build());
        }

        [SlashCommand("endduel", "強制結束自由決鬥，恢復頻道正常功能")]
        public async Task EndFreeDuelAsync()
        {
            await DeferAsync();
            if (!await _freeDuelSvc.IsDuelActiveAsync(Context.Channel.Id))
            {
                await FollowupAsync("此頻道沒有進行中的自由決鬥。");
                return;
            }
            var msg = await _freeDuelSvc.ForceEndDuelAsync(Context.Channel.Id);
            await FollowupAsync(msg);
        }

        #endregion
    }
}