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
using System.Net.Http;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

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
        private readonly RVC_Service _rVC_Service;
        private readonly SetTextService _setTextService;
        private readonly Game2048Service _game2048Service;
        private readonly Pick2Service _pick2Service;
        private readonly JikanAnimeService _animeService;

        public SlashCommandHandler(Program program, WordGuessingService wordService, MineGameService mineGameService, ElevenLabsService elevenLabsService, OldMaidService oldMaidService, RubiksCubeService rubiksCubeService, GoogleAIStudioService googleAIStudioService, RVC_Service rVC_Service, SetTextService setTextService, Game2048Service game2048Service, Pick2Service pick2Service,JikanAnimeService animeService)
        {
            _program = program;
            _wordService = wordService;
            _elevenLabsService = elevenLabsService;
            _mineGameService = mineGameService;
            _setTextService = setTextService;
            _oldMaidService = oldMaidService;
            _rubiksCubeService = rubiksCubeService;
            _googleAIStudioService = googleAIStudioService;
            _rVC_Service = rVC_Service;
            _game2048Service = game2048Service;
            _pick2Service = pick2Service;
            _animeService = animeService;
        }

        [SlashCommand("播放音樂", "播放音樂")]
        public async Task PlayCommand([Summary("查詢", "YouTube URL 或搜尋關鍵字")] string query)
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            await _program.PlayMusicAsync(Context.Channel, user, query);
            await FollowupAsync("-", ephemeral: true);
        }

        [SlashCommand("撥放bilibili", "播放 Bilibili 音樂")]
        public async Task BilibiliCommand([Summary("網址", "Bilibili 影片網址")] string url)
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            await _program.PlayBiblibiliMusicAsync(Context.Channel, user, url);
            await FollowupAsync("-", ephemeral: true);
        }

        [SlashCommand("跳過目前歌曲", "跳過目前歌曲")]
        public async Task SkipCommand()
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            await _program.SkipMusic(Context.Channel, user);
            await FollowupAsync("-", ephemeral: true);
        }

        [SlashCommand("循環播放目前歌曲", "循環播放目前歌曲")]
        public async Task LoopCommand()
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            await _program.LoopMusic(Context.Channel, user);
            await FollowupAsync("-", ephemeral: true);
        }

        [SlashCommand("取消循環撥放", "取消循環播放")]
        public async Task UnloopCommand()
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            await _program.UnLoopMusic(Context.Channel, user);
            await FollowupAsync("-", ephemeral: true);
        }

        [SlashCommand("開關推薦音樂", "開啟/關閉推薦音樂")]
        public async Task RelatedCommand()
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            await _program.HandleRelatedMusicAsync(Context.Channel, user);
            await FollowupAsync("-", ephemeral: true);
        }

        [SlashCommand("搜尋並播放音樂", "搜尋並播放音樂")]
        public async Task FindCommand([Summary("關鍵字", "搜尋關鍵字")] string query)
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            string url = await _program.GetYoutubeUrlByNameAsync(Context.Channel, query);
            if (!string.IsNullOrEmpty(url))
            {
                await _program.PlayMusicAsync(Context.Channel, user, url);
            }
            await FollowupAsync("-", ephemeral: true);
        }

        [SlashCommand("顯示目前播放清單", "顯示目前播放清單")]
        public async Task ListCommand()
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            await _program.CalledPlayListAsync(Context.Channel, user);
            await FollowupAsync("-", ephemeral: true);
        }

        [SlashCommand("開關earrape", "開啟/關閉 Ear Rape 模式")]
        public async Task EarRapeCommand()
        {
            await DeferAsync();
            var user = Context.User as SocketGuildUser;
            await _program.EarRapeAsync(Context.Channel, user);
            await FollowupAsync("-", ephemeral: true);
        }

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
            await champService.GuessChampSkillAsync(Context.Channel as IMessageChannel, champName.ToLower(), skillPos.ToLower(), userGuess.ToLower());
            await FollowupAsync("-", ephemeral: true);
        }

        [SlashCommand("猜單字", "猜單字")]
        public async Task Guess(string word)
        {
            try
            {
                var user = Context.User as SocketGuildUser;
                string res = await _wordService.Guess(word, user);
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
            catch(Exception ex)
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

        [SlashCommand("送光", "送光")]
        public async Task SendLightAsync(
            [Summary("你的代名", "你想用的名字")] string sender,
            [Summary("想送的對象", "請選擇對象")] IUser target,
            [Summary("自訂訊息", "你想要附加的訊息，選填，如果要的話，幫我以/me代表自己，/target代表你要發送的對象")] string message = ""
        )
        {
            var channel = Context.Client.GetChannel(592716175461580800) as ISocketMessageChannel;
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

        [SlashCommand("soyo記憶消除", "清除 Soyo 的記憶")]
        public async Task ClearSoyoMemory(
            [Summary("頻道", "要清除記憶的頻道")] string channelKey = null
        )
        {
            await DeferAsync();

            await _googleAIStudioService.ClearMemoryAsync(channelKey);

            await FollowupAsync($"已清除 Soyo 的記憶 ({channelKey ?? "全部頻道"})");
        }
    }
}