using Discord;
using Discord.WebSocket;
using MusicBot2.Models;
using StackExchange.Redis;
using StackExchange.Redis.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class FreeDuelService
    {
        private readonly IDatabase _redisDb;
        private readonly bool _useRedis;
        private readonly OpenRouterService _ai;
        private readonly DiscordSocketClient _client;
        private static readonly Dictionary<ulong, FreeDuelState> _memFallback = new();

        private static readonly TimeSpan DuelTimeout = TimeSpan.FromMinutes(30);

        // ── Character definitions ──────────────────────────────────────────
        private static readonly Dictionary<string, (string Name, string Personality, string DeckDesc)> Characters = new()
        {
            ["yugi"] = (
    "武藤遊戲",
    "善良溫和、害羞內向但非常重視朋友，相信夥伴與團結的力量；遇到危機時會鼓起勇氣。喜歡遊戲、謎題與決鬥，性格不像另一個自己那麼強勢，會尊重對手並相信自己的夥伴。",
    "黑魔術師、黑魔術師女孩、千年積木、魔法卡、儀式召喚"
),

            ["kaiba"] = (
    "海馬瀬人",
    "極度自信、高傲自負、勝負欲與自尊心極強，認為弱者沒有資格與自己決鬥；經常嘲諷對手，但對真正強大的對手會認真看待。極度執著於超越遊戲並成為最強決鬥者。",
    "青眼白龍、青眼究極龍、巨神兵、XYZ機械、Kaiba Corporation"
),

            ["joey"] = (
    "城之內克也",
    "直率粗獷、愛逞強、嘴硬但重情重義，尤其珍惜朋友；不是最精密的策略型決鬥者，但意志力極強，經常靠臨場反應、直覺與驚人的運氣逆轉局勢。",
    "真紅眼黑龍、戰士族、骰子怪獸、時間魔術師、骰盅"
),

            ["mai"] = (
    "孔雀舞",
    "成熟自信、驕傲而有魅力，說話帶有毒舌和戲謔感，不喜歡示弱；表面上獨立強勢，實際上非常重視友情，尤其在與遊戲和城之內相處後逐漸學會信任他人。",
    "Harpie Lady、Harpie Lady Sisters、Elegant Egotist、Harpie牌組、Amazoness"
),

            ["marik"] = (
    "馬利克・伊修達爾",
    "外表冷靜但內心充滿怨恨，對自己的過去與家族抱持強烈的痛苦與矛盾；如果是闇馬利克人格，則會變得殘酷扭曲，喜歡折磨對手並享受黑暗遊戲帶來的恐懼。",
    "拉之翼神龍、Lava Golem、Slime、墓地與黑暗系卡片、千年錫杖"
),

            ["pegasus"] = (
    "馬克西米利安・佩格薩斯",
    "優雅、有教養、幽默而戲謔，喜歡用誇張又做作的語氣嘲弄對手；表面像紳士，實際上非常狡猾，喜歡利用千年眼讀取對手想法並玩弄對方心理。",
    "Toon World、Toon怪獸、千年眼、卡通牌組"
),

            ["bakura"] = (
    "獏良了（闇獏良）",
    "陰森詭異、狡猾殘忍，喜歡操弄對手的心理與靈魂，說話經常帶著令人不安的笑意；擅長利用黑暗遊戲、詛咒與墓地相關效果慢慢逼迫對手走向絕境。",
    "Destiny Board、Dark Necrofear、Diabound、詛咒與黑暗系卡片"
),

            ["jaden"] = (
    "遊城十代",
    "樂觀開朗、喜歡享受決鬥，對強者與新卡充滿興奮；相信自己的英雄與夥伴，經常憑直覺做出決定。即使面臨逆境，也傾向把決鬥視為一件有趣的事情。",
    "Elemental HERO、Polymerization、Winged Kuriboh、Skyscraper、融合召喚"
),

            ["chazz"] = (
    "萬丈目準",
    "自大傲慢、極度愛面子，認為自己是菁英；嘴硬又好勝，不願承認自己的弱點，但其實非常有潛力且不服輸。遭遇挫折後反而會更加努力證明自己。",
    "Armed Dragon、Ojama、VWXYZ、Union怪獸"
),

            ["alexis"] = (
    "天上院明日香",
    "冷靜、自信、聰明而有競爭心，擅長分析局勢，不容易被情緒左右；外表優雅成熟，對自己的決鬥能力有很高要求，也非常重視朋友與同伴。",
    "Cyber Angel Benten、Cyber Angel Dakini、Cyber Angel Idaten、Cyber Blader、儀式召喚"
),

            ["zane"] = (
    "丸藤亮",
    "冷酷寡言、極度重視勝負與力量，將決鬥視為證明自身價值的方式；遭遇挫折後變得更加執著甚至近乎瘋狂，願意承受巨大代價追求勝利。",
    "Cyber Dragon、Cyber Twin Dragon、Cyber End Dragon、Power Bond、Cyberdark"
),
        };

        public FreeDuelService(string? redisConn, OpenRouterService ai, DiscordSocketClient client)
        {
            _ai = ai;
            _client = client;
            try
            {
                if (!string.IsNullOrEmpty(redisConn))
                {
                    var opts = ConfigurationOptions.Parse(redisConn);
                    opts.ConnectTimeout = 10000;
                    opts.AbortOnConnectFail = false;
                    opts.ConnectRetry = 3;
                    var conn = ConnectionMultiplexer.Connect(opts);
                    _redisDb = conn.GetDatabase();
                    _useRedis = true;
                }
            }
            catch { _useRedis = false; }
        }

        // ── Redis helpers ──────────────────────────────────────────────────
        private string Key(ulong channelId) => $"freeduel:{channelId}";

        private async Task<FreeDuelState> LoadAsync(ulong channelId)
        {
            if (_useRedis)
            {
                var json = await _redisDb.StringGetAsync(Key(channelId));
                if (json.HasValue) return JsonSerializer.Deserialize<FreeDuelState>(json.ToString());
            }
            return _memFallback.TryGetValue(channelId, out var s) ? s : null;
        }

        private async Task SaveAsync(FreeDuelState state)
        {
            var json = JsonSerializer.Serialize(state);
            if (_useRedis) await _redisDb.StringSetAsync(Key(state.ChannelId), json, TimeSpan.FromHours(2));
            else _memFallback[state.ChannelId] = state;
        }

        private async Task DeleteAsync(ulong channelId)
        {
            if (_useRedis) await _redisDb.KeyDeleteAsync(Key(channelId));
            else _memFallback.Remove(channelId);
        }

        // ── Public API ─────────────────────────────────────────────────────
        public async Task<bool> IsDuelActiveAsync(ulong channelId)
        {
            var state = await LoadAsync(channelId);
            if (state == null || state.IsDuelEnded) return false;
            if (DateTime.UtcNow - state.LastActionTime > DuelTimeout)
            {
                await DeleteAsync(channelId);
                return false;
            }
            return true;
        }

        public async Task<(Embed embed, string message)> StartDuelAsync(
            ulong channelId, ulong playerId, string playerName, string characterKey)
        {
            if (!Characters.ContainsKey(characterKey))
                characterKey = "kaiba";
            var ch = Characters[characterKey];

            var state = new FreeDuelState
            {
                ChannelId = channelId,
                PlayerId = playerId,
                PlayerName = playerName,
                AiCharacterKey = characterKey,
                AiCharacterName = ch.Name,
                PlayerHp = 8000,
                AiHp = 8000,
                AiHandCount = 5,
                LastActionTime = DateTime.UtcNow,
            };

            string openingPrompt = BuildSystemPrompt(state) +
                "\n\n【開局】決鬥開始！用角色口吻說一段開場宣言（2-4句話），然後描述你的第一回合行動（抽牌、可選擇召喚一隻怪獸）。" +
                "\n直接回傳 JSON，不要有其他文字。初始狀態：雙方手牌都是5張（你自己決定你的手牌），場上空白。";

            var (opening, updatedState) = await CallAiAsync(state, openingPrompt);
            await SaveAsync(updatedState);

            var embed = BuildBoardEmbed(updatedState);
            return (embed, opening);
        }

        public async Task<(Embed embed, string message)> HandleMessageAsync(
            ulong channelId, ulong userId, string content)
        {
            var state = await LoadAsync(channelId);
            if (state == null) return (null, null);

            if (DateTime.UtcNow - state.LastActionTime > DuelTimeout)
            {
                await DeleteAsync(channelId);
                return (null, "⏰ 決鬥超時（30分鐘無操作），決鬥自動結束，頻道回復正常。");
            }

            state.LastActionTime = DateTime.UtcNow;

            var userTurnContext = BuildUserTurnContext(state, content);
            var (reply, updatedState) = await CallAiAsync(state, userTurnContext);

            bool ended = updatedState.IsDuelEnded;
            if (ended) await DeleteAsync(channelId);
            else await SaveAsync(updatedState);

            var embed = BuildBoardEmbed(updatedState);
            return (embed, reply);
        }

        public async Task<string> ForceEndDuelAsync(ulong channelId)
        {
            await DeleteAsync(channelId);
            return "🏳️ 決鬥強制結束，頻道回復正常。";
        }

        // ── AI call ────────────────────────────────────────────────────────
        private async Task<(string reply, FreeDuelState updatedState)> CallAiAsync(
            FreeDuelState state, string userContent)
        {
            // Build full prompt with history embedded in the message
            var sb = new StringBuilder();
            sb.AppendLine(BuildSystemPrompt(state));
            sb.AppendLine();

            // Add last 10 history entries inline
            var recentHistory = state.History.TakeLast(10).ToList();
            if (recentHistory.Any())
            {
                sb.AppendLine("【對話記錄】");
                foreach (var h in recentHistory)
                {
                    string roleLabel = h.Role == "user" ? "玩家" : "AI";
                    sb.AppendLine($"[{roleLabel}]: {h.Content}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("【本次輸入】");
            sb.AppendLine(userContent);

            string fullPrompt = sb.ToString();
            string raw = "";
            try
            {
                raw = await _ai.GenerateSimpleTextAsync(fullPrompt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FreeDuel] AI call failed: {ex.Message}");
                return ("（AI 暫時無法回應，請稍後再試）", state);
            }

            // Parse JSON
            FreeDuelAiResponse parsed = null;
            string dialogue = raw;

            try
            {
                var jsonStr = ExtractJson(raw);
                if (!string.IsNullOrEmpty(jsonStr))
                {
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    parsed = JsonSerializer.Deserialize<FreeDuelAiResponse>(jsonStr, opts);
                    dialogue = parsed?.Dialogue ?? raw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FreeDuel] JSON parse failed: {ex.Message}\nRaw: {raw.Substring(0, Math.Min(200, raw.Length))}");
            }

            // Update state from parsed response
            if (parsed != null)
            {
                state.PlayerHp = Math.Max(0, parsed.PlayerHp);
                state.AiHp = Math.Max(0, parsed.AiHp);
                if (parsed.PlayerHand?.Any() == true) state.PlayerHand = parsed.PlayerHand;
                if (parsed.PlayerField != null) state.PlayerField = parsed.PlayerField;
                if (parsed.PlayerGraveyard != null) state.PlayerGraveyard = parsed.PlayerGraveyard;
                if (parsed.AiField != null) state.AiField = parsed.AiField;
                if (parsed.AiGraveyard != null) state.AiGraveyard = parsed.AiGraveyard;
                state.AiHandCount = parsed.AiHandCount;
                state.TurnNumber++;

                if (parsed.DuelEnded || state.PlayerHp <= 0 || state.AiHp <= 0)
                {
                    state.IsDuelEnded = true;
                    state.Winner = parsed.Winner ?? (state.PlayerHp <= 0 ? "ai" : "player");
                }
            }

            // Add to history
            state.History.Add(new FreeDuelMessage { Role = "user", Content = userContent });
            state.History.Add(new FreeDuelMessage { Role = "assistant", Content = dialogue });
            if (state.History.Count > 20) state.History.RemoveRange(0, state.History.Count - 20);

            return (dialogue, state);
        }

        // ── Prompt builders ────────────────────────────────────────────────
        private static string BuildSystemPrompt(FreeDuelState state)
        {
            if (!Characters.TryGetValue(state.AiCharacterKey, out var ch))
                ch = Characters["kaiba"];

            return $@"你是遊戲王中的決鬥者【{ch.Name}】，正在與【{state.PlayerName}】進行一場遊戲王卡片決鬥。

【角色個性】
{ch.Personality}

【你的牌組風格】
{ch.DeckDesc}

【決鬥規則】
- 雙方初始 LP 8000，先降至 0 者敗
- 玩家用文字描述行動，你判斷合理性後做出反應，並執行自己的回合行動
- 你同時是對手與裁判，公平計算傷害（攻擊力差距 = 傷害）
- 榮譽制：相信玩家宣告的手牌，但可以偶爾懷疑或嘲諷
- 你的手牌玩家看不到，由你自行管理決定

【回應格式】
只回傳一個 JSON 物件，不要有其他文字、不要用 markdown code block：
{{
  ""dialogue"": ""角色台詞與決鬥描述（包含你的行動，可以很精彩）"",
  ""player_hp"": 數字,
  ""ai_hp"": 數字,
  ""player_hand"": [""牌名1"",""牌名2""],
  ""player_field"": [{{""name"":""牌名"",""atk"":數字,""def"":數字,""face_down"":false,""is_defense"":false}}],
  ""player_graveyard"": [""牌名1""],
  ""ai_field"": [{{""name"":""牌名"",""atk"":數字,""def"":數字,""face_down"":false,""is_defense"":false}}],
  ""ai_graveyard"": [""牌名1""],
  ""ai_hand_count"": 數字,
  ""duel_ended"": false,
  ""winner"": null
}}
決鬥結束時設 ""duel_ended"": true，""winner"": ""player"" 或 ""winner"": ""ai""";
        }

        private static string BuildUserTurnContext(FreeDuelState state, string playerAction)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【當前場面】");
            sb.AppendLine($"回合: {state.TurnNumber}");
            sb.AppendLine($"玩家 LP: {state.PlayerHp}  |  {state.AiCharacterName} LP: {state.AiHp}");
            sb.AppendLine($"玩家手牌: {(state.PlayerHand.Any() ? string.Join("、", state.PlayerHand) : "（空）")}");

            if (state.PlayerField.Any())
                sb.AppendLine("玩家場上: " + string.Join("、", state.PlayerField.Select(c =>
                    c.FaceDown ? "？（背面）" : $"{c.Name}({(c.IsDefense ? "守備" : "攻擊")} {c.Atk})")));
            else
                sb.AppendLine("玩家場上: 空");

            if (state.AiField.Any())
                sb.AppendLine($"{state.AiCharacterName}場上: " + string.Join("、", state.AiField.Select(c =>
                    c.FaceDown ? "？（背面）" : $"{c.Name}({(c.IsDefense ? "守備" : "攻擊")} {c.Atk})")));
            else
                sb.AppendLine($"{state.AiCharacterName}場上: 空");

            sb.AppendLine($"玩家墓地: {(state.PlayerGraveyard.Any() ? string.Join("、", state.PlayerGraveyard.TakeLast(5)) : "空")}");
            sb.AppendLine($"{state.AiCharacterName}墓地: {(state.AiGraveyard.Any() ? string.Join("、", state.AiGraveyard.TakeLast(5)) : "空")}");
            sb.AppendLine($"{state.AiCharacterName}手牌數: {state.AiHandCount}");
            sb.AppendLine();
            sb.AppendLine($"【玩家行動】: {playerAction}");
            sb.AppendLine();
            sb.AppendLine("請做出反應，執行你的回合，回傳 JSON。");
            return sb.ToString();
        }

        private static string ExtractJson(string raw)
        {
            raw = raw.Trim();
            if (raw.StartsWith("```"))
            {
                var start = raw.IndexOf('{');
                var end = raw.LastIndexOf('}');
                if (start >= 0 && end > start) return raw.Substring(start, end - start + 1);
            }
            if (raw.StartsWith("{"))
            {
                var end = raw.LastIndexOf('}');
                if (end >= 0) return raw.Substring(0, end + 1);
            }
            return "";
        }

        // ── Embed builder ──────────────────────────────────────────────────
        public static Embed BuildBoardEmbed(FreeDuelState state)
        {
            var eb = new EmbedBuilder()
                .WithTitle($"⚔️ 自由決鬥  回合 {state.TurnNumber}")
                .WithColor(state.IsDuelEnded ? Color.DarkRed : new Color(0xFFD700));

            string playerHpBar = HpBar(state.PlayerHp);
            string aiHpBar = HpBar(state.AiHp);
            eb.AddField($"🧑 {state.PlayerName}", $"{playerHpBar} **{state.PlayerHp} LP**", inline: true);
            eb.AddField($"🤖 {state.AiCharacterName}", $"{aiHpBar} **{state.AiHp} LP**", inline: true);
            eb.AddField("​", "​", inline: false);

            string playerFieldStr = state.PlayerField.Any()
                ? string.Join("\n", state.PlayerField.Select(c => c.FaceDown ? "▣ 背面守備" : $"▲ {c.Name} ATK{c.Atk}/{(c.IsDefense ? "DEF守" : "ATK攻")}"))
                : "（空）";
            string aiFieldStr = state.AiField.Any()
                ? string.Join("\n", state.AiField.Select(c => c.FaceDown ? "▣ 背面守備" : $"▲ {c.Name} ATK{c.Atk}/{(c.IsDefense ? "DEF守" : "ATK攻")}"))
                : "（空）";
            eb.AddField("🧑 場上", playerFieldStr, inline: true);
            eb.AddField("🤖 場上", aiFieldStr, inline: true);
            eb.AddField("​", "​", inline: false);

            string handStr = state.PlayerHand.Any() ? string.Join("、", state.PlayerHand) : "（空）";
            eb.AddField("🧑 手牌", handStr, inline: true);
            eb.AddField("🤖 手牌", $"{state.AiHandCount} 張（未知）", inline: true);

            if (state.PlayerGraveyard.Any() || state.AiGraveyard.Any())
            {
                eb.AddField("🧑 墓地", state.PlayerGraveyard.Any() ? string.Join("、", state.PlayerGraveyard.TakeLast(3)) : "空", inline: true);
                eb.AddField("🤖 墓地", state.AiGraveyard.Any() ? string.Join("、", state.AiGraveyard.TakeLast(3)) : "空", inline: true);
            }

            if (state.IsDuelEnded)
            {
                string winnerDisplay = state.Winner == "player" ? $"🏆 {state.PlayerName} 勝利！" : $"🏆 {state.AiCharacterName} 勝利！";
                eb.AddField("決鬥結果", winnerDisplay);
                eb.WithFooter("決鬥已結束，頻道回復正常");
            }
            else
            {
                eb.WithFooter("直接輸入你的決鬥行動 | 使用 /endduel 強制結束");
            }

            return eb.Build();
        }

        private static string HpBar(int hp)
        {
            int filled = (int)Math.Round(hp / 8000.0 * 10);
            filled = Math.Clamp(filled, 0, 10);
            return new string('█', filled) + new string('░', 10 - filled);
        }
    }
}
