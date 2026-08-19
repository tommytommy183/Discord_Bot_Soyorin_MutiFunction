using Discord;
using Discord.WebSocket;
using MusicBot2.Models;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class FreeDuelService
    {
        private readonly IDatabase? _redisDb;
        private readonly bool _useRedis;
        private readonly OpenRouterService _ai;
        private readonly DiscordSocketClient _client;
        private readonly YgoDuelService _ygoSvc;
        private static readonly Dictionary<ulong, FreeDuelState> _memFallback = new();
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

        private static readonly TimeSpan DuelTimeout = TimeSpan.FromMinutes(30);

        // ── Character definitions ──────────────────────────────────────────
        private record CharDef(string Name, string Personality, string DeckHint);
        private static readonly Dictionary<string, CharDef> Chars = new()
        {
            ["yugi"]    = new("武藤遊戲",   "熱血、相信夥伴與抽卡的力量，說話充滿鬥志。常說「我相信抽卡的力量！」", "暗黑魔術師、千年之眼、黑魔法儀式"),
            ["kaiba"]   = new("海馬瀬人",   "高傲自大、瞧不起對手，口頭禪是「蠢貨」，但決鬥全力以赴", "青眼白龍×3、白龍降臨、龍之力、敵對英雄"),
            ["joey"]    = new("城之內克也", "大而化之、義氣當頭、愛靠運氣，常說「哥哥我就靠運氣了！」", "紅眼黑龍、骰子怪獸、戰士族"),
            ["mai"]     = new("孔雀舞",     "性感自信、毒舌，內心重視友情", "亞馬遜族女戰士、香水陷阱"),
            ["marik"]   = new("馬立克",     "扭曲殘忍，喜歡折磨對手，使用暗黑魔法", "拉之翼神龍、墓地炸彈、洗腦魔法"),
            ["pegasus"] = new("ペガサス",   "優雅風趣、帶著紅酒杯，用千年之眼讀取手牌", "卡通怪獸、千年之眼"),
            ["bakura"]  = new("闇獏良",     "詭異陰沉、喜歡靈魂類效果與詭計", "屍鬼族、詛咒陷阱"),
            ["jaden"]   = new("響紅一",     "超樂觀、口頭禪是「收到！」，相信英雄的力量", "元素英雄、融合召喚、Wingman"),
            ["chazz"]   = new("萬丈目準",   "自大傲慢，常說「萬丈目準様だぞ！」", "武裝龍、VWXYZ機械合體"),
            ["alexis"]  = new("天上院明日香","冷靜優雅，決鬥時心思縝密", "Cyber Angel、寶石獸"),
            ["zane"]    = new("丸藤亮",     "冷酷強硬、不說廢話，決鬥即一切", "賽博龍、超導波動炮"),
        };

        public FreeDuelService(string? redisConn, OpenRouterService ai, DiscordSocketClient client, YgoDuelService ygoSvc)
        {
            _ai = ai;
            _client = client;
            _ygoSvc = ygoSvc;
            try
            {
                if (!string.IsNullOrWhiteSpace(redisConn))
                {
                    var opts = ConfigurationOptions.Parse(redisConn);
                    opts.AbortOnConnectFail = false;
                    opts.ConnectTimeout = 10000;
                    var conn = ConnectionMultiplexer.Connect(opts);
                    _redisDb = conn.GetDatabase();
                    _useRedis = true;
                }
            }
            catch { _useRedis = false; }
        }

        // ── Redis helpers ──────────────────────────────────────────────────
        private string Key(ulong cid) => $"freeduel:{cid}";

        private async Task<FreeDuelState?> LoadAsync(ulong cid)
        {
            if (_useRedis)
            {
                try
                {
                    var v = await _redisDb!.StringGetAsync(Key(cid));
                    if (v.HasValue) return JsonSerializer.Deserialize<FreeDuelState>(v!);
                }
                catch { }
            }
            return _memFallback.TryGetValue(cid, out var s) ? s : null;
        }

        private async Task SaveAsync(FreeDuelState s)
        {
            var json = JsonSerializer.Serialize(s);
            if (_useRedis) try { await _redisDb!.StringSetAsync(Key(s.ChannelId), json, TimeSpan.FromHours(2)); } catch { }
            _memFallback[s.ChannelId] = s;
        }

        private async Task DeleteAsync(ulong cid)
        {
            if (_useRedis) try { await _redisDb!.KeyDeleteAsync(Key(cid)); } catch { }
            _memFallback.Remove(cid);
        }

        // ── Public API ─────────────────────────────────────────────────────
        public async Task<bool> IsDuelActiveAsync(ulong cid)
        {
            var s = await LoadAsync(cid);
            if (s == null || s.IsDuelEnded) return false;
            if (DateTime.UtcNow - s.LastActionTime > DuelTimeout) { await DeleteAsync(cid); return false; }
            return true;
        }

        public async Task<(Embed embed, ComponentBuilder component, string message)> StartDuelAsync(
            ulong channelId, ulong playerId, string playerName, string playerDeckKey, string aiCharacterKey)
        {
            if (!Chars.ContainsKey(aiCharacterKey)) aiCharacterKey = "kaiba";
            if (!_ygoSvc.GetDeckDefinitions().ContainsKey(playerDeckKey)) playerDeckKey = "yugi";

            var ch = Chars[aiCharacterKey];
            var deck = _ygoSvc.GetDeckDefinitions()[playerDeckKey];

            // Deal 5 cards from player's deck
            var deckCards = deck.MainDeckNames.OrderBy(_ => Guid.NewGuid()).ToList();
            var startHand = deckCards.Take(5).ToList();

            var state = new FreeDuelState
            {
                ChannelId = channelId,
                PlayerId = playerId,
                PlayerName = playerName,
                PlayerDeckKey = playerDeckKey,
                AiCharacterKey = aiCharacterKey,
                AiCharacterName = ch.Name,
                PlayerHand = startHand,
                AiHandCount = 5,
                LastActionTime = DateTime.UtcNow,
            };

            // Opening AI message
            var openingCtx = $"決鬥開始！玩家{playerName}的牌組是「{deck.CharacterName}」風格，你是「{ch.Name}」。\n" +
                             $"玩家初始手牌（5張）：{string.Join("、", startHand)}\n" +
                             $"請用角色口吻說開場宣言（2-4句話），然後描述你的第一回合行動（你先手：抽1張牌，可以選擇召喚一隻怪獸或不行動）。\n" +
                             "回傳 JSON，dialogue 包含台詞，events 包含你這回合的行動事件。";

            var (dialogue, updatedState) = await CallAiAsync(state, openingCtx);
            await SaveAsync(updatedState);

            var embed = BuildBoardEmbed(updatedState);
            var cb = BuildBoardButtons(updatedState);
            return (embed, cb, dialogue);
        }

        public async Task<(Embed embed, ComponentBuilder component, string? message)> HandleMessageAsync(
            ulong channelId, ulong userId, string content)
        {
            var state = await LoadAsync(channelId);
            if (state == null) return (null!, null!, null);

            if (DateTime.UtcNow - state.LastActionTime > DuelTimeout)
            {
                await DeleteAsync(channelId);
                return (null!, null!, "⏰ 決鬥超時（30分鐘無操作），自動結束。");
            }

            state.LastActionTime = DateTime.UtcNow;
            var ctx = BuildTurnContext(state, content);
            var (dialogue, updatedState) = await CallAiAsync(state, ctx);

            if (updatedState.IsDuelEnded) await DeleteAsync(channelId);
            else await SaveAsync(updatedState);

            var embed = BuildBoardEmbed(updatedState);
            var cb = BuildBoardButtons(updatedState);
            return (embed, cb, dialogue);
        }

        public async Task<string> ForceEndAsync(ulong channelId)
        {
            await DeleteAsync(channelId);
            return "🏳️ 決鬥強制結束，頻道回復正常。";
        }

        // Keep old name for backward compat
        public Task<string> ForceEndDuelAsync(ulong channelId) => ForceEndAsync(channelId);

        /// <summary>顯示玩家手牌 embed，selectedIdx 決定顯示哪張卡圖</summary>
        public async Task<(Embed embed, ComponentBuilder component)> GetFreeDuelHandEmbedAsync(
            ulong channelId, ulong userId, int selectedIdx = 0)
        {
            var state = await LoadAsync(channelId);
            if (state == null) return (BuildError("沒有進行中的決鬥。"), new ComponentBuilder());
            if (state.PlayerId != userId) return (BuildError("不是你的決鬥！"), new ComponentBuilder());

            var hand = state.PlayerHand;
            if (!hand.Any())
            {
                var emptyEb = new EmbedBuilder()
                    .WithTitle($"🤚 {state.PlayerName} 的手牌（0 張）")
                    .WithColor(Color.DarkGrey)
                    .WithDescription("手牌是空的！");
                return (emptyEb.Build(), new ComponentBuilder());
            }

            int sel = Math.Clamp(selectedIdx, 0, hand.Count - 1);
            string selName = hand[sel];

            var eb = new EmbedBuilder()
                .WithTitle($"🤚 {state.PlayerName} 的手牌（{hand.Count} 張）　　▶ {selName}")
                .WithColor(Color.Gold);

            for (int i = 0; i < hand.Count; i++)
            {
                string label = i == sel ? $"**[{i + 1}] {hand[i]}**" : $"{i + 1}. {hand[i]}";
                eb.AddField(label, i == sel ? "◀ 目前顯示" : "－", inline: true);
            }

            // Fetch card image for selected card
            string imgUrl = await TryGetCardImageAsync(selName);
            if (!string.IsNullOrEmpty(imgUrl)) eb.WithImageUrl(imgUrl);

            // Numbered buttons
            var cb = new ComponentBuilder();
            var row = new ActionRowBuilder();
            for (int i = 0; i < hand.Count; i++)
            {
                if (i > 0 && i % 5 == 0) { cb.AddRow(row); row = new ActionRowBuilder(); }
                var style = i == sel ? ButtonStyle.Primary : ButtonStyle.Secondary;
                row.WithButton($"🖼️{i + 1}", $"ygo_fdcardimg_{channelId}_{i}", style);
            }
            cb.AddRow(row);
            return (eb.Build(), cb);
        }

        public async Task SetFreeDuelHandMessageIdAsync(ulong channelId, ulong messageId)
        {
            var state = await LoadAsync(channelId);
            if (state == null) return;
            state.HandMessageId = messageId;
            await SaveAsync(state);
        }

        public async Task<FreeDuelState?> GetStateAsync(ulong channelId) => await LoadAsync(channelId);

        // ── AI call ────────────────────────────────────────────────────────
        private async Task<(string dialogue, FreeDuelState updatedState)> CallAiAsync(
            FreeDuelState state, string userContent)
        {
            // Build full prompt: system + history + current turn
            var historyBlock = new StringBuilder();
            foreach (var h in state.History.TakeLast(8))
                historyBlock.AppendLine($"[{h.Role.ToUpper()}]: {h.Content}");

            string fullPrompt = BuildSystemPrompt(state) + "\n\n" +
                                (historyBlock.Length > 0 ? "【對話歷史】\n" + historyBlock + "\n" : "") +
                                userContent;

            string raw = "";
            try { raw = await _ai.GenerateSimpleTextAsync(fullPrompt); }
            catch (Exception ex)
            {
                Console.WriteLine($"[FreeDuel] AI error: {ex.Message}");
                return ("（AI 暫時無法回應）", state);
            }

            // Parse AI response
            FreeDuelAiTurn? parsed = null;
            string dialogue = raw;
            try
            {
                var jsonStr = ExtractJson(raw);
                if (!string.IsNullOrEmpty(jsonStr))
                {
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    parsed = JsonSerializer.Deserialize<FreeDuelAiTurn>(jsonStr, opts);
                    if (parsed != null) dialogue = parsed.Dialogue;
                }
            }
            catch (Exception ex) { Console.WriteLine($"[FreeDuel] JSON parse failed: {ex.Message}"); }

            // Apply events to state
            if (parsed?.Events != null)
                foreach (var ev in parsed.Events)
                    ApplyEvent(state, ev);

            // 手牌有變動 → 更新手牌訊息
            await RefreshHandDisplayAsync(state);

            // Update history
            state.History.Add(new FreeDuelMessage { Role = "user", Content = userContent });
            state.History.Add(new FreeDuelMessage { Role = "assistant", Content = dialogue });
            if (state.History.Count > 16) state.History.RemoveRange(0, state.History.Count - 16);
            state.TurnNumber++;

            return (dialogue, state);
        }

        // ── Event applicator ───────────────────────────────────────────────
        private static void ApplyEvent(FreeDuelState s, FreeDuelEvent ev)
        {
            bool isPlayer = ev.Target == "player";
            try
            {
                switch (ev.Type)
                {
                    case "draw":
                        if (isPlayer && !string.IsNullOrEmpty(ev.Card))
                            s.PlayerHand.Add(ev.Card);
                        else if (!isPlayer)
                            s.AiHandCount = Math.Max(0, s.AiHandCount + 1);
                        break;

                    case "summon":
                        if (isPlayer) { s.PlayerHand.Remove(ev.Card); s.PlayerField.Add(new FreeDuelCardOnField { Name = ev.Card, Atk = ev.Atk, Def = ev.Def, IsDefense = ev.Position == "defense" }); }
                        else { s.AiHandCount = Math.Max(0, s.AiHandCount - 1); s.AiField.Add(new FreeDuelCardOnField { Name = ev.Card, Atk = ev.Atk, Def = ev.Def, IsDefense = ev.Position == "defense" }); }
                        break;

                    case "set_monster":
                        if (isPlayer) { s.PlayerHand.Remove(ev.Card); s.PlayerField.Add(new FreeDuelCardOnField { Name = ev.Card, FaceDown = true, IsDefense = true }); }
                        else { s.AiHandCount = Math.Max(0, s.AiHandCount - 1); s.AiField.Add(new FreeDuelCardOnField { Name = "？", FaceDown = true, IsDefense = true }); }
                        break;

                    case "set_st":
                        if (isPlayer) s.PlayerHand.Remove(ev.Card);
                        else s.AiHandCount = Math.Max(0, s.AiHandCount - 1);
                        break;

                    case "activate_spell":
                        if (isPlayer) { s.PlayerHand.Remove(ev.Card); s.PlayerGraveyard.Add(ev.Card); }
                        else { s.AiHandCount = Math.Max(0, s.AiHandCount - 1); s.AiGraveyard.Add(ev.Card); }
                        break;

                    case "flip":
                        var flipList = isPlayer ? s.PlayerField : s.AiField;
                        var flipCard = flipList.FirstOrDefault(c => c.FaceDown);
                        if (flipCard != null) { flipCard.FaceDown = false; flipCard.Name = ev.Card; flipCard.Atk = ev.Atk; flipCard.Def = ev.Def; }
                        break;

                    case "destroy":
                        if (ev.Zone == "hand") { if (isPlayer) s.PlayerHand.Remove(ev.Card); else s.AiHandCount = Math.Max(0, s.AiHandCount - 1); }
                        else
                        {
                            var fList = isPlayer ? s.PlayerField : s.AiField;
                            var gy = isPlayer ? s.PlayerGraveyard : s.AiGraveyard;
                            var toRemove = fList.FirstOrDefault(c => c.Name == ev.Card || (ev.Card == "？" && c.FaceDown));
                            if (toRemove != null) { fList.Remove(toRemove); if (!toRemove.FaceDown) gy.Add(toRemove.Name); else gy.Add(ev.Card); }
                        }
                        break;

                    case "discard":
                        if (isPlayer) { s.PlayerHand.Remove(ev.Card); s.PlayerGraveyard.Add(ev.Card); }
                        else { s.AiHandCount = Math.Max(0, s.AiHandCount - 1); s.AiGraveyard.Add(ev.Card); }
                        break;

                    case "damage":
                        if (isPlayer) s.PlayerHp = Math.Max(0, s.PlayerHp - ev.Amount);
                        else s.AiHp = Math.Max(0, s.AiHp - ev.Amount);
                        if (s.PlayerHp == 0 || s.AiHp == 0) { s.IsDuelEnded = true; s.Winner = s.PlayerHp == 0 ? "ai" : "player"; }
                        break;

                    case "heal":
                        if (isPlayer) s.PlayerHp = Math.Min(8000, s.PlayerHp + ev.Amount);
                        else s.AiHp = Math.Min(8000, s.AiHp + ev.Amount);
                        break;

                    case "attack":
                        // Calculate battle damage
                        var atkCard = ev.AttackerOwner == "player"
                            ? s.PlayerField.FirstOrDefault(c => c.Name == ev.Attacker)
                            : s.AiField.FirstOrDefault(c => c.Name == ev.Attacker);
                        if (atkCard != null)
                        {
                            if (string.IsNullOrEmpty(ev.Defender))
                            {
                                // Direct attack
                                int directDmg = atkCard.Atk;
                                if (ev.DefenderOwner == "player" || ev.AttackerOwner == "ai") s.PlayerHp = Math.Max(0, s.PlayerHp - directDmg);
                                else s.AiHp = Math.Max(0, s.AiHp - directDmg);
                            }
                            // Actual destruction is handled by separate destroy events
                        }
                        if (s.PlayerHp == 0 || s.AiHp == 0) { s.IsDuelEnded = true; s.Winner = s.PlayerHp == 0 ? "ai" : "player"; }
                        break;

                    case "end_duel":
                        s.IsDuelEnded = true;
                        s.Winner = ev.Winner;
                        break;
                }
            }
            catch (Exception ex) { Console.WriteLine($"[FreeDuel] ApplyEvent error ({ev.Type}): {ex.Message}"); }
        }

        // ── Prompt builders ────────────────────────────────────────────────
        private static string BuildSystemPrompt(FreeDuelState s)
        {
            var ch = Chars.TryGetValue(s.AiCharacterKey, out var c) ? c : Chars["kaiba"];
            return $@"你是遊戲王中的決鬥者【{ch.Name}】，正在與【{s.PlayerName}】進行一場遊戲王卡片決鬥。

【角色個性】{ch.Personality}
【你的牌組風格】{ch.DeckHint}

【決鬥規則】
- 雙方初始 LP 8000，先降至 0 者敗
- 玩家用文字描述行動，你判斷合理性後回應，並執行自己的回合
- 你是裁判也是對手，公平計算傷害（ATK 差距 = 傷害）
- 榮譽制：相信玩家宣告的手牌，但可以偶爾懷疑或嘲諷
- 你的手牌玩家看不到，由你自行決定抽到什麼

【回應格式 — 只回傳 JSON，不要有任何其他文字】
{{
  ""dialogue"": ""你的台詞與決鬥描述（包含你自己這回合的所有行動）"",
  ""events"": [
    {{""type"":""draw"",""target"":""ai""}},
    {{""type"":""summon"",""target"":""ai"",""card"":""青眼白龍"",""atk"":3000,""def"":2500,""position"":""attack""}},
    {{""type"":""attack"",""attacker_owner"":""ai"",""attacker"":""青眼白龍"",""defender_owner"":""player"",""defender"":""暗黑騎士蓋亞""}},
    {{""type"":""destroy"",""target"":""player"",""card"":""暗黑騎士蓋亞"",""zone"":""field""}},
    {{""type"":""damage"",""target"":""player"",""amount"":700}}
  ]
}}

事件類型說明：
- draw: 抽牌（player/ai）
- summon: 正面召喚怪獸（需 card/atk/def/position）
- set_monster: 背面覆蓋怪獸（AI 的 card 可以不填）
- set_st: 覆蓋魔陷（AI 的 card 可以不填）
- activate_spell: 發動魔法（card=牌名）
- attack: 攻擊（attacker_owner/attacker/defender_owner/defender，直接攻擊時 defender 留空）
- destroy: 破壞（target=被破壞方，card=牌名，zone=field/hand）
- damage: 傷害（target=受傷方，amount=數值）
- heal: 回復 LP（target/amount）
- discard: 棄牌（target/card）
- end_duel: 決鬥結束（winner=player/ai）

【重要】只列出確實發生的事件。events 可以是空陣列。";
        }

        private static string BuildTurnContext(FreeDuelState s, string playerAction)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【當前場面】");
            sb.AppendLine($"回合 {s.TurnNumber}  |  玩家 LP: {s.PlayerHp}  |  {s.AiCharacterName} LP: {s.AiHp}");
            sb.AppendLine($"玩家手牌（{s.PlayerHand.Count}張）: {(s.PlayerHand.Any() ? string.Join("、", s.PlayerHand) : "空")}");

            sb.AppendLine("玩家場上: " + (s.PlayerField.Any()
                ? string.Join("、", s.PlayerField.Select(c => c.FaceDown ? "背面覆蓋" : $"{c.Name} ATK{c.Atk}/{(c.IsDefense ? "守" : "攻")}"))
                : "空"));
            sb.AppendLine($"{s.AiCharacterName}場上: " + (s.AiField.Any()
                ? string.Join("、", s.AiField.Select(c => c.FaceDown ? "背面覆蓋" : $"{c.Name} ATK{c.Atk}/{(c.IsDefense ? "守" : "攻")}"))
                : "空"));
            sb.AppendLine($"玩家墓地: {(s.PlayerGraveyard.Any() ? string.Join("、", s.PlayerGraveyard.TakeLast(5)) : "空")}");
            sb.AppendLine($"{s.AiCharacterName}墓地: {(s.AiGraveyard.Any() ? string.Join("、", s.AiGraveyard.TakeLast(5)) : "空")}");
            sb.AppendLine($"{s.AiCharacterName}手牌數: {s.AiHandCount}");
            sb.AppendLine();
            sb.AppendLine($"【玩家行動】: {playerAction}");
            sb.AppendLine();
            sb.AppendLine("根據玩家行動做出反應，然後執行你自己的回合行動。回傳 JSON。");
            return sb.ToString();
        }

        // ── Helpers ────────────────────────────────────────────────────────
        private static string ExtractJson(string raw)
        {
            raw = raw.Trim();
            int start = raw.IndexOf('{');
            int end   = raw.LastIndexOf('}');
            if (start >= 0 && end > start) return raw.Substring(start, end - start + 1);
            return "";
        }

        private async Task<string> TryGetCardImageAsync(string cardName)
        {
            try
            {
                var data = await _ygoSvc.FetchCardAsync(cardName);
                return data?.CardImages?.FirstOrDefault()?.ImageUrl ?? "";
            }
            catch { return ""; }
        }

        // ── Embed / UI builders ────────────────────────────────────────────
        public static Embed BuildBoardEmbed(FreeDuelState s)
        {
            var color = s.IsDuelEnded ? Color.DarkRed : new Color(0xFFD700);
            var eb = new EmbedBuilder().WithTitle($"⚔️ 自由決鬥  回合 {s.TurnNumber}").WithColor(color);

            eb.AddField($"🧑 {s.PlayerName}", $"{HpBar(s.PlayerHp)} **{s.PlayerHp} LP**", inline: true);
            eb.AddField($"🤖 {s.AiCharacterName}", $"{HpBar(s.AiHp)} **{s.AiHp} LP**", inline: true);
            eb.AddField("​", "​", inline: false);

            eb.AddField("🧑 場上", s.PlayerField.Any()
                ? string.Join("\n", s.PlayerField.Select(c => c.FaceDown ? "▣ 背面" : $"▲ {c.Name} {(c.IsDefense ? "DEF" : "ATK")}{c.Atk}"))
                : "（空）", inline: true);
            eb.AddField("🤖 場上", s.AiField.Any()
                ? string.Join("\n", s.AiField.Select(c => c.FaceDown ? "▣ 背面" : $"▲ {c.Name} {(c.IsDefense ? "DEF" : "ATK")}{c.Atk}"))
                : "（空）", inline: true);
            eb.AddField("​", "​", inline: false);

            eb.AddField("🧑 手牌", $"{s.PlayerHand.Count} 張", inline: true);
            eb.AddField("🤖 手牌", $"{s.AiHandCount} 張（未知）", inline: true);

            if (s.PlayerGraveyard.Any() || s.AiGraveyard.Any())
            {
                eb.AddField("🧑 墓地", s.PlayerGraveyard.Any() ? string.Join("、", s.PlayerGraveyard.TakeLast(3)) : "空", inline: true);
                eb.AddField("🤖 墓地", s.AiGraveyard.Any() ? string.Join("、", s.AiGraveyard.TakeLast(3)) : "空", inline: true);
            }

            if (s.IsDuelEnded)
            {
                string w = s.Winner == "player" ? $"🏆 {s.PlayerName} 勝利！" : $"🏆 {s.AiCharacterName} 勝利！";
                eb.AddField("決鬥結果", w);
                eb.WithFooter("決鬥已結束，頻道回復正常");
            }
            else eb.WithFooter("直接輸入你的決鬥行動 | /endduel 強制結束");

            return eb.Build();
        }

        public static ComponentBuilder BuildBoardButtons(FreeDuelState s)
        {
            if (s.IsDuelEnded) return new ComponentBuilder();
            return new ComponentBuilder()
                .WithButton("🃏 查看手牌", $"ygo_fdhand_{s.ChannelId}", ButtonStyle.Secondary);
        }

        /// <summary>若有手牌訊息存在，自動更新顯示（回合結束/手牌變動後呼叫）</summary>
        private async Task RefreshHandDisplayAsync(FreeDuelState state)
        {
            if (state.HandMessageId == 0) return;
            try
            {
                if (_client.GetChannel(state.ChannelId) is not IMessageChannel ch) return;
                if (await ch.GetMessageAsync(state.HandMessageId) is not IUserMessage msg) return;
                var (handEmbed, handComp) = await GetFreeDuelHandEmbedAsync(state.ChannelId, state.PlayerId, 0);
                await msg.ModifyAsync(m => { m.Embed = handEmbed; m.Components = handComp.Build(); });
            }
            catch { /* 手牌更新失敗不影響主流程 */ }
        }

        private static Embed BuildError(string msg) =>
            new EmbedBuilder().WithTitle("❌ 錯誤").WithDescription(msg).WithColor(Color.Red).Build();

        private static string HpBar(int hp)
        {
            int f = Math.Clamp((int)Math.Round(hp / 800.0), 0, 10);
            return new string('█', f) + new string('░', 10 - f);
        }
    }
}
