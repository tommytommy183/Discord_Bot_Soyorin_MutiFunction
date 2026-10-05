using Discord;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class LOLService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        private const string DdragonBase = "https://ddragon.leagueoflegends.com";

        // region code → (platform, matchRegional, accountRegional)
        // matchRegional:   /lol/match/v5/ routing
        // accountRegional: /riot/account/v1/ routing（TW 帳號 API 只在 asia 可查，match 用 sea）
        private static (string Platform, string MatchRegional, string AccountRegional) GetEndpoints(string region) => (region ?? "tw").ToLower() switch
        {
            "kr"   => ("https://kr.api.riotgames.com",   "https://asia.api.riotgames.com",     "https://asia.api.riotgames.com"),
            "jp"   => ("https://jp1.api.riotgames.com",  "https://asia.api.riotgames.com",     "https://asia.api.riotgames.com"),
            "sg"   => ("https://sg2.api.riotgames.com",  "https://sea.api.riotgames.com",      "https://asia.api.riotgames.com"),
            "na"   => ("https://na1.api.riotgames.com",  "https://americas.api.riotgames.com", "https://americas.api.riotgames.com"),
            "euw"  => ("https://euw1.api.riotgames.com", "https://europe.api.riotgames.com",   "https://europe.api.riotgames.com"),
            "eune" => ("https://eun1.api.riotgames.com", "https://europe.api.riotgames.com",   "https://europe.api.riotgames.com"),
            "oce"  => ("https://oc1.api.riotgames.com",  "https://americas.api.riotgames.com", "https://americas.api.riotgames.com"),
            _      => ("https://tw2.api.riotgames.com",  "https://sea.api.riotgames.com",      "https://asia.api.riotgames.com"),  // tw / 預設
        };

        // Discord ID → PUUID
        public static readonly Dictionary<ulong, string> FriendsPuuid = new()
        {
            { 415032840925741056UL, "jBDEyQij_banYooUWZph_QHX7K-LC5MzCE0cMoeo4vSFpKFtqpUffBO0d2eNy_b1JB16VHJHqB4Z1Q" },
            { 540922644267270154UL, "WE_uzNuYx4oYgGAt3q89u_P_7H6CVDtXbIKbuXnc-YDVvBiSDB4U1kFEx8POJ2zqUM0EIEgGNPmVSw" },
            { 325482625127153664UL, "Dcy9asOLYTrAbYaT_1IhFSRYfMIPHJwygtXgUwLUqUKP-Gauvekr6kihPechxIuj4PKnhNTKqswCoQ" },
            { 404439235290988544UL, "IuTS7BSjJMHpxpAI2xu6VCO8fz_CEpYdDNLQyhvtn-F2A-vt4qcxmXEkcaM_0swUmIQ6YLHQbyTxRg" },
            { 541105947435859978, "16dyB1qKIS_aStgZUhFvqTrnzk9rywVRErq28Rge-z8tmxjFRbvu3JOYLnlM5b29PAneS9RBjJiIeQ" },
        };

        // Monitored for rank losses (Soyo taunts)
        public static readonly HashSet<ulong> MonitoredDiscordIds = new()
        {
            325482625127153664UL,
            404439235290988544UL,
            541105947435859978,
        };

        private readonly Dictionary<string, (string Id, string Name, int Level)> _summonerCache = new();
        private readonly Dictionary<ulong, RankSnapshot> _lastRankSnapshot = new();

        // Match list cache：key = first 14 alphanum chars of puuid
        private static readonly Dictionary<string, MatchListCache> _matchCache = new();
        private record MatchListCache(string GameName, string TagLine, int Level,
            RankEntry SoloEntry, RankEntry FlexEntry,
            List<MatchDetail> Matches,
            List<(List<ParticipantInfo> Blue, List<ParticipantInfo> Red)> Teams);
        private record MatchWithTeams(MatchDetail Detail, List<ParticipantInfo> Blue, List<ParticipantInfo> Red, string PlayerGameName = null, string PlayerTagLine = null);

        private static string MakeCacheKey(string puuid)
            => new string(puuid.Where(char.IsLetterOrDigit).Take(14).ToArray());

        public LOLService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _httpClient.DefaultRequestHeaders.Add("X-Riot-Token", apiKey);
        }

        #region Riot API helpers

        private async Task<JsonElement?> GetJsonAsync(string url)
        {
            var resp = await _httpClient.GetAsync(url);
            Console.WriteLine($"[LOLService] {(int)resp.StatusCode} {url[..Math.Min(100, url.Length)]}");
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        public async Task<string> GetPuuidByRiotIdAsync(string gameName, string tagLine, string region = "tw")
        {
            var (_, _, accountRegional) = GetEndpoints(region);
            var url = $"{accountRegional}/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}";
            var json = await GetJsonAsync(url);
            return json?.TryGetProperty("puuid", out var p) == true ? p.GetString() : null;
        }

        private async Task<(string GameName, string TagLine)> GetAccountByPuuidAsync(string puuid, string regional)
        {
            var url = $"{regional}/riot/account/v1/accounts/by-puuid/{Uri.EscapeDataString(puuid)}";
            var json = await GetJsonAsync(url);
            if (json == null) return ("?", "?");
            var gameName = json.Value.TryGetProperty("gameName", out var g) ? g.GetString() : "?";
            var tagLine = json.Value.TryGetProperty("tagLine", out var t) ? t.GetString() : "?";
            return (gameName ?? "?", tagLine ?? "?");
        }

        private async Task<int> GetSummonerLevelAsync(string puuid, string platform)
        {
            var url = $"{platform}/lol/summoner/v4/summoners/by-puuid/{Uri.EscapeDataString(puuid)}";
            var json = await GetJsonAsync(url);
            if (json == null) return 0;
            return json.Value.TryGetProperty("summonerLevel", out var lv) ? lv.GetInt32() : 0;
        }

        private async Task<List<RankEntry>> GetRankEntriesAsync(string puuid, string platform)
        {
            var url = $"{platform}/lol/league/v4/entries/by-puuid/{Uri.EscapeDataString(puuid)}";
            var json = await GetJsonAsync(url);
            if (json == null || json.Value.ValueKind != JsonValueKind.Array) return new();
            var list = new List<RankEntry>();
            foreach (var entry in json.Value.EnumerateArray())
            {
                var queueType = entry.TryGetProperty("queueType", out var q) ? q.GetString() : "";
                var tier = entry.TryGetProperty("tier", out var t) ? t.GetString() : "UNRANKED";
                var rank = entry.TryGetProperty("rank", out var r) ? r.GetString() : "";
                var lp = entry.TryGetProperty("leaguePoints", out var l) ? l.GetInt32() : 0;
                var wins = entry.TryGetProperty("wins", out var w) ? w.GetInt32() : 0;
                var losses = entry.TryGetProperty("losses", out var lo) ? lo.GetInt32() : 0;
                list.Add(new RankEntry(queueType, tier, rank, lp, wins, losses));
            }
            return list;
        }

        private async Task<List<string>> GetMatchIdsAsync(string puuid, string regional, int count = 10, int? queueId = null)
        {
            var qParam = queueId.HasValue ? $"&queue={queueId}" : "";
            var url = $"{regional}/lol/match/v5/matches/by-puuid/{Uri.EscapeDataString(puuid)}/ids?count={count}{qParam}";
            var json = await GetJsonAsync(url);
            if (json == null || json.Value.ValueKind != JsonValueKind.Array) return new();
            return json.Value.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).ToList();
        }

        private async Task<MatchWithTeams> GetMatchWithTeamsAsync(string matchId, string puuid, string regional)
        {
            var url = $"{regional}/lol/match/v5/matches/{Uri.EscapeDataString(matchId)}";
            var json = await GetJsonAsync(url);
            if (json == null) return null;
            try
            {
                var info     = json.Value.GetProperty("info");
                var queueId  = info.TryGetProperty("queueId",          out var qi)  ? qi.GetInt32()  : 0;
                var duration = info.TryGetProperty("gameDuration",      out var gd)  ? gd.GetInt32()  : 0;
                var gameEnd  = info.TryGetProperty("gameEndTimestamp",  out var gete) ? gete.GetInt64() : 0L;

                MatchDetail myDetail = null;
                string playerGameName = null, playerTagLine = null;
                var blueTeam = new List<ParticipantInfo>();
                var redTeam  = new List<ParticipantInfo>();

                foreach (var p in info.GetProperty("participants").EnumerateArray())
                {
                    var pPuuid  = p.TryGetProperty("puuid", out var ppv) ? ppv.GetString() : "";
                    var isMe    = pPuuid == puuid;
                    var pName   = p.TryGetProperty("riotIdGameName", out var rn) && !string.IsNullOrEmpty(rn.GetString())
                                    ? rn.GetString()
                                    : (p.TryGetProperty("summonerName", out var sn) ? sn.GetString() : "?");
                    var champ   = p.TryGetProperty("championName",              out var cn) ? cn.GetString() : "?";
                    var kills   = p.TryGetProperty("kills",                     out var k)  ? k.GetInt32()  : 0;
                    var deaths  = p.TryGetProperty("deaths",                    out var d)  ? d.GetInt32()  : 0;
                    var assists = p.TryGetProperty("assists",                   out var a)  ? a.GetInt32()  : 0;
                    var cs      = p.TryGetProperty("totalMinionsKilled",        out var csv) ? csv.GetInt32() : 0;
                    var jungle  = p.TryGetProperty("neutralMinionsKilled",      out var jv)  ? jv.GetInt32()  : 0;
                    var dmg     = p.TryGetProperty("totalDamageDealtToChampions", out var dv) ? dv.GetInt32() : 0;
                    var win     = p.TryGetProperty("win",                       out var w)  && w.GetBoolean();
                    var teamId  = p.TryGetProperty("teamId",                    out var ti)  ? ti.GetInt32() : 100;
                    var lane    = p.TryGetProperty("teamPosition",              out var lv)  && !string.IsNullOrEmpty(lv.GetString())
                                    ? lv.GetString()
                                    : (p.TryGetProperty("individualPosition",  out var iv) ? iv.GetString() : "");

                    var pi = new ParticipantInfo(pName ?? "?", champ, kills, deaths, assists, dmg, cs + jungle, win, isMe, lane);
                    if (teamId == 100) blueTeam.Add(pi); else redTeam.Add(pi);

                    if (isMe)
                    {
                        // Extract name from match data as fallback
                        playerGameName = p.TryGetProperty("riotIdGameName", out var mgn) && !string.IsNullOrEmpty(mgn.GetString()) ? mgn.GetString() : null;
                        playerTagLine  = p.TryGetProperty("riotIdTagline",  out var mtl) && !string.IsNullOrEmpty(mtl.GetString()) ? mtl.GetString() : null;

                        var vision  = p.TryGetProperty("visionScore",               out var vv) ? vv.GetInt32() : 0;
                        var gold    = p.TryGetProperty("goldEarned",                out var gv) ? gv.GetInt32() : 0;
                        var doubles = p.TryGetProperty("doubleKills",               out var dk) ? dk.GetInt32() : 0;
                        var triples = p.TryGetProperty("tripleKills",               out var tk) ? tk.GetInt32() : 0;
                        var quadras = p.TryGetProperty("quadraKills",               out var qk) ? qk.GetInt32() : 0;
                        var pentas  = p.TryGetProperty("pentaKills",                out var pk) ? pk.GetInt32() : 0;
                        var objDmg  = p.TryGetProperty("damageDealtToObjectives",   out var ov) ? ov.GetInt32() : 0;
                        var turrets = p.TryGetProperty("turretKills",               out var tv) ? tv.GetInt32() : 0;
                        var heal    = p.TryGetProperty("totalHeal",                 out var hv) ? hv.GetInt32() : 0;
                        myDetail = new MatchDetail(
                            matchId, queueId, QueueIdToName(queueId), champ,
                            kills, deaths, assists, win, cs + jungle, duration / 60,
                            dmg, vision, gold, LaneEmoji(lane),
                            doubles, triples, quadras, pentas, objDmg, turrets, heal, gameEnd
                        );
                    }
                }
                if (myDetail == null) return null;
                return new MatchWithTeams(myDetail, blueTeam, redTeam, playerGameName, playerTagLine);
            }
            catch { }
            return null;
        }

        // Keep compat for loss monitor (only needs MatchDetail)
        private async Task<MatchDetail> GetMatchDetailAsync(string matchId, string puuid, string regional)
        {
            var result = await GetMatchWithTeamsAsync(matchId, puuid, regional);
            return result?.Detail;
        }

        private static string LaneEmoji(string lane) => (lane ?? "").ToUpper() switch
        {
            "TOP"     => "🛡️",
            "JUNGLE"  => "🌿",
            "MIDDLE"  => "⚡",
            "BOTTOM"  => "🏹",
            "UTILITY" => "💊",
            _         => "❓",
        };

        private static string QueueIdToName(int queueId) => queueId switch
        {
            420 => "單排",
            440 => "彈性",
            430 => "一般",
            450 => "克漢",
            700 => "競技",
            _ => "其他"
        };

        private static string TierEmoji(string tier) => (tier ?? "").ToUpper() switch
        {
            "IRON" => "⚫",
            "BRONZE" => "🟤",
            "SILVER" => "⬜",
            "GOLD" => "🟡",
            "PLATINUM" => "🩵",
            "EMERALD" => "💚",
            "DIAMOND" => "💎",
            "MASTER" => "🔮",
            "GRANDMASTER" => "🔴",
            "CHALLENGER" => "👑",
            _ => "❓"
        };

        private static string FormatRank(string tier, string rank, int lp, int wins, int losses)
        {
            if (string.IsNullOrEmpty(tier) || tier == "UNRANKED") return "未定位";
            string rankStr = (tier.ToUpper() is "MASTER" or "GRANDMASTER" or "CHALLENGER") ? "" : $" {rank}";
            int total = wins + losses;
            float wr = total > 0 ? (float)wins / total * 100 : 0;
            return $"{TierEmoji(tier)} {tier}{rankStr}　{lp} LP　{wins}W/{losses}L（{wr:F0}%）";
        }

        #endregion

        #region Public stats methods

        public async Task<(string TextForAI, Embed Embed, ComponentBuilder Component, string CacheKey)> GetPlayerStatsAsync(string puuid, string region = "tw")
        {
            try
            {
            var (platform, matchRegional, accountRegional) = GetEndpoints(region);
            Console.WriteLine($"[LOLService] GetPlayerStats puuid={puuid[..20]}... platform={platform} match={matchRegional} account={accountRegional}");
            var (gameName, tagLine) = await GetAccountByPuuidAsync(puuid, accountRegional);
            var level = await GetSummonerLevelAsync(puuid, platform);
            Console.WriteLine($"[LOLService] account={gameName}#{tagLine} level={level}");

            var rankEntries = await GetRankEntriesAsync(puuid, platform);
            Console.WriteLine($"[LOLService] rankEntries count={rankEntries.Count}");
            var soloEntry = rankEntries.FirstOrDefault(e => e.Queue == "RANKED_SOLO_5x5");
            var flexEntry = rankEntries.FirstOrDefault(e => e.Queue == "RANKED_FLEX_SR");

            // Fetch recent 20 matches (all queues)
            var matchIds = await GetMatchIdsAsync(puuid, matchRegional, 20);
            Console.WriteLine($"[LOLService] matchIds count={matchIds.Count}");
            var matchTasks = matchIds.Select(id => GetMatchWithTeamsAsync(id, puuid, matchRegional)).ToArray();
            await Task.WhenAll(matchTasks);
            var matchResults = matchTasks.Select(t => t.Result).Where(m => m != null).ToList();
            var matches = matchResults.Select(m => m.Detail).ToList();
            var teams   = matchResults.Select(m => (m.Blue, m.Red)).ToList();
            Console.WriteLine($"[LOLService] matches parsed={matches.Count}");

            // Fallback: use name from first match if account API returned "?"
            if ((gameName == "?" || string.IsNullOrEmpty(gameName)) && matchResults.Count > 0)
            {
                var first = matchResults[0];
                if (!string.IsNullOrEmpty(first.PlayerGameName)) gameName = first.PlayerGameName;
                if (!string.IsNullOrEmpty(first.PlayerTagLine))  tagLine  = first.PlayerTagLine;
            }

            // Store in cache
            var cacheKey = MakeCacheKey(puuid);
            _matchCache[cacheKey] = new MatchListCache(gameName, tagLine, level, soloEntry, flexEntry, matches, teams);

            var (embed, component) = BuildOverviewEmbed(gameName, tagLine, level, soloEntry, flexEntry, matches, cacheKey);

            // Text for AI
            var displayName = $"{gameName}#{tagLine}";
            var sb = new StringBuilder();
            sb.AppendLine($"召喚師：{displayName}（等級 {level}）");
            if (soloEntry != null)
                sb.AppendLine($"單排：{FormatRank(soloEntry.Tier, soloEntry.Rank, soloEntry.LP, soloEntry.Wins, soloEntry.Losses)}");
            if (flexEntry != null)
                sb.AppendLine($"彈性：{FormatRank(flexEntry.Tier, flexEntry.Rank, flexEntry.LP, flexEntry.Wins, flexEntry.Losses)}");
            if (matches.Count > 0)
            {
                var totalWins = matches.Count(m => m.Win);
                double avgK = matches.Average(m => m.Kills), avgD = matches.Average(m => m.Deaths), avgA = matches.Average(m => m.Assists);
                string kdaRatio = avgD > 0 ? $"{(avgK + avgA) / avgD:F2}" : "Perfect";
                sb.AppendLine($"近{matches.Count}場：{totalWins}勝{matches.Count - totalWins}敗（{totalWins * 100 / matches.Count}%）KDA {avgK:F1}/{avgD:F1}/{avgA:F1}={kdaRatio}");
            }
            foreach (var m in matches)
                sb.AppendLine($"{(m.Win ? "勝" : "敗")} {m.QueueName} {m.LaneEmoji}{m.Champion} {m.Kills}/{m.Deaths}/{m.Assists} {m.CS}cs {m.Damage / 1000:F1}k傷 {m.DurationMin}min");

            return (sb.ToString().TrimEnd(), embed, component, cacheKey);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOLService] GetPlayerStats EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return (null, null, null, null);
            }
        }

        private static (Embed embed, ComponentBuilder component) BuildOverviewEmbed(
            string gameName, string tagLine, int level,
            RankEntry soloEntry, RankEntry flexEntry, List<MatchDetail> matches, string cacheKey)
        {
            var displayName = $"{gameName}#{tagLine}";
            var eb = new EmbedBuilder()
                .WithTitle($"🎮  {displayName}")
                .WithColor(new Color(0xC89B3C))
                .WithFooter($"LOL 戰績 · Riot API · 等級 {level}");

            eb.AddField("🗡️ 單排積分",
                soloEntry != null ? FormatRank(soloEntry.Tier, soloEntry.Rank, soloEntry.LP, soloEntry.Wins, soloEntry.Losses) : "未定位",
                inline: true);
            eb.AddField("⚔️ 彈性積分",
                flexEntry != null ? FormatRank(flexEntry.Tier, flexEntry.Rank, flexEntry.LP, flexEntry.Wins, flexEntry.Losses) : "未定位",
                inline: true);

            var component = new ComponentBuilder();

            if (matches.Count > 0)
            {
                int totalWins   = matches.Count(m => m.Win);
                int totalGames  = matches.Count;
                double avgKills   = matches.Average(m => m.Kills);
                double avgDeaths  = matches.Average(m => m.Deaths);
                double avgAssists = matches.Average(m => m.Assists);
                double avgDmg     = matches.Average(m => m.Damage);
                double avgVision  = matches.Average(m => m.VisionScore);
                double avgCS      = matches.Average(m => m.CS);
                string kdaRatio   = avgDeaths > 0 ? $"{(avgKills + avgAssists) / avgDeaths:F2}" : "Perfect";
                var mostChamp = matches.GroupBy(m => m.Champion).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "-";

                var winRate = totalWins * 100 / totalGames;
                var summary = $"**{totalWins}勝 {totalGames - totalWins}敗**（{winRate}%）　KDA **{avgKills:F1}/{avgDeaths:F1}/{avgAssists:F1}** = **{kdaRatio}**\n" +
                              $"平均傷害：{avgDmg / 1000:F1}k　視野分：{avgVision:F1}　CS/場：{avgCS:F0}　最常玩：{mostChamp}";
                eb.AddField($"📊 近 {totalGames} 場統計", summary, inline: false);

                var matchLines = matches.Select((m, i) =>
                {
                    double kda = m.Deaths > 0 ? (m.Kills + m.Assists) / (double)m.Deaths : m.Kills + m.Assists;
                    return $"`#{i + 1}` {(m.Win ? "🟢" : "🔴")} `{m.QueueName}` {m.LaneEmoji}**{m.Champion}**　" +
                           $"{m.Kills}/{m.Deaths}/{m.Assists} ({kda:F1})　" +
                           $"{m.CS}cs　{m.Damage / 1000:F1}k傷　👁{m.VisionScore}　{m.DurationMin}min";
                }).ToList();

                // Split into two fields to stay under Discord's 1024-char field limit
                var half = (matchLines.Count + 1) / 2;
                eb.AddField("📋 對局紀錄（點按鈕查詳情）", string.Join("\n", matchLines.Take(half)), inline: false);
                if (matchLines.Count > half)
                    eb.AddField("📋 對局紀錄（續）", string.Join("\n", matchLines.Skip(half)), inline: false);

                // Add buttons (max 25 = 5 rows × 5): each match gets a button
                for (int i = 0; i < Math.Min(matches.Count, 25); i++)
                {
                    var m = matches[i];
                    var champ = m.Champion.Length > 6 ? m.Champion[..6] : m.Champion;
                    var label = $"{(m.Win ? "✅" : "❌")}#{i + 1} {champ}";
                    component.WithButton(label, $"lol_match_{cacheKey}_{i}", ButtonStyle.Secondary, row: i / 5);
                }
            }
            else
            {
                eb.AddField("📋 近期對局", "Riot API 未回傳近期對局資料（可能近期未出賽）", inline: false);
            }

            return (eb.Build(), component);
        }

        public (Embed embed, ComponentBuilder component) GetMatchDetailEmbed(string cacheKey, int matchIndex)
        {
            if (!_matchCache.TryGetValue(cacheKey, out var cache))
                return (new EmbedBuilder().WithTitle("❌ 查詢已過期").WithDescription("請重新執行指令").WithColor(Color.Red).Build(), new ComponentBuilder());

            if (matchIndex < 0 || matchIndex >= cache.Matches.Count)
                return (new EmbedBuilder().WithTitle("❌ 對局不存在").WithColor(Color.Red).Build(), new ComponentBuilder());

            var m = cache.Matches[matchIndex];
            double kda = m.Deaths > 0 ? (m.Kills + m.Assists) / (double)m.Deaths : m.Kills + m.Assists;

            string multiKill = m.PentaKills > 0 ? "🏆 **PENTA KILL**" :
                               m.QuadraKills > 0 ? "🔥 **QUADRA KILL**" :
                               m.TripleKills > 0 ? "⚡ **TRIPLE KILL**" :
                               m.DoubleKills > 0 ? "✨ Double Kill" : "";

            string timeAgo = "";
            if (m.GameEndTimestamp > 0)
            {
                var diff = DateTimeOffset.Now - DateTimeOffset.FromUnixTimeMilliseconds(m.GameEndTimestamp).ToLocalTime();
                timeAgo = diff.TotalDays >= 1 ? $"{(int)diff.TotalDays}天前" :
                          diff.TotalHours >= 1 ? $"{(int)diff.TotalHours}小時前" :
                          $"{(int)diff.TotalMinutes}分鐘前";
            }

            var eb = new EmbedBuilder()
                .WithTitle($"{(m.Win ? "🟢 勝利" : "🔴 失敗")} — {m.LaneEmoji} {m.Champion}")
                .WithDescription($"`{m.QueueName}` · {m.DurationMin} 分鐘 · {timeAgo}{(multiKill != "" ? $"\n{multiKill}" : "")}")
                .WithColor(m.Win ? new Color(0x57AB27) : new Color(0xDA373C))
                .WithFooter($"{cache.GameName}#{cache.TagLine} · 第 {matchIndex + 1} 場");

            eb.AddField("⚔️ KDA",
                $"**{m.Kills} / {m.Deaths} / {m.Assists}**\nKDA 比：**{kda:F2}**",
                inline: true);
            eb.AddField("🗡️ 傷害",
                $"對英雄：**{m.Damage / 1000:F1}k**\n對目標：{m.ObjDamage / 1000:F1}k",
                inline: true);
            eb.AddField("💰 資源",
                $"CS：**{m.CS}**\n金幣：{m.Gold / 1000:F1}k",
                inline: true);
            eb.AddField("👁️ 視野", $"視野分：**{m.VisionScore}**", inline: true);
            if (m.TurretKills > 0)
                eb.AddField("🏰 推塔", $"**{m.TurretKills}** 座", inline: true);
            if (m.TotalHeal > 500)
                eb.AddField("💚 治療", $"{m.TotalHeal / 1000:F1}k", inline: true);

            // Team compositions
            if (matchIndex < cache.Teams.Count)
            {
                var (blue, red) = cache.Teams[matchIndex];

                string TeamLine(ParticipantInfo p)
                {
                    double pkda = p.Deaths > 0 ? (p.Kills + p.Assists) / (double)p.Deaths : p.Kills + p.Assists;
                    var nameShort = p.Name.Length > 12 ? p.Name[..12] : p.Name;
                    return $"{(p.IsMe ? "▶ " : "")}{LaneEmoji(p.Lane)}**{p.Champion}**  " +
                           $"{p.Kills}/{p.Deaths}/{p.Assists} ({pkda:F1})  " +
                           $"{p.CS}cs  {p.Damage / 1000:F1}k傷  `{nameShort}`";
                }

                if (blue.Count > 0)
                    eb.AddField($"{(blue[0].Win ? "🔵🏆 藍方（勝）" : "🔵 藍方")}",
                        string.Join("\n", blue.Select(TeamLine)), inline: false);
                if (red.Count > 0)
                    eb.AddField($"{(red[0].Win ? "🔴🏆 紅方（勝）" : "🔴 紅方")}",
                        string.Join("\n", red.Select(TeamLine)), inline: false);
            }

            var component = new ComponentBuilder()
                .WithButton("← 返回戰績列表", $"lol_back_{cacheKey}", ButtonStyle.Secondary);

            return (eb.Build(), component);
        }

        public (Embed embed, ComponentBuilder component) GetOverviewEmbed(string cacheKey)
        {
            if (!_matchCache.TryGetValue(cacheKey, out var cache))
                return (new EmbedBuilder().WithTitle("❌ 查詢已過期").WithDescription("請重新執行指令").WithColor(Color.Red).Build(), new ComponentBuilder());

            return BuildOverviewEmbed(cache.GameName, cache.TagLine, cache.Level, cache.SoloEntry, cache.FlexEntry, cache.Matches, cacheKey);
        }

        #endregion

        #region Loss monitoring

        public async Task<List<LossEvent>> CheckForLossesAsync()
        {
            var (platform, matchRegional, accountRegional) = GetEndpoints("tw"); // 監控的朋友都是 TW
            var results = new List<LossEvent>();
            foreach (var (discordId, puuid) in FriendsPuuid.Where(kv => MonitoredDiscordIds.Contains(kv.Key)))
            {
                try
                {
                    var (sumName, _) = await GetAccountByPuuidAsync(puuid, accountRegional);
                    var entries = await GetRankEntriesAsync(puuid, platform);
                    var solo = entries.FirstOrDefault(e => e.Queue == "RANKED_SOLO_5x5");
                    var flex = entries.FirstOrDefault(e => e.Queue == "RANKED_FLEX_SR");

                    var current = new RankSnapshot(
                        solo?.LP ?? 0, solo != null ? $"{solo.Tier} {solo.Rank}" : "UNRANKED",
                        flex?.LP ?? 0, flex != null ? $"{flex.Tier} {flex.Rank}" : "UNRANKED"
                    );

                    if (_lastRankSnapshot.TryGetValue(discordId, out var last))
                    {
                        if (solo != null && current.SoloLP < last.SoloLP)
                        {
                            int diff = last.SoloLP - current.SoloLP;
                            var champ = await GetLastRankedChampAsync(puuid, matchRegional, 420);
                            results.Add(new LossEvent(discordId, sumName, "單排", current.SoloFull, diff, champ));
                        }
                        if (flex != null && current.FlexLP < last.FlexLP)
                        {
                            int diff = last.FlexLP - current.FlexLP;
                            var champ = await GetLastRankedChampAsync(puuid, matchRegional, 440);
                            results.Add(new LossEvent(discordId, sumName, "彈性", current.FlexFull, diff, champ));
                        }
                    }

                    _lastRankSnapshot[discordId] = current;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LOLService] CheckForLosses {discordId}: {ex.Message}");
                }
            }
            return results;
        }

        private async Task<string> GetLastRankedChampAsync(string puuid, string regional, int queueId)
        {
            try
            {
                var ids = await GetMatchIdsAsync(puuid, regional, 1, queueId);
                if (ids.Count == 0) return null;
                var match = await GetMatchDetailAsync(ids[0], puuid, regional);
                return match?.Champion;
            }
            catch { return null; }
        }

        #endregion
    }

    public record ParticipantInfo(string Name, string Champion, int Kills, int Deaths, int Assists, int Damage, int CS, bool Win, bool IsMe, string Lane);
    public record RankEntry(string Queue, string Tier, string Rank, int LP, int Wins, int Losses);
    public record RankSnapshot(int SoloLP, string SoloFull, int FlexLP, string FlexFull);
    public record LossEvent(ulong DiscordId, string SummonerName, string Queue, string RankFull, int LPLost, string ChampionName);
    public record MatchDetail(string MatchId, int QueueId, string QueueName, string Champion, int Kills, int Deaths, int Assists, bool Win, int CS, int DurationMin, int Damage, int VisionScore, int Gold, string LaneEmoji,
        int DoubleKills = 0, int TripleKills = 0, int QuadraKills = 0, int PentaKills = 0,
        int ObjDamage = 0, int TurretKills = 0, int TotalHeal = 0, long GameEndTimestamp = 0);
}
