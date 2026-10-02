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

        private const string PlatformBase = "https://tw2.api.riotgames.com";
        private const string RegionalBase = "https://sea.api.riotgames.com";
        private const string DdragonBase = "https://ddragon.leagueoflegends.com";

        // Discord ID → PUUID
        public static readonly Dictionary<ulong, string> FriendsPuuid = new()
        {
            { 415032840925741056UL, "jBDEyQij_banYooUWZph_QHX7K-LC5MzCE0cMoeo4vSFpKFtqpUffBO0d2eNy_b1JB16VHJHqB4Z1Q" },
            { 540922644267270154UL, "WE_uzNuYx4oYgGAt3q89u_P_7H6CVDtXbIKbuXnc-YDVvBiSDB4U1kFEx8POJ2zqUM0EIEgGNPmVSw" },
            { 325482625127153664UL, "Dcy9asOLYTrAbYaT_1IhFSRYfMIPHJwygtXgUwLUqUKP-Gauvekr6kihPechxIuj4PKnhNTKqswCoQ" },
            { 404439235290988544UL, "IuTS7BSjJMHpxpAI2xu6VCO8fz_CEpYdDNLQyhvtn-F2A-vt4qcxmXEkcaM_0swUmIQ6YLHQbyTxRg" },
        };

        // Monitored for rank losses (Soyo taunts)
        public static readonly HashSet<ulong> MonitoredDiscordIds = new()
        {
            325482625127153664UL,
            404439235290988544UL,
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

        public async Task<string> GetPuuidByRiotIdAsync(string gameName, string tagLine)
        {
            var url = $"{RegionalBase}/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}";
            var json = await GetJsonAsync(url);
            return json?.TryGetProperty("puuid", out var p) == true ? p.GetString() : null;
        }

        private async Task<(string Id, string Name, int Level)> GetSummonerAsync(string puuid)
        {
            if (_summonerCache.TryGetValue(puuid, out var cached)) return cached;
            var url = $"{PlatformBase}/lol/summoner/v4/summoners/by-puuid/{Uri.EscapeDataString(puuid)}";
            var json = await GetJsonAsync(url);
            if (json == null) return (null, null, 0);
            var id = json.Value.TryGetProperty("id", out var i) ? i.GetString() : null;
            var name = json.Value.TryGetProperty("name", out var n) ? n.GetString() : "?";
            var level = json.Value.TryGetProperty("summonerLevel", out var lv) ? lv.GetInt32() : 0;
            var info = (id, name, level);
            if (id != null) _summonerCache[puuid] = info;
            return info;
        }

        private async Task<List<RankEntry>> GetRankEntriesAsync(string summonerId)
        {
            var url = $"{PlatformBase}/lol/league/v4/entries/by-summoner/{Uri.EscapeDataString(summonerId)}";
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

        private async Task<List<string>> GetMatchIdsAsync(string puuid, int count = 10, int? queueId = null)
        {
            var qParam = queueId.HasValue ? $"&queue={queueId}" : "";
            var url = $"{RegionalBase}/lol/match/v5/matches/by-puuid/{Uri.EscapeDataString(puuid)}/ids?count={count}{qParam}";
            var json = await GetJsonAsync(url);
            if (json == null || json.Value.ValueKind != JsonValueKind.Array) return new();
            return json.Value.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).ToList();
        }

        private async Task<MatchDetail> GetMatchDetailAsync(string matchId, string puuid)
        {
            var url = $"{RegionalBase}/lol/match/v5/matches/{Uri.EscapeDataString(matchId)}";
            var json = await GetJsonAsync(url);
            if (json == null) return null;
            try
            {
                var info = json.Value.GetProperty("info");
                var queueId = info.TryGetProperty("queueId", out var qi) ? qi.GetInt32() : 0;
                var duration = info.TryGetProperty("gameDuration", out var gd) ? gd.GetInt32() : 0;
                var gameMode = info.TryGetProperty("gameMode", out var gm) ? gm.GetString() : "?";

                foreach (var p in info.GetProperty("participants").EnumerateArray())
                {
                    if (!p.TryGetProperty("puuid", out var pp) || pp.GetString() != puuid) continue;
                    return new MatchDetail(
                        matchId,
                        queueId,
                        QueueIdToName(queueId),
                        p.TryGetProperty("championName", out var cn) ? cn.GetString() : "?",
                        p.TryGetProperty("kills", out var k) ? k.GetInt32() : 0,
                        p.TryGetProperty("deaths", out var d) ? d.GetInt32() : 0,
                        p.TryGetProperty("assists", out var a) ? a.GetInt32() : 0,
                        p.TryGetProperty("win", out var w) && w.GetBoolean(),
                        p.TryGetProperty("totalMinionsKilled", out var cs) ? cs.GetInt32() : 0,
                        duration / 60
                    );
                }
            }
            catch { }
            return null;
        }

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

        public async Task<(string TextForAI, Embed Embed)> GetPlayerStatsAsync(string puuid)
        {
            var (sumId, sumName, level) = await GetSummonerAsync(puuid);
            if (sumId == null) return (null, null);

            var rankEntries = await GetRankEntriesAsync(sumId);
            var soloEntry = rankEntries.FirstOrDefault(e => e.Queue == "RANKED_SOLO_5x5");
            var flexEntry = rankEntries.FirstOrDefault(e => e.Queue == "RANKED_FLEX_SR");

            // Fetch recent 10 matches (all queues)
            var matchIds = await GetMatchIdsAsync(puuid, 10);
            var matchTasks = matchIds.Select(id => GetMatchDetailAsync(id, puuid)).ToArray();
            await Task.WhenAll(matchTasks);
            var matches = matchTasks.Select(t => t.Result).Where(m => m != null).ToList();

            // Text for AI
            var sb = new StringBuilder();
            sb.AppendLine($"召喚師：{sumName}（等級 {level}）");
            if (soloEntry != null)
                sb.AppendLine($"單排：{FormatRank(soloEntry.Tier, soloEntry.Rank, soloEntry.LP, soloEntry.Wins, soloEntry.Losses)}");
            if (flexEntry != null)
                sb.AppendLine($"彈性：{FormatRank(flexEntry.Tier, flexEntry.Rank, flexEntry.LP, flexEntry.Wins, flexEntry.Losses)}");
            foreach (var m in matches)
                sb.AppendLine($"{(m.Win ? "勝" : "敗")} {m.QueueName} {m.Champion} {m.Kills}/{m.Deaths}/{m.Assists} {m.CS}cs {m.DurationMin}分鐘");

            // Embed
            var eb = new EmbedBuilder()
                .WithTitle($"🎮  {sumName}")
                .WithColor(new Color(0xC89B3C))
                .WithFooter($"LOL 戰績 · Riot API · 等級 {level}");

            eb.AddField("🗡️ 單排積分",
                soloEntry != null ? FormatRank(soloEntry.Tier, soloEntry.Rank, soloEntry.LP, soloEntry.Wins, soloEntry.Losses) : "未定位",
                inline: false);
            eb.AddField("⚔️ 彈性積分",
                flexEntry != null ? FormatRank(flexEntry.Tier, flexEntry.Rank, flexEntry.LP, flexEntry.Wins, flexEntry.Losses) : "未定位",
                inline: false);

            if (matches.Any())
            {
                var matchLines = matches.Select(m =>
                    $"{(m.Win ? "🟢" : "🔴")} [{m.QueueName}] {m.Champion}  {m.Kills}/{m.Deaths}/{m.Assists}  {m.CS}cs  {m.DurationMin}min").ToList();
                eb.AddField($"📋 近 {matches.Count} 場對局", string.Join("\n", matchLines), inline: false);
            }

            return (sb.ToString().TrimEnd(), eb.Build());
        }

        #endregion

        #region Loss monitoring

        public async Task<List<LossEvent>> CheckForLossesAsync()
        {
            var results = new List<LossEvent>();
            foreach (var (discordId, puuid) in FriendsPuuid.Where(kv => MonitoredDiscordIds.Contains(kv.Key)))
            {
                try
                {
                    var (sumId, sumName, _) = await GetSummonerAsync(puuid);
                    if (sumId == null) continue;

                    var entries = await GetRankEntriesAsync(sumId);
                    var solo = entries.FirstOrDefault(e => e.Queue == "RANKED_SOLO_5x5");
                    var flex = entries.FirstOrDefault(e => e.Queue == "RANKED_FLEX_SR");

                    var current = new RankSnapshot(
                        solo?.LP ?? 0, solo != null ? $"{solo.Tier} {solo.Rank}" : "UNRANKED",
                        flex?.LP ?? 0, flex != null ? $"{flex.Tier} {flex.Rank}" : "UNRANKED"
                    );

                    if (_lastRankSnapshot.TryGetValue(discordId, out var last))
                    {
                        // Solo queue loss
                        if (solo != null && current.SoloLP < last.SoloLP)
                        {
                            int diff = last.SoloLP - current.SoloLP;
                            // If rank also changed, we demotion (could be more LP diff)
                            var champ = await GetLastRankedChampAsync(puuid, 420);
                            results.Add(new LossEvent(discordId, sumName, "單排", current.SoloFull, diff, champ));
                        }
                        // Flex queue loss
                        if (flex != null && current.FlexLP < last.FlexLP)
                        {
                            int diff = last.FlexLP - current.FlexLP;
                            var champ = await GetLastRankedChampAsync(puuid, 440);
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

        private async Task<string> GetLastRankedChampAsync(string puuid, int queueId)
        {
            try
            {
                var ids = await GetMatchIdsAsync(puuid, 1, queueId);
                if (ids.Count == 0) return null;
                var match = await GetMatchDetailAsync(ids[0], puuid);
                return match?.Champion;
            }
            catch { return null; }
        }

        #endregion
    }

    public record RankEntry(string Queue, string Tier, string Rank, int LP, int Wins, int Losses);
    public record RankSnapshot(int SoloLP, string SoloFull, int FlexLP, string FlexFull);
    public record LossEvent(ulong DiscordId, string SummonerName, string Queue, string RankFull, int LPLost, string ChampionName);
    public record MatchDetail(string MatchId, int QueueId, string QueueName, string Champion, int Kills, int Deaths, int Assists, bool Win, int CS, int DurationMin);
}
