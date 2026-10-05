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

        // region code → (platform, regional)
        private static (string Platform, string Regional) GetEndpoints(string region) => (region ?? "tw").ToLower() switch
        {
            "kr"   => ("https://kr.api.riotgames.com",   "https://asia.api.riotgames.com"),
            "jp"   => ("https://jp1.api.riotgames.com",  "https://asia.api.riotgames.com"),
            "sg"   => ("https://sg2.api.riotgames.com",  "https://asia.api.riotgames.com"),
            "na"   => ("https://na1.api.riotgames.com",  "https://americas.api.riotgames.com"),
            "euw"  => ("https://euw1.api.riotgames.com", "https://europe.api.riotgames.com"),
            "eune" => ("https://eun1.api.riotgames.com", "https://europe.api.riotgames.com"),
            "oce"  => ("https://oc1.api.riotgames.com",  "https://americas.api.riotgames.com"),
            _      => ("https://tw2.api.riotgames.com",  "https://sea.api.riotgames.com"),  // tw / 預設
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
            var (_, regional) = GetEndpoints(region);
            var url = $"{regional}/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}";
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

        private async Task<MatchDetail> GetMatchDetailAsync(string matchId, string puuid, string regional)
        {
            var url = $"{regional}/lol/match/v5/matches/{Uri.EscapeDataString(matchId)}";
            var json = await GetJsonAsync(url);
            if (json == null) return null;
            try
            {
                var info = json.Value.GetProperty("info");
                var queueId = info.TryGetProperty("queueId", out var qi) ? qi.GetInt32() : 0;
                var duration = info.TryGetProperty("gameDuration", out var gd) ? gd.GetInt32() : 0;

                foreach (var p in info.GetProperty("participants").EnumerateArray())
                {
                    if (!p.TryGetProperty("puuid", out var pp) || pp.GetString() != puuid) continue;
                    var kills   = p.TryGetProperty("kills",   out var k)  ? k.GetInt32()  : 0;
                    var deaths  = p.TryGetProperty("deaths",  out var d)  ? d.GetInt32()  : 0;
                    var assists = p.TryGetProperty("assists", out var a)  ? a.GetInt32()  : 0;
                    var cs      = p.TryGetProperty("totalMinionsKilled", out var csv) ? csv.GetInt32() : 0;
                    var jungle  = p.TryGetProperty("neutralMinionsKilled", out var jv) ? jv.GetInt32() : 0;
                    var dmg     = p.TryGetProperty("totalDamageDealtToChampions", out var dv) ? dv.GetInt32() : 0;
                    var vision  = p.TryGetProperty("visionScore", out var vv) ? vv.GetInt32() : 0;
                    var gold    = p.TryGetProperty("goldEarned", out var gv) ? gv.GetInt32() : 0;
                    var lane    = p.TryGetProperty("teamPosition", out var lv) && !string.IsNullOrEmpty(lv.GetString())
                                    ? lv.GetString()
                                    : (p.TryGetProperty("individualPosition", out var iv) ? iv.GetString() : "");
                    return new MatchDetail(
                        matchId, queueId, QueueIdToName(queueId),
                        p.TryGetProperty("championName", out var cn) ? cn.GetString() : "?",
                        kills, deaths, assists,
                        p.TryGetProperty("win", out var w) && w.GetBoolean(),
                        cs + jungle, duration / 60,
                        dmg, vision, gold,
                        LaneEmoji(lane)
                    );
                }
            }
            catch { }
            return null;
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

        public async Task<(string TextForAI, Embed Embed)> GetPlayerStatsAsync(string puuid, string region = "tw")
        {
            try
            {
            var (platform, regional) = GetEndpoints(region);
            Console.WriteLine($"[LOLService] GetPlayerStats puuid={puuid[..20]}... platform={platform} regional={regional}");
            var (gameName, tagLine) = await GetAccountByPuuidAsync(puuid, regional);
            var level = await GetSummonerLevelAsync(puuid, platform);
            Console.WriteLine($"[LOLService] account={gameName}#{tagLine} level={level}");

            var rankEntries = await GetRankEntriesAsync(puuid, platform);
            Console.WriteLine($"[LOLService] rankEntries count={rankEntries.Count}");
            var soloEntry = rankEntries.FirstOrDefault(e => e.Queue == "RANKED_SOLO_5x5");
            var flexEntry = rankEntries.FirstOrDefault(e => e.Queue == "RANKED_FLEX_SR");

            // Fetch recent 10 matches (all queues)
            var matchIds = await GetMatchIdsAsync(puuid, regional, 10);
            Console.WriteLine($"[LOLService] matchIds count={matchIds.Count}");
            var matchTasks = matchIds.Select(id => GetMatchDetailAsync(id, puuid, regional)).ToArray();
            await Task.WhenAll(matchTasks);
            var matches = matchTasks.Select(t => t.Result).Where(m => m != null).ToList();
            Console.WriteLine($"[LOLService] matches parsed={matches.Count}");

            var displayName = $"{gameName}#{tagLine}";

            // ── 近場統計 ──
            int totalWins   = matches.Count(m => m.Win);
            int totalGames  = matches.Count;
            double avgKills   = totalGames > 0 ? matches.Average(m => m.Kills)   : 0;
            double avgDeaths  = totalGames > 0 ? matches.Average(m => m.Deaths)  : 0;
            double avgAssists = totalGames > 0 ? matches.Average(m => m.Assists) : 0;
            double avgDmg     = totalGames > 0 ? matches.Average(m => m.Damage)  : 0;
            double avgVision  = totalGames > 0 ? matches.Average(m => m.VisionScore) : 0;
            double avgCS      = totalGames > 0 ? matches.Average(m => m.CS)      : 0;
            string kdaRatio   = avgDeaths > 0 ? $"{(avgKills + avgAssists) / avgDeaths:F2}" : "Perfect";
            var mostChamp = matches.GroupBy(m => m.Champion).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "-";

            // Text for AI
            var sb = new StringBuilder();
            sb.AppendLine($"召喚師：{displayName}（等級 {level}）");
            if (soloEntry != null)
                sb.AppendLine($"單排：{FormatRank(soloEntry.Tier, soloEntry.Rank, soloEntry.LP, soloEntry.Wins, soloEntry.Losses)}");
            if (flexEntry != null)
                sb.AppendLine($"彈性：{FormatRank(flexEntry.Tier, flexEntry.Rank, flexEntry.LP, flexEntry.Wins, flexEntry.Losses)}");
            if (totalGames > 0)
                sb.AppendLine($"近{totalGames}場：{totalWins}勝{totalGames - totalWins}敗（{totalWins * 100 / totalGames}%）KDA {avgKills:F1}/{avgDeaths:F1}/{avgAssists:F1}={kdaRatio} 最常玩:{mostChamp}");
            foreach (var m in matches)
                sb.AppendLine($"{(m.Win ? "勝" : "敗")} {m.QueueName} {m.LaneEmoji}{m.Champion} {m.Kills}/{m.Deaths}/{m.Assists} {m.CS}cs {m.Damage / 1000:F1}k傷害 {m.DurationMin}分鐘");

            // Embed
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

            if (totalGames > 0)
            {
                var winRate = totalWins * 100 / totalGames;
                var summary = $"**{totalWins}勝 {totalGames - totalWins}敗**（{winRate}%）　KDA **{avgKills:F1}/{avgDeaths:F1}/{avgAssists:F1}** = **{kdaRatio}**\n" +
                              $"平均傷害：{avgDmg / 1000:F1}k　視野分：{avgVision:F1}　CS/場：{avgCS:F0}　最常玩：{mostChamp}";
                eb.AddField($"📊 近 {totalGames} 場統計", summary, inline: false);

                var matchLines = matches.Select(m =>
                {
                    double kda = m.Deaths > 0 ? (m.Kills + m.Assists) / (double)m.Deaths : m.Kills + m.Assists;
                    return $"{(m.Win ? "🟢" : "🔴")} `{m.QueueName}` {m.LaneEmoji}**{m.Champion}**　" +
                           $"{m.Kills}/{m.Deaths}/{m.Assists} ({kda:F1})　" +
                           $"{m.CS}cs　{m.Damage / 1000:F1}k傷　👁{m.VisionScore}　{m.DurationMin}min";
                }).ToList();
                eb.AddField($"📋 對局紀錄", string.Join("\n", matchLines), inline: false);
            }
            else
            {
                eb.AddField("📋 近期對局", "Riot API 未回傳近期對局資料（可能近期未出賽）", inline: false);
            }

            return (sb.ToString().TrimEnd(), eb.Build());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOLService] GetPlayerStats EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return (null, null);
            }
        }

        #endregion

        #region Loss monitoring

        public async Task<List<LossEvent>> CheckForLossesAsync()
        {
            var (platform, regional) = GetEndpoints("tw"); // 監控的朋友都是 TW
            var results = new List<LossEvent>();
            foreach (var (discordId, puuid) in FriendsPuuid.Where(kv => MonitoredDiscordIds.Contains(kv.Key)))
            {
                try
                {
                    var (sumName, _) = await GetAccountByPuuidAsync(puuid, regional);
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
                            var champ = await GetLastRankedChampAsync(puuid, regional, 420);
                            results.Add(new LossEvent(discordId, sumName, "單排", current.SoloFull, diff, champ));
                        }
                        if (flex != null && current.FlexLP < last.FlexLP)
                        {
                            int diff = last.FlexLP - current.FlexLP;
                            var champ = await GetLastRankedChampAsync(puuid, regional, 440);
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

    public record RankEntry(string Queue, string Tier, string Rank, int LP, int Wins, int Losses);
    public record RankSnapshot(int SoloLP, string SoloFull, int FlexLP, string FlexFull);
    public record LossEvent(ulong DiscordId, string SummonerName, string Queue, string RankFull, int LPLost, string ChampionName);
    public record MatchDetail(string MatchId, int QueueId, string QueueName, string Champion, int Kills, int Deaths, int Assists, bool Win, int CS, int DurationMin, int Damage, int VisionScore, int Gold, string LaneEmoji);
}
