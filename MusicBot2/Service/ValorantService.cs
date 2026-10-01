using Discord;
using Discord.WebSocket;
using MusicBot2.Helpers;
using MusicBot2.Models;
using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    //https://valorant-api.com/v1/agents?language=zh-TW
    public class ValorantService
    {
        private readonly HttpClient _httpClient;
        private const string API_BASE_URL = "https://valorant-api.com/v1/agents?language=zh-TW";
        private const string HenrikDevToken = "HDEV-a084e574-e764-43d6-9976-a03427aaf334";
        private const string HenrikDevBase = "https://api.henrikdev.xyz";

        // Discord UserId → (Valorant Name, Tag, Region)
        // Region: ap(亞太) / na / eu / kr / latam / br
        public static readonly Dictionary<ulong, (string Name, string Tag, string Region)> FriendsList = new()
        {
            { 325482625127153664UL, ("小老朋鳥", "大老鳥", "ap") },
            { 415032840925741056UL, ("小老月月鳥", "鬆鬆餅", "ap") },
            { 581407603658326016UL, ("Takoyaki", "9120", "ap") },
            { 541105947435859978UL, ("MadCorgi", "1111", "ap") },
        };

        public ValorantService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        #region HenrikDev 戰績查詢

        public async Task<(string TextForAI, Embed Embed)> GetPlayerStatsAsync(
            string name, string tag, string region = "ap",
            bool showRank = true, bool showMatches = true,
            bool showHeadshot = true, bool showAgents = true, bool showWeapons = true)
        {
            var (text, eb) = await GetPlayerStatsInternalAsync(name, tag, region, showRank, showMatches, showHeadshot, showAgents, showWeapons);
            if (text == null) return (null, null);
            return (text, eb.Build());
        }

        public async Task<string> GetPlayerStatsTextAsync(string name, string tag, string region = "ap",
            bool showRank = true, bool showMatches = true,
            bool showHeadshot = true, bool showAgents = true, bool showWeapons = true)
        {
            var (text, _) = await GetPlayerStatsInternalAsync(name, tag, region, showRank, showMatches, showHeadshot, showAgents, showWeapons);
            return text;
        }

        private async Task<(string TextForAI, EmbedBuilder Embed)> GetPlayerStatsInternalAsync(
            string name, string tag, string region = "ap",
            bool showRank = true, bool showMatches = true,
            bool showHeadshot = true, bool showAgents = true, bool showWeapons = true)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                _httpClient.DefaultRequestHeaders.Add("Authorization", HenrikDevToken);

                // Account info
                var accountUrl = $"{HenrikDevBase}/valorant/v1/account/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(tag)}";
                var accountResp = await _httpClient.GetAsync(accountUrl);
                string accountLevel = "?";
                if (accountResp.IsSuccessStatusCode)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(await accountResp.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("data", out var d))
                    {
                        accountLevel = d.TryGetProperty("account_level", out var lv) ? lv.GetInt32().ToString() : "?";
                    }
                }

                // MMR (current rank)
                var mmrUrl = $"{HenrikDevBase}/valorant/v2/mmr/{region}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(tag)}";
                var mmrResp = await _httpClient.GetAsync(mmrUrl);
                string rank = "未知", rr = "?", peakRank = "未知";
                if (mmrResp.IsSuccessStatusCode)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(await mmrResp.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("data", out var d))
                    {
                        if (d.TryGetProperty("current_data", out var cur))
                        {
                            rank = cur.TryGetProperty("currenttierpatched", out var r) ? r.GetString() ?? "未知" : "未知";
                            rr = cur.TryGetProperty("ranking_in_tier", out var rrEl) ? rrEl.GetInt32().ToString() : "?";
                        }
                        if (d.TryGetProperty("highest_rank", out var peak))
                            peakRank = peak.TryGetProperty("patched_tier", out var pr) ? pr.GetString() ?? "未知" : "未知";
                    }
                }

                // Recent matches (request 10; free tier may return fewer)
                var matchUrl = $"{HenrikDevBase}/valorant/v3/matches/{region}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(tag)}?size=10";
                var matchResp = await _httpClient.GetAsync(matchUrl);
                var matchSummaries = new List<string>();
                int totalKills = 0, totalDeaths = 0, totalAssists = 0, matchCount = 0;
                int totalHeadshots = 0, totalBodyshots = 0, totalLegshots = 0;
                int wins = 0;
                var agentCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var weaponCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                Console.WriteLine($"[ValorantService] matches {(int)matchResp.StatusCode}: {matchUrl}");
                if (!matchResp.IsSuccessStatusCode)
                {
                    var errBody = await matchResp.Content.ReadAsStringAsync();
                    Console.WriteLine($"[ValorantService] matches error body: {errBody[..Math.Min(300, errBody.Length)]}");
                }

                if (matchResp.IsSuccessStatusCode)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(await matchResp.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("data", out var matches) && matches.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var match in matches.EnumerateArray())
                        {
                            try
                            {
                                string map = match.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("map", out var m) ? m.GetString() : "?";
                                string mode = meta.TryGetProperty("mode", out var md) ? md.GetString() : "?";
                                bool? won = null;
                                int k = 0, d = 0, a = 0;
                                string agent = "?";

                                if (match.TryGetProperty("players", out var players) && players.TryGetProperty("all_players", out var allPlayers))
                                {
                                    foreach (var p in allPlayers.EnumerateArray())
                                    {
                                        var pName = p.TryGetProperty("name", out var pn) ? pn.GetString() : "";
                                        var pTag = p.TryGetProperty("tag", out var pt) ? pt.GetString() : "";
                                        if (string.Equals(pName, name, StringComparison.OrdinalIgnoreCase) &&
                                            string.Equals(pTag, tag, StringComparison.OrdinalIgnoreCase))
                                        {
                                            agent = p.TryGetProperty("character", out var ch) ? ch.GetString() ?? "?" : "?";
                                            agentCount.TryGetValue(agent, out int ac);
                                            agentCount[agent] = ac + 1;

                                            if (p.TryGetProperty("stats", out var stats))
                                            {
                                                k = stats.TryGetProperty("kills", out var kk) ? kk.GetInt32() : 0;
                                                d = stats.TryGetProperty("deaths", out var dd) ? dd.GetInt32() : 0;
                                                a = stats.TryGetProperty("assists", out var aa) ? aa.GetInt32() : 0;
                                                totalHeadshots += stats.TryGetProperty("headshots", out var hs) ? hs.GetInt32() : 0;
                                                totalBodyshots += stats.TryGetProperty("bodyshots", out var bs) ? bs.GetInt32() : 0;
                                                totalLegshots += stats.TryGetProperty("legshots", out var ls) ? ls.GetInt32() : 0;
                                            }

                                            // weapon usage from economy object
                                            if (p.TryGetProperty("economy", out var eco)
                                                && eco.TryGetProperty("weapon", out var wpn)
                                                && wpn.TryGetProperty("name", out var wn))
                                            {
                                                var wname = wn.GetString();
                                                if (!string.IsNullOrWhiteSpace(wname))
                                                {
                                                    weaponCount.TryGetValue(wname, out int wc);
                                                    weaponCount[wname] = wc + 1;
                                                }
                                            }

                                            won = p.TryGetProperty("team", out var team) && match.TryGetProperty("teams", out var teams)
                                                ? CheckWon(team.GetString(), teams) : null;
                                            break;
                                        }
                                    }
                                }
                                totalKills += k; totalDeaths += d; totalAssists += a; matchCount++;
                                if (won == true) wins++;
                                string result = won == true ? "勝" : won == false ? "敗" : "?";
                                matchSummaries.Add($"{result} {map}({mode}) {agent} {k}/{d}/{a}");
                            }
                            catch (Exception mex) { Console.WriteLine($"[ValorantService] match parse err: {mex.Message}"); }
                        }
                    }
                }

                float kda = totalDeaths > 0 ? (float)(totalKills + totalAssists) / totalDeaths : totalKills + totalAssists;
                float avgKills = matchCount > 0 ? (float)totalKills / matchCount : 0;
                float avgDeaths = matchCount > 0 ? (float)totalDeaths / matchCount : 0;
                int totalShots = totalHeadshots + totalBodyshots + totalLegshots;
                float hsRate = totalShots > 0 ? (float)totalHeadshots / totalShots * 100 : 0;
                float winRate = matchCount > 0 ? (float)wins / matchCount * 100 : 0;

                var topAgents = agentCount.OrderByDescending(x => x.Value).Take(3)
                    .Select(x => $"{x.Key}({x.Value}場)").ToList();
                var topWeapons = weaponCount.OrderByDescending(x => x.Value).Take(3)
                    .Select(x => $"{x.Key}({x.Value})").ToList();

                // ── Plain text for AI ──
                var sb = new StringBuilder();
                sb.AppendLine($"玩家：{name}#{tag}　帳號等級：{accountLevel}");
                if (showRank)
                    sb.AppendLine($"當前段位：{rank}（{rr} RR）　歷史最高：{peakRank}");
                if (matchCount > 0)
                {
                    sb.AppendLine($"近 {matchCount} 場：勝率 {wins}/{matchCount}（{winRate:F0}%）　KDA {totalKills}/{totalDeaths}/{totalAssists}（{kda:F2}）　場均 {avgKills:F1}K/{avgDeaths:F1}D");
                    if (totalShots > 0) sb.AppendLine($"爆頭率：{hsRate:F1}%");
                    if (topAgents.Any()) sb.AppendLine($"常用英雄：{string.Join("、", topAgents)}");
                    if (topWeapons.Any()) sb.AppendLine($"常用武器：{string.Join("、", topWeapons)}");
                    foreach (var ms in matchSummaries) sb.AppendLine(ms);
                }

                // ── Discord Embed ──
                var eb = new EmbedBuilder()
                    .WithTitle($"🎮  {name}#{tag}")
                    .WithColor(new Color(0xFF4655))
                    .WithFooter($"Valorant 戰績 · HenrikDev API · 地區：{region.ToUpper()}");

                if (showRank)
                {
                    eb.AddField("🏅 當前段位", $"{rank}\n{rr} RR", inline: true);
                    eb.AddField("📈 歷史最高", peakRank, inline: true);
                    eb.AddField("🎖️ 帳號等級", accountLevel, inline: true);
                }

                if (matchCount > 0 && (showMatches || showHeadshot || showAgents || showWeapons))
                {
                    eb.AddField("📊 近期統計", $"共 {matchCount} 場  |  勝率 **{wins}/{matchCount}**（{winRate:F0}%）", inline: false);
                    eb.AddField("⚔️ KDA", $"{totalKills} / {totalDeaths} / {totalAssists}\n比值 **{kda:F2}**", inline: true);
                    eb.AddField("📉 場均", $"**{avgKills:F1}** 殺 / **{avgDeaths:F1}** 死", inline: true);

                    if (showHeadshot && totalShots > 0)
                        eb.AddField("🎯 爆頭率", $"**{hsRate:F1}%**\n爆頭 {totalHeadshots} · 軀幹 {totalBodyshots} · 腿 {totalLegshots}", inline: true);

                    if (showAgents && topAgents.Any())
                        eb.AddField("🦸 常用英雄", string.Join("\n", topAgents), inline: true);

                    if (showWeapons && topWeapons.Any())
                        eb.AddField("🔫 常用武器", string.Join("\n", topWeapons), inline: true);

                    if (showMatches && matchSummaries.Any())
                    {
                        var matchLines = string.Join("\n", matchSummaries.Select(m => {
                            var won2 = m.StartsWith("勝");
                            return $"{(won2 ? "🟢" : "🔴")} {m}";
                        }));
                        eb.AddField($"📋 近 {matchSummaries.Count} 場對局", matchLines, inline: false);
                    }
                }

                return (sb.ToString().TrimEnd(), eb);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ValorantService] GetPlayerStats 失敗: {ex.Message}");
                return (null, null);
            }
        }

        private bool CheckWon(string teamColor, System.Text.Json.JsonElement teams)
        {
            if (string.IsNullOrWhiteSpace(teamColor)) return false;
            if (teams.TryGetProperty(teamColor.ToLower(), out var t))
                return t.TryGetProperty("has_won", out var w) && w.GetBoolean();
            return false;
        }

        #endregion

        #region 猜角色圖片
        // 儲存遊戲狀態
        private readonly Dictionary<ulong, ValorantGameSession> _activeImageGames = new Dictionary<ulong, ValorantGameSession>();
        private readonly Dictionary<ulong, ValorantGameSession> _activeAbilityGames = new Dictionary<ulong, ValorantGameSession>();

        // 中英文名稱映射（從 API 動態獲取）
        private Dictionary<string, string> _nameMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 開始猜 Valorant 角色圖片遊戲（所有人都可以猜）
        /// </summary>
        public async Task<((ComponentBuilder component, Embed embed), Stream silhouette)> StartGuessAgentImageAsync(ulong channelId)
        {
            try
            {
                var response = await _httpClient.GetAsync(API_BASE_URL);

                if (!response.IsSuccessStatusCode)
                    return (CommonHelper.BuildErrorResponse("無法獲取 Valorant 角色資料"), null);

                var responseContent = await response.Content.ReadAsStringAsync();
                var agentsResponse = JsonConvert.DeserializeObject<ValorantAgentsResponse>(responseContent);

                // 過濾出可玩角色
                var playableAgents = agentsResponse.data
                    .Where(a => a.isPlayableCharacter && !string.IsNullOrEmpty(a.fullPortrait))
                    .ToList();

                if (playableAgents.Count == 0)
                    return (CommonHelper.BuildErrorResponse("找不到可用的角色資料"), null);

                Random random = new Random();

                // 隨機選擇正確答案
                var correctAgent = playableAgents[random.Next(playableAgents.Count)];

                // 儲存遊戲狀態（使用 channelId 而不是 userId，讓所有人都能猜）
                _activeImageGames[channelId] = new ValorantGameSession
                {
                    CorrectAgent = correctAgent,
                    IsImageMode = true
                };

                // 塗黑圖片（保留白色部分）
                var silhouette = await MakeBlackSilhouette(correctAgent.fullPortrait);

                // 不顯示按鈕，提示使用 slash command
                var componentBuilder = new ComponentBuilder();

                // 建立 Embed
                var embedBuilder = new EmbedBuilder()
                    .WithTitle("🎮 各位瓦學弟們 猜猜這是哪個角色？")
                    .WithDescription("我是誰?\n\n請使用指令回答：`/回答valorant角色 答案:[角色名稱]`\n中文英文都可以！")
                    .WithColor(Discord.Color.Red)
                    .WithImageUrl("attachment://silhouette.png")
                    .WithFooter($"頻道ID: {channelId}")
                    .WithCurrentTimestamp();

                return ((componentBuilder, embedBuilder.Build()), silhouette);
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}"), null);
            }
        }

        /// <summary>
        /// 將圖片非白色部分塗黑
        /// </summary>
        private async Task<Stream> MakeBlackSilhouette(string imageUrl)
        {
            var bytes = await _httpClient.GetByteArrayAsync(imageUrl);
            using var bitmap = SKBitmap.Decode(bytes);
            using var result = new SKBitmap(bitmap.Width, bitmap.Height);

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);

                    // 如果是透明或接近白色，保留白色背景
                    if (pixel.Alpha < 25 || IsNearWhite(pixel))
                        result.SetPixel(x, y, SKColors.White);
                    else
                        result.SetPixel(x, y, SKColors.Black);  // 其他部分塗黑
                }
            }

            using var image = SKImage.FromBitmap(result);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            var output = new MemoryStream();
            data.SaveTo(output);
            output.Position = 0;
            return output;
        }

        /// <summary>
        /// 判斷像素是否接近白色
        /// </summary>
        private bool IsNearWhite(SKColor color)
        {
            // 如果 RGB 值都大於 250，視為白色
            return color.Red > 250 && color.Green > 250 && color.Blue > 250;
        }
        #endregion

        #region 猜角色技能
        /// <summary>
        /// 開始猜 Valorant 技能名稱遊戲（所有人都可以猜）
        /// </summary>
        public async Task<(ComponentBuilder component, Embed embed)> StartGuessAbilityNameAsync(ulong channelId)
        {
            try
            {
                var response = await _httpClient.GetAsync(API_BASE_URL);

                if (!response.IsSuccessStatusCode)
                    return CommonHelper.BuildErrorResponse("無法獲取 Valorant 角色資料");

                var responseContent = await response.Content.ReadAsStringAsync();
                var agentsResponse = JsonConvert.DeserializeObject<ValorantAgentsResponse>(responseContent);

                // 過濾出可玩角色且有技能的
                var playableAgents = agentsResponse.data
                    .Where(a => a.isPlayableCharacter && a.abilities != null && a.abilities.Any())
                    .ToList();

                if (playableAgents.Count == 0)
                    return CommonHelper.BuildErrorResponse("找不到可用的角色資料");

                Random random = new Random();

                // 隨機選擇正確答案
                var correctAgent = playableAgents[random.Next(playableAgents.Count)];

                // 隨機選擇該角色的一個技能（排除 Passive）
                var validAbilities = correctAgent.abilities
                    .Where(a => !string.IsNullOrEmpty(a.displayIcon) && a.slot != "Passive")
                    .ToList();

                if (validAbilities.Count == 0)
                    return CommonHelper.BuildErrorResponse("該角色沒有可用的技能圖示");

                var randomAbility = validAbilities[random.Next(validAbilities.Count)];

                // 儲存遊戲狀態（使用 channelId 而不是 userId，讓所有人都能猜）
                _activeAbilityGames[channelId] = new ValorantGameSession
                {
                    CorrectAgent = correctAgent,
                    SelectedAbility = randomAbility,
                    IsImageMode = false
                };

                // 不顯示按鈕
                var componentBuilder = new ComponentBuilder();

                // 建立 Embed
                var embedBuilder = new EmbedBuilder()
                    .WithTitle("🎮 猜猜這個技能叫什麼名字？")
                    .WithDescription($"**角色：** {correctAgent.displayName}\n**技能類型：** {randomAbility.slot}\n\n{randomAbility.description}\n\n請使用指令回答：`/回答valorant技能 答案:[技能名稱]`\n中文英文都可以！")
                    .WithColor(Discord.Color.Red)
                    .WithThumbnailUrl(randomAbility.displayIcon)
                    .WithFooter($"頻道ID: {channelId}")
                    .WithCurrentTimestamp();

                return (componentBuilder, embedBuilder.Build());
            }
            catch (Exception ex)
            {
                return CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}");
            }
        }
        #endregion

        #region 共用


        /// <summary>
        /// 處理角色圖片答案（slash command）
        /// </summary>
        public async Task<(bool isCorrect, Embed embed, string? correctName)> HandleAgentAnswerAsync(ulong channelId, string userAnswer, SocketGuildUser user, ISocketMessageChannel channel)
        {
            // 檢查是否有遊戲進行中
            if (!_activeImageGames.ContainsKey(channelId))
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("⚠️ 提示")
                    .WithColor(Discord.Color.Orange)
                    .WithDescription("找不到遊戲，請先使用 `/猜猜我是誰瓦學弟ver` 開始遊戲！")
                    .Build();
                return (false, errorEmbed, null);
            }

            var session = _activeImageGames[channelId];
            userAnswer = userAnswer.Trim();

            // 檢查答案（支援中英文，不區分大小寫）
            bool isCorrect = await CheckAgentAnswerAsync(session.CorrectAgent, userAnswer);

            if (isCorrect)
            {
                // 答對了
                _activeImageGames.Remove(channelId);

                var rewardText = await RewardsHelpers.GetRandomRewards(channel, user);

                var embedBuilder = new EmbedBuilder()
                    .WithTitle("🎉 答對了！")
                    .WithColor(Discord.Color.Green)
                    .WithDescription($"正確答案就是 **{session.CorrectAgent.displayName}**！")
                    .AddField("角色定位", session.CorrectAgent.role?.displayName ?? "未知", inline: true)
                    .AddField("恭喜", $"Valorant 大師 **{user.Mention}** {CommonHelper.GetUserFace(user.Id.ToString())}", inline: true)
                    .AddField("獎勵", rewardText)
                    .WithImageUrl(session.CorrectAgent.fullPortrait)
                    .WithTimestamp(DateTimeOffset.Now);

                return (true, embedBuilder.Build(), session.CorrectAgent.displayName);
            }
            else
            {
                // 答錯了
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ 答錯了！")
                    .WithColor(Discord.Color.Red)
                    .WithDescription($"{user.Mention} {CommonHelper.GetUserFace(user.Id.ToString())} 答：**{userAnswer}**\n\n請再試一次！")
                    .Build();
                return (false, errorEmbed, null);
            }
        }

        /// <summary>
        /// 處理技能名稱答案（slash command）
        /// </summary>
        public async Task<(bool isCorrect, string? wrongAnswer, string? correctAnswer, string? agentName)> HandleAbilityAnswerAsync(ulong channelId, string userAnswer)
        {
            // 檢查是否有遊戲進行中
            if (!_activeAbilityGames.ContainsKey(channelId))
            {
                return (false, null, null, null);
            }

            var session = _activeAbilityGames[channelId];
            userAnswer = userAnswer.Trim();

            // 檢查答案（支援中英文，不區分大小寫）
            bool isCorrect = await CheckAbilityAnswerAsync(session.SelectedAbility, userAnswer);

            if (isCorrect)
            {
                // 答對了 - 移除遊戲狀態
                _activeAbilityGames.Remove(channelId);
                return (true, null, session.SelectedAbility.displayName, session.CorrectAgent.displayName);
            }
            else
            {
                // 答錯了 - 遊戲繼續
                return (false, userAnswer, null, null);
            }
        }

        /// <summary>
        /// 取得遊戲狀態（用於儲存訊息ID）
        /// </summary>
        public void SetMessageId(ulong id, ulong messageId, bool isImageMode)
        {
            if (isImageMode && _activeImageGames.ContainsKey(id))
            {
                _activeImageGames[id].MessageId = messageId;
            }
            else if (!isImageMode && _activeAbilityGames.ContainsKey(id))
            {
                _activeAbilityGames[id].MessageId = messageId;
            }
        }

        /// <summary>
        /// 檢查角色答案（支援中英文）
        /// </summary>
        private async Task<bool> CheckAgentAnswerAsync(ValorantAgent agent, string userAnswer)
        {
            // 先確保有最新的 API 資料（如果 _nameMapping 為空）
            if (_nameMapping.Count == 0)
            {
                await LoadNameMappingAsync();
            }

            // 直接比對中文名（API displayName）
            if (string.Equals(userAnswer, agent.displayName, StringComparison.OrdinalIgnoreCase))
                return true;

            // 嘗試從映射表找英文名
            if (_nameMapping.TryGetValue(agent.displayName, out string? englishName))
            {
                if (string.Equals(userAnswer, englishName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 檢查技能答案（支援中英文）
        /// </summary>
        private async Task<bool> CheckAbilityAnswerAsync(ValorantAbility ability, string userAnswer)
        {
            // 先確保有最新的 API 資料
            if (_nameMapping.Count == 0)
            {
                await LoadNameMappingAsync();
            }

            // 直接比對中文名
            if (string.Equals(userAnswer, ability.displayName, StringComparison.OrdinalIgnoreCase))
                return true;

            // 嘗試從映射表找英文名
            if (_nameMapping.TryGetValue(ability.displayName, out string? englishName))
            {
                if (string.Equals(userAnswer, englishName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 載入中英文名稱映射（從 API 動態獲取）
        /// </summary>
        private async Task LoadNameMappingAsync()
        {
            try
            {                // 取得中文版（zh-TW）
                var zhResponse = await _httpClient.GetAsync("https://valorant-api.com/v1/agents?language=zh-TW");
                var zhContent = await zhResponse.Content.ReadAsStringAsync();
                var zhData = JsonConvert.DeserializeObject<ValorantAgentsResponse>(zhContent);

                // 取得英文版（en-US）
                var enResponse = await _httpClient.GetAsync("https://valorant-api.com/v1/agents?language=en-US");
                var enContent = await enResponse.Content.ReadAsStringAsync();
                var enData = JsonConvert.DeserializeObject<ValorantAgentsResponse>(enContent);

                // 建立映射表 (中文 -> 英文)
                _nameMapping.Clear();
                foreach (var zhAgent in zhData.data)
                {
                    var enAgent = enData.data.FirstOrDefault(a => a.uuid == zhAgent.uuid);
                    if (enAgent != null)
                    {
                        // 角色名稱
                        _nameMapping[zhAgent.displayName] = enAgent.displayName;

                        // 技能名稱
                        if (zhAgent.abilities != null && enAgent.abilities != null)
                        {
                            for (int i = 0; i < zhAgent.abilities.Count && i < enAgent.abilities.Count; i++)
                            {
                                var zhAbility = zhAgent.abilities[i];
                                var enAbility = enAgent.abilities[i];
                                if (!string.IsNullOrEmpty(zhAbility.displayName) && !string.IsNullOrEmpty(enAbility.displayName))
                                {
                                    _nameMapping[zhAbility.displayName] = enAbility.displayName;
                                }
                            }
                        }
                    }
                }

                Console.WriteLine($"[ValorantService] 已載入 {_nameMapping.Count} 組中英文名稱映射");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ValorantService] 載入名稱映射失敗: {ex.Message}");
            }
        }


        public async Task<(string weaponName, string skinName, string skinImageUrl)> RandomWeaponSkin(string name)
        {
            try
            {
                // 取得中文版（zh-TW）
                var zhResponse = await _httpClient.GetAsync("https://valorant-api.com/v1/weapons?language=zh-TW");
                var zhContent = await zhResponse.Content.ReadAsStringAsync();
                var zhData = JsonConvert.DeserializeObject<ValorantWeaponResponse>(zhContent);

                if (zhData == null || zhData.data == null || zhData.data.Count == 0)
                {
                    Console.WriteLine("[ValorantService] 無法取得武器資料");
                    return (null, null, null);
                }

                Random random = new Random();
                ValorantWeapon weapon = null;

                // 如果有指定武器名稱，先嘗試找該武器
                if (!string.IsNullOrWhiteSpace(name))
                {
                    weapon = zhData.data.FirstOrDefault(w => w.displayName.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (weapon == null)
                    {
                        Console.WriteLine($"[ValorantService] 找不到武器: {name}，改為隨機選擇");
                    }
                }

                // 如果沒有指定武器或找不到，就隨機選一把
                if (weapon == null)
                {
                    // 過濾掉沒有皮膚的武器
                    var weaponsWithSkins = zhData.data.Where(w => w.skins != null && w.skins.Count > 0).ToList();

                    if (weaponsWithSkins.Count == 0)
                    {
                        Console.WriteLine("[ValorantService] 沒有可用的武器皮膚");
                        return (null, null, null);
                    }

                    weapon = weaponsWithSkins[random.Next(weaponsWithSkins.Count)];
                }

                // 檢查該武器是否有皮膚
                if (weapon.skins == null || weapon.skins.Count == 0)
                {
                    Console.WriteLine($"[ValorantService] 武器 {weapon.displayName} 沒有可用的皮膚");
                    return (null, null, null);
                }

                // 隨機選擇一個皮膚
                var randomSkin = weapon.skins[random.Next(weapon.skins.Count)];

                // 確保皮膚有圖片
                if (string.IsNullOrEmpty(randomSkin.displayIcon))
                {
                    // 如果這個皮膚沒圖，再試一次
                    var skinsWithIcon = weapon.skins.Where(s => !string.IsNullOrEmpty(s.displayIcon)).ToList();

                    if (skinsWithIcon.Count == 0)
                    {
                        Console.WriteLine($"[ValorantService] 武器 {weapon.displayName} 的皮膚都沒有圖片");
                        return (null, null, null);
                    }

                    randomSkin = skinsWithIcon[random.Next(skinsWithIcon.Count)];
                }

                Console.WriteLine($"[ValorantService] 隨機武器造型: {weapon.displayName} - {randomSkin.displayName}");
                return (weapon.displayName, randomSkin.displayName, randomSkin.displayIcon);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ValorantService] RandomWeaponSkin 失敗: {ex.Message}");
                return (null, null, null);
            }
        }
        #endregion

    }
}
