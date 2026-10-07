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
    public class TFTService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        // region → (matchRegional, accountRegional, platform)
        private static (string Match, string Account, string Platform) GetEndpoints(string region) => (region ?? "tw").ToLower() switch
        {
            "kr"   => ("https://asia.api.riotgames.com",     "https://asia.api.riotgames.com",     "https://kr.api.riotgames.com"),
            "jp"   => ("https://asia.api.riotgames.com",     "https://asia.api.riotgames.com",     "https://jp1.api.riotgames.com"),
            "sg"   => ("https://sea.api.riotgames.com",      "https://asia.api.riotgames.com",     "https://sg2.api.riotgames.com"),
            "na"   => ("https://americas.api.riotgames.com", "https://americas.api.riotgames.com", "https://na1.api.riotgames.com"),
            "euw"  => ("https://europe.api.riotgames.com",   "https://europe.api.riotgames.com",   "https://euw1.api.riotgames.com"),
            "eune" => ("https://europe.api.riotgames.com",   "https://europe.api.riotgames.com",   "https://eun1.api.riotgames.com"),
            _      => ("https://sea.api.riotgames.com",      "https://asia.api.riotgames.com",     "https://tw2.api.riotgames.com"),
        };

        // Match list cache
        private static readonly Dictionary<string, TFTCache> _cache = new();

        private record TFTCache(string GameName, string TagLine,
            string Tier, string Rank, int LP, int Wins, int Losses,
            List<TFTMatchDetail> Matches, List<List<TFTParticipant>> AllPlayers);

        public record TFTMatchDetail(string MatchId, int QueueId, int Placement, int Level,
            int LastRound, long DamageTo, int KOs,
            List<string> Augments, List<TFTUnit> Units, List<TFTTrait> Traits,
            long GameDatetime, int GameLengthSec);

        public record TFTUnit(string Champion, int Tier);
        public record TFTTrait(string Name, int NumUnits, int Style);
        public record TFTParticipant(string Name, int Placement, int Level, List<TFTUnit> Units, bool IsMe);

        private static string MakeCacheKey(string puuid)
            => new string(puuid.Where(char.IsLetterOrDigit).Take(14).ToArray());

        // Strip TFT set prefix: TFT14_Jinx → Jinx, Set14_Sorcerer → Sorcerer
        private static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id)) return id ?? "";
            var idx = id.LastIndexOf('_');
            return idx >= 0 ? id[(idx + 1)..] : id;
        }

        private static string PlacementEmoji(int p) => p switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", _ => $"#{p}" };
        private static string QueueLabel(int q) => q switch { 1100 => "積分", 1090 => "一般", 1130 => "極速", 1160 => "雙人", _ => "其他" };

        private static string TierLabel(string tier) => (tier ?? "").ToUpper() switch
        {
            "IRON" => "鐵牌", "BRONZE" => "銅牌", "SILVER" => "銀牌", "GOLD" => "金牌",
            "PLATINUM" => "白金", "EMERALD" => "翡翠", "DIAMOND" => "鑽石",
            "MASTER" => "大師", "GRANDMASTER" => "宗師", "CHALLENGER" => "菁英",
            _ => tier ?? "未知"
        };

        public TFTService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _httpClient.DefaultRequestHeaders.Add("X-Riot-Token", apiKey);
        }

        private async Task<JsonElement?> GetJsonAsync(string url)
        {
            try
            {
                var resp = await _httpClient.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return null;
                var json = await resp.Content.ReadAsStringAsync();
                return JsonDocument.Parse(json).RootElement;
            }
            catch { return null; }
        }

        public async Task<string> GetPuuidByRiotIdAsync(string gameName, string tagLine, string region = "tw")
        {
            var (_, account, _) = GetEndpoints(region);
            var data = await GetJsonAsync($"{account}/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}?api_key={_apiKey}");
            return data?.TryGetProperty("puuid", out var p) == true ? p.GetString() : null;
        }

        private async Task<(string name, string tag)> GetAccountByPuuidAsync(string puuid, string accountRegional)
        {
            var data = await GetJsonAsync($"{accountRegional}/riot/account/v1/accounts/by-puuid/{puuid}?api_key={_apiKey}");
            if (data == null) return ("?", "?");
            return (data.Value.TryGetProperty("gameName", out var n) ? n.GetString() ?? "?" : "?",
                    data.Value.TryGetProperty("tagLine", out var t) ? t.GetString() ?? "?" : "?");
        }

        private async Task<List<string>> GetMatchIdsAsync(string puuid, string matchRegional, int count = 20)
        {
            var data = await GetJsonAsync($"{matchRegional}/tft/match/v1/matches/by-puuid/{puuid}/ids?count={count}&api_key={_apiKey}");
            if (data == null) return new();
            return data.Value.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).ToList();
        }

        private async Task<(string tier, string rank, int lp, int wins, int losses)> GetRankAsync(string puuid, string platform)
        {
            var summ = await GetJsonAsync($"{platform}/tft/summoner/v1/summoners/by-puuid/{puuid}?api_key={_apiKey}");
            if (summ == null) return (null, null, 0, 0, 0);
            var summonerId = summ.Value.TryGetProperty("id", out var idp) ? idp.GetString() : null;
            if (summonerId == null) return (null, null, 0, 0, 0);

            var ranks = await GetJsonAsync($"{platform}/tft/league/v1/entries/by-summoner/{summonerId}?api_key={_apiKey}");
            if (ranks == null) return (null, null, 0, 0, 0);

            foreach (var entry in ranks.Value.EnumerateArray())
            {
                if (!entry.TryGetProperty("queueType", out var qt) || qt.GetString() != "RANKED_TFT") continue;
                return (
                    entry.TryGetProperty("tier", out var t) ? t.GetString() : null,
                    entry.TryGetProperty("rank", out var r) ? r.GetString() : null,
                    entry.TryGetProperty("leaguePoints", out var lp) ? lp.GetInt32() : 0,
                    entry.TryGetProperty("wins", out var w) ? w.GetInt32() : 0,
                    entry.TryGetProperty("losses", out var lo) ? lo.GetInt32() : 0
                );
            }
            return (null, null, 0, 0, 0);
        }

        private async Task<(TFTMatchDetail me, List<TFTParticipant> all, string fallbackName)> ParseMatchAsync(string matchId, string puuid, string matchRegional)
        {
            var data = await GetJsonAsync($"{matchRegional}/tft/match/v1/matches/{matchId}?api_key={_apiKey}");
            if (data == null) return (null, null, null);
            if (!data.Value.TryGetProperty("info", out var info)) return (null, null, null);
            if (!info.TryGetProperty("participants", out var participants)) return (null, null, null);

            int queueId = info.TryGetProperty("queue_id", out var qp) ? qp.GetInt32() : 0;
            long gameDatetime = info.TryGetProperty("game_datetime", out var gdt) ? gdt.GetInt64() : 0;
            int gameLengthSec = info.TryGetProperty("game_length", out var gl) ? (int)gl.GetDouble() : 0;

            TFTMatchDetail myDetail = null;
            string fallbackName = null;
            var allPlayers = new List<TFTParticipant>();

            foreach (var p in participants.EnumerateArray())
            {
                var pPuuid = p.TryGetProperty("puuid", out var pp) ? pp.GetString() : null;
                bool isMe = pPuuid == puuid;

                int placement = p.TryGetProperty("placement", out var pl) ? pl.GetInt32() : 0;
                int level = p.TryGetProperty("level", out var lv) ? lv.GetInt32() : 0;
                int lastRound = p.TryGetProperty("last_round", out var lr) ? lr.GetInt32() : 0;
                long damageTo = p.TryGetProperty("total_damage_to_players", out var dtd) ? dtd.GetInt64() : 0;
                int kos = p.TryGetProperty("players_eliminated", out var ke) ? ke.GetInt32() : 0;
                string pName = (p.TryGetProperty("riotIdGameName", out var rn) ? rn.GetString() : null) ?? "?";

                if (isMe && pName != "?") fallbackName = pName;

                var augments = new List<string>();
                if (p.TryGetProperty("augments", out var augArr))
                    foreach (var a in augArr.EnumerateArray())
                    {
                        var aStr = a.GetString();
                        if (aStr != null) augments.Add(ShortId(aStr));
                    }

                var units = new List<TFTUnit>();
                if (p.TryGetProperty("units", out var unitArr))
                    foreach (var u in unitArr.EnumerateArray())
                    {
                        var cid = u.TryGetProperty("character_id", out var cidp) ? cidp.GetString() : null;
                        int tier = u.TryGetProperty("tier", out var tp) ? tp.GetInt32() : 1;
                        if (cid != null) units.Add(new TFTUnit(ShortId(cid), tier));
                    }

                var traits = new List<TFTTrait>();
                if (p.TryGetProperty("traits", out var traitArr))
                    foreach (var tr in traitArr.EnumerateArray())
                    {
                        string name = tr.TryGetProperty("name", out var tn) ? ShortId(tn.GetString()) : "?";
                        int numUnits = tr.TryGetProperty("num_units", out var nu) ? nu.GetInt32() : 0;
                        int style = tr.TryGetProperty("style", out var s) ? s.GetInt32() : 0;
                        if (numUnits > 0) traits.Add(new TFTTrait(name, numUnits, style));
                    }

                allPlayers.Add(new TFTParticipant(pName, placement, level, units, isMe));

                if (isMe)
                    myDetail = new TFTMatchDetail(matchId, queueId, placement, level, lastRound,
                        damageTo, kos, augments, units, traits, gameDatetime, gameLengthSec);
            }

            return (myDetail, allPlayers.OrderBy(p => p.Placement).ToList(), fallbackName);
        }

        #region Public methods

        public async Task<(string TextForAI, Embed Embed, ComponentBuilder Component, string CacheKey)> GetPlayerStatsAsync(string puuid, string region = "tw")
        {
            try
            {
                var (matchRegional, accountRegional, platform) = GetEndpoints(region);
                Console.WriteLine($"[TFTService] GetPlayerStats puuid={puuid[..20]}...");

                var (gameName, tagLine) = await GetAccountByPuuidAsync(puuid, accountRegional);
                var (tier, rank, lp, wins, losses) = await GetRankAsync(puuid, platform);
                var matchIds = await GetMatchIdsAsync(puuid, matchRegional, 20);

                var parseTasks = matchIds.Select(id => ParseMatchAsync(id, puuid, matchRegional)).ToArray();
                await Task.WhenAll(parseTasks);

                var matches = new List<TFTMatchDetail>();
                var allPlayers = new List<List<TFTParticipant>>();
                string fallback = null;
                foreach (var t in parseTasks)
                {
                    if (t.Result.me != null)
                    {
                        matches.Add(t.Result.me);
                        allPlayers.Add(t.Result.all ?? new List<TFTParticipant>());
                        fallback ??= t.Result.fallbackName;
                    }
                }

                if ((gameName == "?" || string.IsNullOrEmpty(gameName)) && fallback != null)
                    gameName = fallback;

                var cacheKey = MakeCacheKey(puuid);
                _cache[cacheKey] = new TFTCache(gameName, tagLine, tier, rank, lp, wins, losses, matches, allPlayers);

                var (embed, component) = BuildOverviewEmbed(gameName, tagLine, tier, rank, lp, wins, losses, matches, cacheKey);

                // Text for AI
                var sb = new StringBuilder();
                sb.AppendLine($"TFT 玩家：{gameName}#{tagLine}");
                sb.AppendLine(tier != null ? $"積分段位：{TierLabel(tier)} {rank} {lp} LP（{wins}W/{losses}L）" : "積分段位：未定位");

                if (matches.Count > 0)
                {
                    double avgP = matches.Average(m => m.Placement);
                    int top4 = matches.Count(m => m.Placement <= 4);
                    int first = matches.Count(m => m.Placement == 1);
                    sb.AppendLine($"近{matches.Count}場：平均名次{avgP:F2}，Top4率{top4 * 100 / matches.Count}%，吃雞率{first * 100 / matches.Count}%");
                    foreach (var m in matches)
                    {
                        var topTraits = m.Traits.Where(t => t.Style >= 1).OrderByDescending(t => t.Style).Take(2).Select(t => t.Name);
                        sb.AppendLine($"{PlacementEmoji(m.Placement)} {QueueLabel(m.QueueId)} Lv{m.Level} 羈絆:{string.Join(",", topTraits)} 增益:{string.Join(",", m.Augments.Take(2))}");
                    }
                }

                return (sb.ToString().TrimEnd(), embed, component, cacheKey);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TFTService] GetPlayerStats EXCEPTION: {ex.Message}");
                return (null, null, null, null);
            }
        }

        private static (Embed embed, ComponentBuilder component) BuildOverviewEmbed(
            string gameName, string tagLine,
            string tier, string rank, int lp, int wins, int losses,
            List<TFTMatchDetail> matches, string cacheKey)
        {
            var displayName = $"{gameName}#{tagLine}";
            var eb = new EmbedBuilder()
                .WithTitle($"♟️  {displayName}")
                .WithColor(new Color(0x1A78CF))
                .WithFooter("TFT 雲頂之弈戰績 · Riot API");

            string rankStr = tier != null
                ? $"**{TierLabel(tier)} {rank}** — {lp} LP\n{wins}W / {losses}L（{(wins + losses > 0 ? wins * 100 / (wins + losses) : 0)}%勝率）"
                : "未定位";
            eb.AddField("🏆 TFT 積分", rankStr, inline: false);

            if (matches.Count > 0)
            {
                double avgP = matches.Average(m => m.Placement);
                int top4 = matches.Count(m => m.Placement <= 4);
                int first = matches.Count(m => m.Placement == 1);
                int total = matches.Count;
                eb.AddField("📊 近期統計",
                    $"平均名次：**{avgP:F2}**　Top4率：**{top4 * 100 / total}%**　吃雞率：**{first * 100 / total}%**",
                    inline: false);

                var lines = matches.Select((m, i) =>
                {
                    var topTraits = m.Traits.Where(t => t.Style >= 2).OrderByDescending(t => t.Style).Take(2)
                        .Select(t => $"{t.Name}({t.NumUnits})");
                    var traitStr = string.Join(" ", topTraits);
                    if (string.IsNullOrEmpty(traitStr))
                        traitStr = m.Traits.Where(t => t.Style >= 1).OrderByDescending(t => t.NumUnits)
                            .Take(1).Select(t => $"{t.Name}({t.NumUnits})").FirstOrDefault() ?? "-";
                    return $"`#{i + 1}` {(m.Placement <= 4 ? "🟢" : "🔴")} **{PlacementEmoji(m.Placement)}** `{QueueLabel(m.QueueId)}` Lv{m.Level}　{traitStr}";
                }).ToList();

                var half = (lines.Count + 1) / 2;
                eb.AddField("📋 對局紀錄（點按鈕查詳情）", string.Join("\n", lines.Take(half)), inline: false);
                if (lines.Count > half)
                    eb.AddField("📋 對局紀錄（續）", string.Join("\n", lines.Skip(half)), inline: false);

                var component = new ComponentBuilder();
                for (int i = 0; i < Math.Min(matches.Count, 25); i++)
                {
                    var m = matches[i];
                    var label = $"{(m.Placement <= 4 ? "✅" : "❌")} #{i + 1} {PlacementEmoji(m.Placement)}";
                    component.WithButton(label, $"tft_match_{cacheKey}_{i}", ButtonStyle.Secondary, row: i / 5);
                }
                return (eb.Build(), component);
            }
            else
            {
                eb.AddField("📋 近期對局", "未找到近期 TFT 對局資料（可能近期未出賽）", inline: false);
                return (eb.Build(), new ComponentBuilder());
            }
        }

        public (Embed embed, ComponentBuilder component) GetMatchDetailEmbed(string cacheKey, int idx)
        {
            if (!_cache.TryGetValue(cacheKey, out var cache))
                return (new EmbedBuilder().WithTitle("❌ 查詢已過期").WithDescription("請重新執行指令").WithColor(Color.Red).Build(), new ComponentBuilder());
            if (idx < 0 || idx >= cache.Matches.Count)
                return (new EmbedBuilder().WithTitle("❌ 對局不存在").WithColor(Color.Red).Build(), new ComponentBuilder());

            var m = cache.Matches[idx];
            string timeAgo = "";
            if (m.GameDatetime > 0)
            {
                var diff = DateTimeOffset.Now - DateTimeOffset.FromUnixTimeMilliseconds(m.GameDatetime).ToLocalTime();
                timeAgo = diff.TotalDays >= 1 ? $"{(int)diff.TotalDays}天前" :
                          diff.TotalHours >= 1 ? $"{(int)diff.TotalHours}小時前" :
                          $"{(int)diff.TotalMinutes}分鐘前";
            }

            var color = m.Placement == 1 ? new Color(0xFFD700) :
                        m.Placement <= 4 ? new Color(0x57AB27) : new Color(0xDA373C);

            var eb = new EmbedBuilder()
                .WithTitle($"{PlacementEmoji(m.Placement)} 第 {m.Placement} 名")
                .WithDescription($"`{QueueLabel(m.QueueId)}` · {m.GameLengthSec / 60} 分鐘 · {timeAgo}")
                .WithColor(color)
                .WithFooter($"{cache.GameName}#{cache.TagLine} · 第 {idx + 1} 場");

            if (m.Augments.Count > 0)
                eb.AddField("🔮 增益選擇", string.Join("　", m.Augments), inline: false);

            var activeTraits = m.Traits.Where(t => t.Style >= 1)
                .OrderByDescending(t => t.Style).ThenByDescending(t => t.NumUnits).Take(6)
                .Select(t => $"{t.Name}({t.NumUnits})");
            var traitStr = string.Join("　", activeTraits);
            if (!string.IsNullOrEmpty(traitStr))
                eb.AddField("🎭 羈絆", traitStr, inline: false);

            var unitStr = string.Join("  ", m.Units.OrderByDescending(u => u.Tier).ThenBy(u => u.Champion)
                .Select(u => $"{u.Champion}{new string('★', Math.Min(u.Tier, 3))}"));
            if (!string.IsNullOrEmpty(unitStr))
                eb.AddField("🪄 棋子", unitStr, inline: false);

            eb.AddField("📊 數據",
                $"等級：{m.Level}　傷害：{m.DamageTo}　擊倒：{m.KOs}　回合：{m.LastRound}", inline: false);

            if (idx < cache.AllPlayers.Count && cache.AllPlayers[idx]?.Count > 0)
            {
                var allStr = string.Join("\n", cache.AllPlayers[idx].Select(p =>
                {
                    var marker = p.IsMe ? "▶ " : "";
                    var topUnits = string.Join(" ", p.Units.OrderByDescending(u => u.Tier).Take(4).Select(u => u.Champion));
                    var name = p.Name.Length > 12 ? p.Name[..12] : p.Name;
                    return $"{marker}**{PlacementEmoji(p.Placement)}** Lv{p.Level}  `{name}`  {topUnits}";
                }));
                eb.AddField("🏟️ 所有玩家", allStr, inline: false);
            }

            return (eb.Build(), new ComponentBuilder().WithButton("← 返回戰績列表", $"tft_back_{cacheKey}", ButtonStyle.Secondary));
        }

        public (Embed embed, ComponentBuilder component) GetOverviewEmbed(string cacheKey)
        {
            if (!_cache.TryGetValue(cacheKey, out var cache))
                return (new EmbedBuilder().WithTitle("❌ 查詢已過期").WithDescription("請重新執行指令").WithColor(Color.Red).Build(), new ComponentBuilder());
            return BuildOverviewEmbed(cache.GameName, cache.TagLine, cache.Tier, cache.Rank, cache.LP, cache.Wins, cache.Losses, cache.Matches, cacheKey);
        }

        #endregion
    }
}
