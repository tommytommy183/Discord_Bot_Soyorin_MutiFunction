using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using MusicBot2.Helpers;
using MusicBot2.Models;
using StackExchange.Redis;

namespace MusicBot2.Service
{
    public class YgoDuelService
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private readonly IDatabase? _redisDb;
        private readonly bool _useRedis;
        private readonly OpenRouterService _ai;
        private readonly Random _rng = new();

        private const string DUEL_KEY   = "ygo:duel:";
        private const string CHAN_KEY   = "ygo:channel:";
        private const string CARD_KEY   = "ygo:card:";
        private const string API_BASE   = "https://db.ygoprodeck.com/api/v7/cardinfo.php";

        private static readonly Dictionary<ulong, YgoDuelState> _memDuels = new();
        private static readonly Dictionary<string, YgoCardData> _cardCache = new();

        // ─────────────────────────────────────────────────────────────────
        // 動漫牌組清單
        // ─────────────────────────────────────────────────────────────────
        private static readonly Dictionary<string, AnimeDeckDefinition> _decks = BuildDecks();

        public YgoDuelService(string? redisConn, OpenRouterService ai)
        {
            _ai = ai;
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

        // =================================================================
        // PUBLIC API
        // =================================================================

        /// <summary>挑戰 AI，選擇自己和 AI 各自使用的牌組</summary>
        public async Task<(Embed embed, ComponentBuilder component)> StartPvAiDuelAsync(
            ulong channelId, SocketGuildUser player, string playerDeckKey, string aiDeckKey)
        {
            try
            {
                // 確保沒有進行中的決鬥
                var existing = await LoadDuelAsync(channelId);
                if (existing != null && existing.IsActive)
                    return Error("這個頻道已有決鬥進行中！");

                if (!_decks.ContainsKey(playerDeckKey)) return Error($"找不到牌組：{playerDeckKey}");
                if (!_decks.ContainsKey(aiDeckKey))     return Error($"找不到牌組：{aiDeckKey}");

                var duel = new YgoDuelState
                {
                    DuelId = $"{player.Id}_ai",
                    ChannelId = channelId,
                    IsAiDuel = true,
                    Field1 = new YgoPlayerField
                    {
                        UserId = player.Id,
                        UserName = player.DisplayName,
                        IsAi = false,
                        DeckName = playerDeckKey,
                    },
                    Field2 = new YgoPlayerField
                    {
                        UserId = 0,
                        UserName = _decks[aiDeckKey].CharacterName,
                        IsAi = true,
                        DeckName = aiDeckKey,
                    },
                    CurrentTurnPlayerId = player.Id,
                };

                // 建立牌組
                duel.Field1.Deck = await BuildDeckAsync(playerDeckKey);
                duel.Field1.ExtraDeck = await BuildExtraDeckAsync(playerDeckKey);
                duel.Field2.Deck = await BuildDeckAsync(aiDeckKey);
                duel.Field2.ExtraDeck = await BuildExtraDeckAsync(aiDeckKey);

                // 洗牌並發初始手牌
                Shuffle(duel.Field1.Deck);
                Shuffle(duel.Field2.Deck);
                DrawCards(duel.Field1, 5);
                DrawCards(duel.Field2, 5);

                // 先手第一回合進入 Main Phase 1（跳過 Draw + Battle）
                duel.CurrentPhase = DuelPhase.MainPhase1;
                duel.Field1.NormalSummonedThisTurn = false;

                duel.AddLog($"🎴 決鬥開始！{duel.Field1.UserName} vs {duel.Field2.UserName}");
                duel.AddLog($"▶ {duel.Field1.UserName} 的先手！");

                await SaveDuelAsync(channelId, duel);

                return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
            }
            catch (Exception ex)
            {
                return Error($"建立決鬥失敗：{ex.Message}");
            }
        }

        /// <summary>列出所有動漫牌組</summary>
        public (Embed embed, ComponentBuilder component) ListDecks()
        {
            var eb = new EmbedBuilder()
                .WithTitle("🃏 可用動漫牌組")
                .WithColor(Color.Gold)
                .WithDescription("使用 `/決鬥ai` 選擇牌組挑戰動漫角色！");

            foreach (var kv in _decks)
            {
                var d = kv.Value;
                eb.AddField(
                    $"{d.Emoji} {d.CharacterName}（{d.Series}）  `{d.Key}`",
                    $"主牌 {d.MainDeckNames.Count} 張  ·  額外牌組 {d.ExtraDeckNames.Count} 張",
                    inline: true);
            }
            return (eb.Build(), new ComponentBuilder());
        }

        /// <summary>顯示當前決鬥場地</summary>
        public async Task<(Embed embed, ComponentBuilder component)> GetBoardAsync(ulong channelId)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("此頻道沒有進行中的決鬥。");
            return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
        }

        /// <summary>顯示玩家手牌（含看牌圖按鈕）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> GetHandEmbedAsync(
            ulong channelId, ulong userId)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("此頻道沒有進行中的決鬥。");

            var field = duel.Field1.UserId == userId ? duel.Field1 : duel.Field2;
            var embed = BuildHandEmbed(field);
            var cb    = BuildHandImageButtons(duel.DuelId, field);
            return (embed, cb);
        }

        /// <summary>顯示特定手牌卡圖（ephemeral）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> ShowCardImageAsync(
            ulong channelId, ulong userId, int handIndex)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");

            var field = duel.Field1.UserId == userId ? duel.Field1 : duel.Field2;
            if (handIndex < 0 || handIndex >= field.Hand.Count) return Error("無效的索引。");
            var card = field.Hand[handIndex];

            string imgUrl = card.RareImageUrl;
            if (string.IsNullOrWhiteSpace(imgUrl)) imgUrl = card.ImageUrl;

            var eb = new EmbedBuilder()
                .WithTitle($"🖼️ {card.Name}")
                .WithColor(card.IsMonster ? new Color(0xFFD700) :
                           card.IsSpell   ? new Color(0x1DB954) : new Color(0xE74C3C))
                .WithDescription(card.IsMonster
                    ? $"ATK {card.Atk} / DEF {card.Def}  ★{card.Level}  {card.Attribute}/{card.Race}"
                    : card.Type);

            if (!string.IsNullOrWhiteSpace(imgUrl))
                eb.WithImageUrl(imgUrl);

            return (eb.Build(), new ComponentBuilder());
        }

        /// <summary>抽牌</summary>
        public async Task<(Embed embed, ComponentBuilder component)> DrawCardAsync(
            ulong channelId, ulong userId)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你的回合！");
            if (duel.CurrentPhase != DuelPhase.DrawPhase) return Error("現在不是抽牌階段。");
            if (duel.CurrentField.DrewThisTurn) return Error("本回合已抽過牌了。");

            var field = duel.CurrentField;
            if (field.Deck.Count == 0)
            {
                duel.IsActive = false;
                duel.WinnerName = duel.OpponentField.UserName;
                duel.CurrentPhase = DuelPhase.GameOver;
                duel.AddLog($"💀 {field.UserName} 牌組耗盡！{duel.WinnerName} 獲勝！");
                await SaveDuelAsync(channelId, duel);
                return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
            }

            var card = field.Deck[0];
            field.Deck.RemoveAt(0);
            field.Hand.Add(card);
            field.DrewThisTurn = true;
            duel.CurrentPhase = DuelPhase.StandbyPhase;
            duel.AddLog($"🎴 {field.UserName} 抽了一張牌（手牌 {field.HandCount} 張）");

            await SaveDuelAsync(channelId, duel);
            return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
        }

        /// <summary>進入下一個 Phase</summary>
        public async Task<(Embed embed, ComponentBuilder component)> AdvancePhaseAsync(
            ulong channelId, ulong userId)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");

            var next = NextPhase(duel);
            duel.CurrentPhase = next;
            duel.AddLog($"▶ 進入 **{PhaseLabel(next)}**");

            if (next == DuelPhase.EndPhase)
            {
                await DoEndPhase(duel);
            }

            await SaveDuelAsync(channelId, duel);

            // 如果切換到 AI 回合，執行 AI
            if (duel.IsAiDuel && duel.CurrentTurnPlayerId == duel.Field2.UserId && duel.IsActive)
            {
                await Task.Delay(1500);
                return await ExecuteAiTurnAsync(channelId);
            }

            return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
        }

        /// <summary>直接結束回合</summary>
        public async Task<(Embed embed, ComponentBuilder component)> EndTurnAsync(
            ulong channelId, ulong userId)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");

            await DoEndPhase(duel);
            duel.AddLog($"⏩ {duel.CurrentField.UserName} 結束回合");
            SwitchTurn(duel);
            duel.AddLog($"▶ {duel.CurrentField.UserName} 的第 {duel.TurnNumber} 回合開始！");

            await SaveDuelAsync(channelId, duel);

            if (duel.IsAiDuel && duel.CurrentTurnPlayerId == duel.Field2.UserId && duel.IsActive)
            {
                await Task.Delay(1500);
                return await ExecuteAiTurnAsync(channelId);
            }

            return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
        }

        /// <summary>通常召喚（含貢獻）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> NormalSummonAsync(
            ulong channelId, ulong userId, int handIndex)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");
            if (duel.CurrentPhase != DuelPhase.MainPhase1 && duel.CurrentPhase != DuelPhase.MainPhase2)
                return Error("只能在主要階段召喚。");

            var field = duel.CurrentField;
            if (field.NormalSummonedThisTurn) return Error("本回合已通常召喚過了。");
            if (handIndex < 0 || handIndex >= field.Hand.Count) return Error("無效的手牌索引。");

            var card = field.Hand[handIndex];
            if (!card.IsMonster) return Error($"{card.Name} 不是怪獸牌！");

            int tribute = card.TributeRequired;
            int monstersOnField = field.GetMonstersOnField().Count;

            // 需要貢獻但場上怪獸不夠
            if (tribute > 0 && monstersOnField < tribute)
                return Error($"召喚 {card.Name}（Lv{card.Level}）需要 {tribute} 隻怪獸作為貢獻，但場上只有 {monstersOnField} 隻。");

            // 需要貢獻 → 進入選擇流程
            if (tribute > 0)
            {
                duel.PendingSummonHandIndex = handIndex;
                duel.PendingTributeZones = new List<int>();
                await SaveDuelAsync(channelId, duel);
                return (BuildTributeSelectEmbed(duel, card, tribute), BuildTributeButtons(duel));
            }

            // 直接召喚（Lv 1-4）
            if (field.FirstEmptyMonsterZone() == -1) return Error("怪獸區已滿！");
            PlaceMonster(field, card, handIndex);
            field.NormalSummonedThisTurn = true;
            duel.LastPlayedCardImageUrl = card.RareImageUrl;
            duel.AddLog($"⬆️ {field.UserName} 通常召喚 **{card.Name}** (ATK {card.Atk})");

            await SaveDuelAsync(channelId, duel);
            return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
        }

        /// <summary>選擇貢獻怪獸（可多步）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> SelectTributeAsync(
            ulong channelId, ulong userId, int zone)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null || !duel.PendingSummonHandIndex.HasValue) return Error("沒有待完成的貢獻召喚。");

            var field = duel.CurrentField;
            if (zone < 0 || zone >= 5 || field.MonsterZones[zone] == null)
                return Error("無效的格子。");
            if (duel.PendingTributeZones.Contains(zone)) return Error("已選過這隻怪獸了。");

            // 送墓
            var sacrificed = field.MonsterZones[zone]!;
            field.MonsterZones[zone] = null;
            field.Graveyard.Add(sacrificed);
            duel.PendingTributeZones.Add(zone);
            duel.AddLog($"🔥 {field.UserName} 獻祭了 {sacrificed.Name}");

            var summonCard = field.Hand[duel.PendingSummonHandIndex.Value];
            int needed = summonCard.TributeRequired - duel.PendingTributeZones.Count;

            if (needed > 0)
            {
                // 還要再選
                await SaveDuelAsync(channelId, duel);
                return (BuildTributeSelectEmbed(duel, summonCard, needed), BuildTributeButtons(duel));
            }

            // 貢獻完成，召喚
            PlaceMonster(field, summonCard, duel.PendingSummonHandIndex.Value);
            field.NormalSummonedThisTurn = true;
            duel.LastPlayedCardImageUrl = summonCard.RareImageUrl;
            duel.PendingSummonHandIndex = null;
            duel.PendingTributeZones = new();
            duel.AddLog($"⬆️ {field.UserName} 貢獻召喚 **{summonCard.Name}** (ATK {summonCard.Atk})");

            await SaveDuelAsync(channelId, duel);
            return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
        }

        /// <summary>覆蓋怪獸（守備覆蓋）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> SetMonsterAsync(
            ulong channelId, ulong userId, int handIndex)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");
            if (duel.CurrentPhase != DuelPhase.MainPhase1 && duel.CurrentPhase != DuelPhase.MainPhase2)
                return Error("只能在主要階段覆蓋。");

            var field = duel.CurrentField;
            if (field.NormalSummonedThisTurn) return Error("本回合已通常召喚/覆蓋過了。");
            if (handIndex < 0 || handIndex >= field.Hand.Count) return Error("無效的索引。");
            var card = field.Hand[handIndex];
            if (!card.IsMonster) return Error($"{card.Name} 不是怪獸！");
            if (field.FirstEmptyMonsterZone() == -1) return Error("怪獸區已滿！");

            var placed = card.Clone();
            placed.FaceDown = true;
            placed.IsDefensePosition = true;
            placed.SummonedThisTurn = true;
            field.Hand.RemoveAt(handIndex);
            int slot = field.FirstEmptyMonsterZone();
            while (field.MonsterZones.Count <= slot) field.MonsterZones.Add(null);
            field.MonsterZones[slot] = placed;
            field.NormalSummonedThisTurn = true;
            duel.AddLog($"🔽 {field.UserName} 將一張牌怪獸覆蓋");

            await SaveDuelAsync(channelId, duel);
            return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
        }

        /// <summary>覆蓋魔陷</summary>
        public async Task<(Embed embed, ComponentBuilder component)> SetSpellTrapAsync(
            ulong channelId, ulong userId, int handIndex)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");
            if (duel.CurrentPhase != DuelPhase.MainPhase1 && duel.CurrentPhase != DuelPhase.MainPhase2)
                return Error("只能在主要階段覆蓋。");

            var field = duel.CurrentField;
            if (handIndex < 0 || handIndex >= field.Hand.Count) return Error("無效的索引。");
            var card = field.Hand[handIndex];
            if (card.IsMonster) return Error($"{card.Name} 是怪獸牌，請使用召喚指令。");
            if (field.FirstEmptySTZone() == -1) return Error("魔陷區已滿！");

            var placed = card.Clone();
            placed.FaceDown = true;
            field.Hand.RemoveAt(handIndex);
            int slot = field.FirstEmptySTZone();
            while (field.SpellTrapZones.Count <= slot) field.SpellTrapZones.Add(null);
            field.SpellTrapZones[slot] = placed;
            duel.AddLog($"🔽 {field.UserName} 覆蓋了一張 {(card.IsSpell ? "魔法" : "陷阱")}");

            await SaveDuelAsync(channelId, duel);
            return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
        }

        /// <summary>統一覆蓋（自動判斷怪獸 or 魔陷）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> SetCardAsync(
            ulong channelId, ulong userId, int handIndex)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");
            var field = duel.CurrentField;
            if (handIndex < 0 || handIndex >= field.Hand.Count) return Error("無效的手牌索引。");
            return field.Hand[handIndex].IsMonster
                ? await SetMonsterAsync(channelId, userId, handIndex)
                : await SetSpellTrapAsync(channelId, userId, handIndex);
        }

        /// <summary>顯示手牌供選擇召喚</summary>
        public async Task<(Embed embed, ComponentBuilder component)> ShowHandForSummonAsync(
            ulong channelId, ulong userId)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");
            if (duel.CurrentPhase != DuelPhase.MainPhase1 && duel.CurrentPhase != DuelPhase.MainPhase2)
                return Error("只能在主要階段召喚。");
            if (duel.CurrentField.NormalSummonedThisTurn) return Error("本回合已通常召喚過了。");

            var hand = duel.CurrentField.Hand;
            var monsters = hand.Select((c, i) => (c, i)).Where(x => x.c.IsMonster).ToList();
            if (!monsters.Any()) return Error("手牌中沒有怪獸牌！");

            var eb = new EmbedBuilder()
                .WithTitle("⬆️ 選擇要召喚的怪獸")
                .WithColor(Color.Gold);
            foreach (var (c, i) in monsters)
                eb.AddField($"{i+1}. {c.Name}",
                    $"ATK {c.Atk} / DEF {c.Def}  Lv{c.Level}" +
                    (c.TributeRequired > 0 ? $"  ⚠️需{c.TributeRequired}貢獻" : "  ✅可直接召喚"),
                    inline: true);

            var cb  = new ComponentBuilder();
            var row = new ActionRowBuilder();
            int cnt = 0;
            foreach (var (c, i) in monsters)
            {
                if (cnt == 5) { cb.AddRow(row); row = new ActionRowBuilder(); cnt = 0; }
                row.WithButton($"{i+1}.{c.ShortName}", $"ygo_ns_{duel.DuelId}_{i}", ButtonStyle.Primary);
                cnt++;
            }
            cb.AddRow(row);
            return (eb.Build(), cb);
        }

        /// <summary>顯示手牌供選擇覆蓋</summary>
        public async Task<(Embed embed, ComponentBuilder component)> ShowHandForSetAsync(
            ulong channelId, ulong userId)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");
            if (duel.CurrentPhase != DuelPhase.MainPhase1 && duel.CurrentPhase != DuelPhase.MainPhase2)
                return Error("只能在主要階段覆蓋。");

            var hand = duel.CurrentField.Hand;
            if (!hand.Any()) return Error("手牌是空的！");

            var eb = new EmbedBuilder()
                .WithTitle("🔽 選擇要覆蓋的牌（怪獸=守備覆蓋，魔陷=覆蓋魔陷）")
                .WithColor(Color.DarkGrey);
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                string kind = c.IsMonster ? $"怪獸 Lv{c.Level} DEF{c.Def}" : (c.IsSpell ? "魔法" : "陷阱");
                eb.AddField($"{i+1}. {c.Name}", kind, inline: true);
            }

            var cb  = new ComponentBuilder();
            var row = new ActionRowBuilder();
            int cnt = 0;
            for (int i = 0; i < hand.Count; i++)
            {
                if (cnt == 5) { cb.AddRow(row); row = new ActionRowBuilder(); cnt = 0; }
                row.WithButton($"{i+1}.{hand[i].ShortName}", $"ygo_sc_{duel.DuelId}_{i}", ButtonStyle.Secondary);
                cnt++;
            }
            cb.AddRow(row);
            return (eb.Build(), cb);
        }

        /// <summary>顯示手牌供選擇發動魔法</summary>
        public async Task<(Embed embed, ComponentBuilder component)> ShowHandForActivateAsync(
            ulong channelId, ulong userId)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");

            var hand = duel.CurrentField.Hand;
            var spells = hand.Select((c, i) => (c, i)).Where(x => x.c.IsSpell).ToList();
            if (!spells.Any()) return Error("手牌中沒有魔法牌可以發動！");

            var eb = new EmbedBuilder()
                .WithTitle("✨ 選擇要發動的魔法")
                .WithColor(new Color(0x1DB954));
            foreach (var (c, i) in spells)
                eb.AddField($"{i+1}. {c.Name}", c.Desc?.Split('.').FirstOrDefault() ?? c.Race, inline: true);

            var cb  = new ComponentBuilder();
            var row = new ActionRowBuilder();
            int cnt = 0;
            foreach (var (c, i) in spells)
            {
                if (cnt == 5) { cb.AddRow(row); row = new ActionRowBuilder(); cnt = 0; }
                row.WithButton($"{i+1}.{c.ShortName}", $"ygo_act_{duel.DuelId}_{i}", ButtonStyle.Success);
                cnt++;
            }
            cb.AddRow(row);
            return (eb.Build(), cb);
        }

        /// <summary>顯示場上怪獸供選擇攻擊方</summary>
        public async Task<(Embed embed, ComponentBuilder component)> ShowMonstersForAttackAsync(
            ulong channelId, ulong userId)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");
            if (duel.CurrentPhase != DuelPhase.BattlePhase) return Error("只能在戰鬥階段攻擊。");

            var field = duel.CurrentField;
            var atkers = field.MonsterZones
                .Select((c, i) => (c, i))
                .Where(x => x.c != null && !x.c.SummonedThisTurn &&
                            !x.c.AttackedThisTurn && !x.c.IsDefensePosition)
                .ToList();

            if (!atkers.Any()) return Error("沒有可以攻擊的怪獸！（召喚病、已攻擊、或守備表示）");

            var eb = new EmbedBuilder()
                .WithTitle("⚔️ 選擇要發動攻擊的怪獸")
                .WithColor(Color.Red);
            foreach (var (c, i) in atkers)
                eb.AddField($"格子 {i+1}：{c!.Name}", $"ATK {c.EffectiveAtk}", inline: true);

            var cb  = new ComponentBuilder();
            var row = new ActionRowBuilder();
            foreach (var (c, i) in atkers)
                row.WithButton($"[{i+1}]{c!.ShortName}", $"ygo_atkselect_{duel.DuelId}_{i}", ButtonStyle.Danger);
            cb.AddRow(row);
            return (eb.Build(), cb);
        }

        /// <summary>發動魔法（從手牌）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> ActivateSpellAsync(
            ulong channelId, ulong userId, int handIndex)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");

            var field = duel.CurrentField;
            if (handIndex < 0 || handIndex >= field.Hand.Count) return Error("無效的索引。");
            var card = field.Hand[handIndex];
            if (!card.IsSpell) return Error($"{card.Name} 不是魔法！");

            field.Hand.RemoveAt(handIndex);
            var msg = ApplySpellEffect(card, duel);
            field.Graveyard.Add(card);
            duel.LastPlayedCardImageUrl = card.RareImageUrl;
            duel.AddLog($"✨ {field.UserName} 發動 **{card.Name}**");
            if (!string.IsNullOrWhiteSpace(msg)) duel.AddLog($"　→ {msg}");

            var (ended, winner) = CheckGameOver(duel);
            if (ended)
            {
                duel.IsActive = false;
                duel.WinnerName = winner;
                duel.CurrentPhase = DuelPhase.GameOver;
            }

            await SaveDuelAsync(channelId, duel);
            return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
        }

        /// <summary>選擇攻擊方（第一步）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> SelectAttackerAsync(
            ulong channelId, ulong userId, int attackerZone)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");
            if (duel.CurrentPhase != DuelPhase.BattlePhase) return Error("只能在戰鬥階段攻擊。");

            var field = duel.CurrentField;
            if (attackerZone < 0 || attackerZone >= 5 || field.MonsterZones[attackerZone] == null)
                return Error("無效的攻擊方格子。");

            var attacker = field.MonsterZones[attackerZone]!;
            if (attacker.SummonedThisTurn) return Error($"{attacker.ShortName} 本回合剛召喚，不能攻擊（召喚病）。");
            if (attacker.AttackedThisTurn) return Error($"{attacker.ShortName} 本回合已攻擊過了。");
            if (attacker.IsDefensePosition) return Error("守備表示的怪獸不能攻擊。");

            if (duel.Field1.UserId == userId && duel.Field1.SwordsCounter > 0 ||
                duel.Field2.UserId == userId && duel.Field2.SwordsCounter > 0)
                return Error("你的怪獸被光之護封劍封印，無法攻擊！");

            duel.PendingAttackerZone = attackerZone;
            await SaveDuelAsync(channelId, duel);

            var opp = duel.OpponentField;
            var hasMonsters = opp.MonsterZones.Any(c => c != null);
            return (BuildAttackTargetEmbed(duel, attacker, hasMonsters), BuildAttackTargetButtons(duel, hasMonsters));
        }

        /// <summary>確認攻擊目標（第二步）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> ConfirmAttackAsync(
            ulong channelId, ulong userId, int? targetZone)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null || !duel.PendingAttackerZone.HasValue) return Error("沒有選擇攻擊方。");

            var atkField = duel.CurrentField;
            var defField = duel.OpponentField;
            int aZone = duel.PendingAttackerZone.Value;
            var attacker = atkField.MonsterZones[aZone]!;
            duel.PendingAttackerZone = null;

            // 直接攻擊（targetZone = null）
            if (targetZone == null)
            {
                if (defField.GetMonstersOnField().Count > 0)
                    return Error("對方場上有怪獸，不能直接攻擊！");
                defField.LifePoints -= attacker.EffectiveAtk;
                attacker.AttackedThisTurn = true;
                duel.AddLog($"⚔️ {attacker.ShortName} 直接攻擊！{defField.UserName} -{attacker.EffectiveAtk} LP → **{defField.LifePoints}**");
            }
            else
            {
                int tZone = targetZone.Value;
                if (tZone < 0 || tZone >= 5 || defField.MonsterZones[tZone] == null)
                    return Error("無效的攻擊目標。");
                var defender = defField.MonsterZones[tZone]!;

                // 翻面
                if (defender.FaceDown)
                {
                    defender.FaceDown = false;
                    duel.LastPlayedCardImageUrl = defender.RareImageUrl;
                    duel.AddLog($"🔄 {defender.ShortName} 翻面！(DEF {defender.Def})");
                }

                ResolveBattle(attacker, defender, atkField, defField, aZone, tZone, duel);
            }

            var (ended, winner) = CheckGameOver(duel);
            if (ended)
            {
                duel.IsActive = false;
                duel.WinnerName = winner;
                duel.CurrentPhase = DuelPhase.GameOver;
                duel.AddLog($"🏆 **{winner} 獲勝！**");
            }

            await SaveDuelAsync(channelId, duel);
            return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
        }

        /// <summary>切換怪獸攻守表示</summary>
        public async Task<(Embed embed, ComponentBuilder component)> ChangePositionAsync(
            ulong channelId, ulong userId, int zone)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");
            if (duel.CurrentPhase != DuelPhase.MainPhase1 && duel.CurrentPhase != DuelPhase.MainPhase2)
                return Error("只能在主要階段更換表示。");

            var field = duel.CurrentField;
            if (zone < 0 || zone >= 5 || field.MonsterZones[zone] == null) return Error("無效格子。");
            var card = field.MonsterZones[zone]!;
            if (card.SummonedThisTurn) return Error("剛召喚的怪獸本回合不能更換表示方式。");

            card.IsDefensePosition = !card.IsDefensePosition;
            card.FaceDown = false;
            var modeStr = card.IsDefensePosition ? "守備表示" : "攻擊表示";
            duel.AddLog($"🔄 {field.UserName} 將 {card.ShortName} 更換為 {modeStr}");

            await SaveDuelAsync(channelId, duel);
            return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
        }

        /// <summary>投降</summary>
        public async Task<(Embed embed, ComponentBuilder component)> SurrenderAsync(
            ulong channelId, ulong userId)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");

            var loser = duel.Field1.UserId == userId ? duel.Field1 : duel.Field2;
            var winner = duel.Field1.UserId == userId ? duel.Field2 : duel.Field1;

            duel.IsActive = false;
            duel.WinnerName = winner.UserName;
            duel.CurrentPhase = DuelPhase.GameOver;
            duel.AddLog($"🏳️ {loser.UserName} 投降了！{winner.UserName} 獲勝！");

            await SaveDuelAsync(channelId, duel);
            return (BuildBoardEmbed(duel), new ComponentBuilder());
        }

        /// <summary>自然語言訊息處理</summary>
        public async Task<(Embed? embed, ComponentBuilder? component)> ProcessNlpAsync(
            ulong channelId, ulong userId, string message)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null || !duel.IsActive) return (null, null);

            // 只處理當前回合玩家（非 AI）
            if (duel.CurrentTurnPlayerId != userId) return (null, null);

            var m = message.Trim();

            // 結束回合
            if (Regex.IsMatch(m, @"結束回合|end\s*turn|turn\s*end|pass", RegexOptions.IgnoreCase))
                return await EndTurnAsync(channelId, userId);

            // 下一階段
            if (Regex.IsMatch(m, @"next\s*phase|下一個?階段|進入戰鬥|battle\s*phase", RegexOptions.IgnoreCase))
                return await AdvancePhaseAsync(channelId, userId);

            // 抽牌
            if (Regex.IsMatch(m, @"抽牌|draw|摸牌", RegexOptions.IgnoreCase))
                return await DrawCardAsync(channelId, userId);

            // 投降
            if (Regex.IsMatch(m, @"投降|surrender|認輸", RegexOptions.IgnoreCase))
                return await SurrenderAsync(channelId, userId);

            // 召喚 <卡名/數字>
            var summonMatch = Regex.Match(m, @"(召喚|summon|ns)\s*(.+)", RegexOptions.IgnoreCase);
            if (summonMatch.Success)
            {
                var target = summonMatch.Groups[2].Value.Trim();
                var idx = ParseCardTarget(target, duel.CurrentField.Hand);
                if (idx >= 0) return await NormalSummonAsync(channelId, userId, idx);
            }

            // 覆蓋 <卡名/數字>
            var setMatch = Regex.Match(m, @"(覆蓋|set)\s*(.+)", RegexOptions.IgnoreCase);
            if (setMatch.Success)
            {
                var target = setMatch.Groups[2].Value.Trim();
                var field  = duel.CurrentField;
                var idx    = ParseCardTarget(target, field.Hand);
                if (idx >= 0)
                {
                    if (field.Hand[idx].IsMonster) return await SetMonsterAsync(channelId, userId, idx);
                    else return await SetSpellTrapAsync(channelId, userId, idx);
                }
            }

            // 發動 <卡名/數字>
            var activateMatch = Regex.Match(m, @"(發動|activate|打出|play)\s*(.+)", RegexOptions.IgnoreCase);
            if (activateMatch.Success)
            {
                var target = activateMatch.Groups[2].Value.Trim();
                var idx    = ParseCardTarget(target, duel.CurrentField.Hand);
                if (idx >= 0) return await ActivateSpellAsync(channelId, userId, idx);
            }

            // 攻擊：「用 X 攻擊 Y」或「X 攻擊 Y」
            var atkMatch = Regex.Match(m,
                @"(?:用\s*)?(.+?)\s*(?:去|進行)?攻擊\s*(.*)", RegexOptions.IgnoreCase);
            if (atkMatch.Success)
            {
                var attackerStr = atkMatch.Groups[1].Value.Trim();
                var targetStr   = atkMatch.Groups[2].Value.Trim();
                var field = duel.CurrentField;
                int aZone = ParseZoneTarget(attackerStr, field.MonsterZones);
                if (aZone >= 0)
                {
                    // 先 select attacker
                    var selResult = await SelectAttackerAsync(channelId, userId, aZone);
                    if (!duel.PendingAttackerZone.HasValue) return selResult; // error

                    // 直接攻擊
                    if (string.IsNullOrWhiteSpace(targetStr) ||
                        Regex.IsMatch(targetStr, @"直接|direct", RegexOptions.IgnoreCase))
                        return await ConfirmAttackAsync(channelId, userId, null);

                    int tZone = ParseZoneTarget(targetStr, duel.OpponentField.MonsterZones);
                    if (tZone >= 0) return await ConfirmAttackAsync(channelId, userId, tZone);
                }
            }

            return (null, null);
        }

        // =================================================================
        // AI TURN
        // =================================================================

        public async Task<(Embed embed, ComponentBuilder component)> ExecuteAiTurnAsync(ulong channelId)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null || !duel.IsActive) return Error("沒有決鬥。");

            var aiField  = duel.Field2; // AI is always field2
            var opp      = duel.Field1;
            var deckDef  = _decks.TryGetValue(aiField.DeckName, out var d) ? d : null;

            // Draw Phase
            if (duel.TurnNumber > 1 && aiField.Deck.Count > 0)
            {
                var drawn = aiField.Deck[0];
                aiField.Deck.RemoveAt(0);
                aiField.Hand.Add(drawn);
                duel.AddLog($"🎴 {aiField.UserName} 抽了一張牌");
            }
            else if (aiField.Deck.Count == 0)
            {
                duel.IsActive = false;
                duel.WinnerName = opp.UserName;
                duel.CurrentPhase = DuelPhase.GameOver;
                duel.AddLog($"💀 {aiField.UserName} 牌組耗盡！{opp.UserName} 獲勝！");
                await SaveDuelAsync(channelId, duel);
                return (BuildBoardEmbed(duel), new ComponentBuilder());
            }
            duel.CurrentPhase = DuelPhase.MainPhase1;

            // Main Phase 1: Summon best monster
            var summonable = aiField.Hand
                .Where(c => c.IsMonster && !aiField.NormalSummonedThisTurn)
                .Where(c => c.TributeRequired == 0 || aiField.GetMonstersOnField().Count >= c.TributeRequired)
                .OrderByDescending(c => c.EffectiveAtk)
                .FirstOrDefault();

            if (summonable != null && aiField.FirstEmptyMonsterZone() >= 0 && !aiField.NormalSummonedThisTurn)
            {
                int hi = aiField.Hand.IndexOf(summonable);
                // Handle tribute
                if (summonable.TributeRequired > 0)
                {
                    int needed = summonable.TributeRequired;
                    var sac = aiField.MonsterZones
                        .Select((c, i) => (c, i))
                        .Where(x => x.c != null)
                        .OrderBy(x => x.c!.EffectiveAtk)
                        .Take(needed)
                        .ToList();
                    foreach (var (c, i) in sac)
                    {
                        aiField.Graveyard.Add(c!);
                        aiField.MonsterZones[i] = null;
                        duel.AddLog($"🔥 {aiField.UserName} 獻祭 {c!.ShortName}");
                    }
                }
                PlaceMonster(aiField, summonable, hi);
                aiField.NormalSummonedThisTurn = true;
                duel.LastPlayedCardImageUrl = summonable.RareImageUrl;
                duel.AddLog($"⬆️ {aiField.UserName} 召喚 **{summonable.Name}** (ATK {summonable.Atk})");
            }

            // Activate spells from hand
            foreach (var spell in aiField.Hand.Where(c => c.IsSpell).ToList())
            {
                if (spell.Name == "Pot of Greed" || spell.Name == "Monster Reborn" || spell.Name == "Dark Hole")
                {
                    aiField.Hand.Remove(spell);
                    var msg = ApplySpellEffect(spell, duel);
                    aiField.Graveyard.Add(spell);
                    duel.LastPlayedCardImageUrl = spell.RareImageUrl;
                    duel.AddLog($"✨ {aiField.UserName} 發動 **{spell.Name}**");
                    if (!string.IsNullOrWhiteSpace(msg)) duel.AddLog($"　→ {msg}");
                }
            }

            // Battle Phase
            duel.CurrentPhase = DuelPhase.BattlePhase;
            if (duel.TurnNumber > 1) // skip battle first turn
            {
                var attackers = aiField.MonsterZones
                    .Select((c, i) => (c, i))
                    .Where(x => x.c != null && !x.c.SummonedThisTurn &&
                                !x.c.AttackedThisTurn && !x.c.IsDefensePosition)
                    .OrderByDescending(x => x.c!.EffectiveAtk)
                    .ToList();

                foreach (var (atk, aZone) in attackers)
                {
                    var targets = opp.MonsterZones
                        .Select((c, i) => (c, i))
                        .Where(x => x.c != null)
                        .ToList();

                    if (targets.Count == 0)
                    {
                        // Direct attack
                        opp.LifePoints -= atk!.EffectiveAtk;
                        atk.AttackedThisTurn = true;
                        duel.AddLog($"⚔️ {atk.ShortName} 直接攻擊！{opp.UserName} -{atk.EffectiveAtk} LP → **{opp.LifePoints}**");
                    }
                    else
                    {
                        // Attack weakest monster
                        var (def, tZone) = targets.OrderBy(x => x.c!.EffectiveAtk).First();
                        if (def!.FaceDown)
                        {
                            def.FaceDown = false;
                            duel.AddLog($"🔄 {def.ShortName} 翻面！");
                        }
                        ResolveBattle(atk!, def, aiField, opp, aZone, tZone, duel);
                    }

                    var (ended, winner) = CheckGameOver(duel);
                    if (ended)
                    {
                        duel.IsActive = false;
                        duel.WinnerName = winner;
                        duel.CurrentPhase = DuelPhase.GameOver;
                        duel.AddLog($"🏆 **{winner} 獲勝！**");
                        await SaveDuelAsync(channelId, duel);
                        return (BuildBoardEmbed(duel), new ComponentBuilder());
                    }
                }
            }

            // Set traps/spells
            foreach (var trap in aiField.Hand.Where(c => c.IsTrap || c.IsSpell).ToList())
            {
                if (aiField.FirstEmptySTZone() < 0) break;
                var placed = trap.Clone();
                placed.FaceDown = true;
                aiField.Hand.Remove(trap);
                int slot = aiField.FirstEmptySTZone();
                while (aiField.SpellTrapZones.Count <= slot) aiField.SpellTrapZones.Add(null);
                aiField.SpellTrapZones[slot] = placed;
                duel.AddLog($"🔽 {aiField.UserName} 覆蓋了一張牌");
            }

            // AI narration
            try
            {
                var personality = deckDef?.AiPersonality ?? "你是決鬥 AI";
                var prompt = $"{personality}\n你剛完成了自己的回合，說一句決鬥台詞（20字以內，充滿個性）：";
                var speech = await _ai.GenerateSimpleTextAsync(prompt, null, false);
                if (!string.IsNullOrWhiteSpace(speech))
                    duel.AddLog($"💬 {aiField.UserName}：「{speech.Trim()}」");
            }
            catch { /* 台詞生成失敗不影響遊戲 */ }

            // End turn → switch to player
            await DoEndPhase(duel);
            SwitchTurn(duel);
            duel.CurrentPhase = DuelPhase.DrawPhase;
            duel.AddLog($"▶ {duel.CurrentField.UserName} 的第 {duel.TurnNumber} 回合！（請抽牌）");

            await SaveDuelAsync(channelId, duel);
            return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
        }

        // =================================================================
        // CARD FETCHING
        // =================================================================

        public async Task<YgoCardData?> FetchCardAsync(string name)
        {
            var cacheKey = name.ToLowerInvariant().Trim();
            if (_cardCache.TryGetValue(cacheKey, out var cached)) return cached;

            // Redis cache
            if (_useRedis)
            {
                try
                {
                    var redisKey = CARD_KEY + Uri.EscapeDataString(cacheKey);
                    var raw = await _redisDb!.StringGetAsync(redisKey);
                    if (raw.HasValue)
                    {
                        var c = JsonSerializer.Deserialize<YgoCardData>(raw!);
                        if (c != null) { _cardCache[cacheKey] = c; return c; }
                    }
                }
                catch { }
            }

            try
            {
                var url = $"{API_BASE}?name={Uri.EscapeDataString(name)}";
                var json = await _http.GetStringAsync(url);
                var response = JsonSerializer.Deserialize<YgoApiResponse>(json);
                var card = response?.Data?.FirstOrDefault();
                if (card == null) return null;

                _cardCache[cacheKey] = card;
                if (_useRedis)
                {
                    try
                    {
                        var redisKey = CARD_KEY + Uri.EscapeDataString(cacheKey);
                        await _redisDb!.StringSetAsync(redisKey,
                            JsonSerializer.Serialize(card), TimeSpan.FromHours(24));
                    }
                    catch { }
                }
                return card;
            }
            catch
            {
                return null;
            }
        }

        public async Task<(Embed embed, ComponentBuilder component)> ShowCardInfoAsync(string name)
        {
            try
            {
                var card = await FetchCardAsync(name);
                if (card == null) return Error($"找不到卡片：{name}");

                var eb = new EmbedBuilder()
                    .WithTitle($"{card.Name}")
                    .WithColor(card.Type.Contains("Monster") ? new Color(0xFFD700) :
                               card.Type.Contains("Spell")   ? new Color(0x1DB954) :
                                                                new Color(0xE74C3C))
                    .WithDescription(card.Desc ?? "")
                    .AddField("類型", card.Type, true)
                    .AddField("種族", card.Race ?? "－", true)
                    .AddField("屬性", card.Attribute ?? "－", true);

                if (card.Atk.HasValue)
                    eb.AddField("ATK / DEF", $"{card.Atk} / {card.Def}", true);
                if (card.Level.HasValue)
                    eb.AddField("等級", $"★{card.Level}", true);
                if (card.CardImages?.Any() == true)
                    eb.WithThumbnailUrl(card.CardImages[0].ImageUrlSmall);

                return (eb.Build(), new ComponentBuilder());
            }
            catch (Exception ex)
            {
                return Error($"查詢失敗：{ex.Message}");
            }
        }

        // =================================================================
        // HELPERS
        // =================================================================

        private static void ResolveBattle(
            YgoCard atk, YgoCard def,
            YgoPlayerField atkField, YgoPlayerField defField,
            int aZone, int dZone, YgoDuelState duel)
        {
            atk.AttackedThisTurn = true;

            if (!def.IsDefensePosition)
            {
                // ATK vs ATK
                int diff = atk.EffectiveAtk - def.EffectiveAtk;
                if (diff > 0)
                {
                    defField.MonsterZones[dZone] = null;
                    defField.Graveyard.Add(def);
                    defField.LifePoints -= diff;
                    duel.AddLog($"⚔️ {atk.ShortName}({atk.EffectiveAtk}) vs {def.ShortName}({def.EffectiveAtk}) → {def.ShortName} 毀滅！{defField.UserName} -{diff} LP");
                }
                else if (diff == 0)
                {
                    atkField.MonsterZones[aZone] = null;
                    defField.MonsterZones[dZone] = null;
                    atkField.Graveyard.Add(atk);
                    defField.Graveyard.Add(def);
                    duel.AddLog($"⚔️ {atk.ShortName} vs {def.ShortName} → **相互毀滅！**");
                }
                else
                {
                    atkField.MonsterZones[aZone] = null;
                    atkField.Graveyard.Add(atk);
                    atkField.LifePoints += diff; // diff is negative
                    duel.AddLog($"⚔️ {atk.ShortName}({atk.EffectiveAtk}) vs {def.ShortName}({def.EffectiveAtk}) → {atk.ShortName} 毀滅！{atkField.UserName} {diff} LP");
                }
            }
            else
            {
                // ATK vs DEF
                int diff = atk.EffectiveAtk - def.Def;
                if (diff > 0)
                {
                    defField.MonsterZones[dZone] = null;
                    defField.Graveyard.Add(def);
                    duel.AddLog($"⚔️ {atk.ShortName}({atk.EffectiveAtk}) 突破 {def.ShortName}(DEF {def.Def}) → 守備方毀滅！");
                }
                else if (diff == 0)
                {
                    defField.MonsterZones[dZone] = null;
                    defField.Graveyard.Add(def);
                    duel.AddLog($"⚔️ 剛好突破守備！{def.ShortName} 毀滅");
                }
                else
                {
                    atkField.LifePoints += diff; // diff is negative
                    duel.AddLog($"⚔️ {atk.ShortName}({atk.EffectiveAtk}) 攻擊守備({def.Def}) → {atkField.UserName} {diff} LP");
                }
            }
        }

        private static (bool ended, string? winner) CheckGameOver(YgoDuelState duel)
        {
            if (duel.Field1.LifePoints <= 0)
                return (true, duel.Field2.UserName);
            if (duel.Field2.LifePoints <= 0)
                return (true, duel.Field1.UserName);
            return (false, null);
        }

        private string ApplySpellEffect(YgoCard spell, YgoDuelState duel)
        {
            var caster   = duel.CurrentField;
            var opponent = duel.OpponentField;

            if (spell.Name == "Pot of Greed")
            {
                DrawCards(caster, 2);
                return $"抽了 2 張牌（手牌 {caster.HandCount} 張）";
            }
            if (spell.Name == "Monster Reborn")
            {
                var allGY = caster.Graveyard.Concat(opponent.Graveyard)
                            .Where(c => c.IsMonster)
                            .OrderByDescending(c => c.EffectiveAtk)
                            .FirstOrDefault();
                if (allGY == null) return "墓地沒有怪獸可以復活！";
                if (caster.FirstEmptyMonsterZone() < 0) return "怪獸區已滿，無法特殊召喚！";
                caster.Graveyard.Remove(allGY);
                opponent.Graveyard.Remove(allGY);
                var revived = allGY.Clone();
                revived.SummonedThisTurn = true;
                int reSlot = caster.FirstEmptyMonsterZone();
                while (caster.MonsterZones.Count <= reSlot) caster.MonsterZones.Add(null);
                caster.MonsterZones[reSlot] = revived;
                return $"特殊召喚 **{revived.Name}** (ATK {revived.Atk})";
            }
            if (spell.Name == "Dark Hole")
            {
                int count = 0;
                for (int i = 0; i < caster.MonsterZones.Count; i++)
                    if (caster.MonsterZones[i] != null) { caster.Graveyard.Add(caster.MonsterZones[i]!); caster.MonsterZones[i] = null; count++; }
                for (int i = 0; i < opponent.MonsterZones.Count; i++)
                    if (opponent.MonsterZones[i] != null) { opponent.Graveyard.Add(opponent.MonsterZones[i]!); opponent.MonsterZones[i] = null; count++; }
                return $"毀滅了場上 {count} 隻怪獸！";
            }
            if (spell.Name == "Raigeki")
            {
                int count = 0;
                for (int i = 0; i < opponent.MonsterZones.Count; i++)
                    if (opponent.MonsterZones[i] != null) { opponent.Graveyard.Add(opponent.MonsterZones[i]!); opponent.MonsterZones[i] = null; count++; }
                return $"毀滅了對方 {count} 隻怪獸！";
            }
            if (spell.Name == "Swords of Revealing Light")
            {
                opponent.SwordsCounter = 3;
                return "對方怪獸 3 回合內無法攻擊（光之護封劍）";
            }
            if (spell.Name == "Spiral Flame Strike")
            {
                opponent.LifePoints -= 1500;
                return $"{opponent.UserName} -1500 LP → **{opponent.LifePoints}**";
            }
            if (spell.Name == "Smile World")
            {
                int cnt = caster.GetMonstersOnField().Count + opponent.GetMonstersOnField().Count;
                int boost = cnt * 100;
                foreach (var m in caster.GetMonstersOnField()) m.TempAtk = m.EffectiveAtk + boost;
                return $"場上怪獸各 +{boost} ATK（直到回合結束）";
            }
            return $"效果：{spell.Desc?.Split('.').FirstOrDefault() ?? "（需手動協議執行）"}";
        }

        private static void PlaceMonster(YgoPlayerField field, YgoCard card, int handIndex)
        {
            var placed = card.Clone();
            placed.SummonedThisTurn = true;
            field.Hand.RemoveAt(handIndex);
            int slot = field.FirstEmptyMonsterZone();
            while (field.MonsterZones.Count <= slot) field.MonsterZones.Add(null);
            field.MonsterZones[slot] = placed;
        }

        private static void DrawCards(YgoPlayerField field, int count)
        {
            for (int i = 0; i < count && field.Deck.Count > 0; i++)
            {
                field.Hand.Add(field.Deck[0]);
                field.Deck.RemoveAt(0);
            }
        }

        private static Task DoEndPhase(YgoDuelState duel)
        {
            var field = duel.CurrentField;
            // Discard to 6
            while (field.Hand.Count > 6)
            {
                var disc = field.Hand[^1];
                field.Hand.RemoveAt(field.Hand.Count - 1);
                field.Graveyard.Add(disc);
                duel.AddLog($"🗑️ {field.UserName} 棄掉 {disc.ShortName}（手牌上限）");
            }
            // Clear TempAtk
            foreach (var m in field.GetMonstersOnField()) m.TempAtk = null;
            // Swords counter
            if (field.SwordsCounter > 0) field.SwordsCounter--;
            if (duel.OpponentField.SwordsCounter > 0 && duel.CurrentField != duel.Field1)
                duel.Field1.SwordsCounter = Math.Max(0, duel.Field1.SwordsCounter - 1);
            return Task.CompletedTask;
        }

        private static void SwitchTurn(YgoDuelState duel)
        {
            var next = duel.CurrentTurnPlayerId == duel.Field1.UserId
                ? duel.Field2 : duel.Field1;
            duel.CurrentTurnPlayerId = next.UserId;
            duel.TurnNumber++;
            next.NormalSummonedThisTurn = false;
            next.DrewThisTurn = false;
            // Clear summoning sickness & attack flags
            foreach (var m in next.GetMonstersOnField())
            {
                m.SummonedThisTurn = false;
                m.AttackedThisTurn = false;
            }
            duel.CurrentPhase = DuelPhase.DrawPhase;
        }

        private static DuelPhase NextPhase(YgoDuelState duel) => duel.CurrentPhase switch
        {
            DuelPhase.DrawPhase    => DuelPhase.StandbyPhase,
            DuelPhase.StandbyPhase => DuelPhase.MainPhase1,
            DuelPhase.MainPhase1   => DuelPhase.BattlePhase,
            DuelPhase.BattlePhase  => DuelPhase.MainPhase2,
            DuelPhase.MainPhase2   => DuelPhase.EndPhase,
            DuelPhase.EndPhase     => DuelPhase.DrawPhase,
            _ => duel.CurrentPhase
        };

        private static int ParseCardTarget(string input, List<YgoCard> cards)
        {
            if (string.IsNullOrWhiteSpace(input) || cards.Count == 0) return -1;
            var lower = input.ToLower().Trim();
            // Chinese ordinals
            var cnMap = new Dictionary<string, int>
            {
                {"第1張",0},{"第2張",1},{"第3張",2},{"第4張",3},{"第5張",4},{"第6張",5},
                {"1號",0},{"2號",1},{"3號",2},{"4號",3},{"5號",4},{"6號",5},
            };
            foreach (var kv in cnMap)
                if (input.Contains(kv.Key)) return kv.Value < cards.Count ? kv.Value : -1;
            // Arabic number
            if (int.TryParse(lower, out int n) && n >= 1 && n <= cards.Count)
                return n - 1;
            // Card name fuzzy
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].Name.ToLower().Contains(lower)) return i;
            return -1;
        }

        private static int ParseZoneTarget(string input, List<YgoCard?> zones)
        {
            if (string.IsNullOrWhiteSpace(input)) return -1;
            var lower = input.ToLower().Trim();
            // Number
            if (int.TryParse(lower, out int n) && n >= 1 && n <= 5)
                return n - 1;
            // Name
            for (int i = 0; i < zones.Count; i++)
                if (zones[i] != null && zones[i]!.Name.ToLower().Contains(lower)) return i;
            return -1;
        }

        // =================================================================
        // EMBED BUILDERS
        // =================================================================

        private Embed BuildBoardEmbed(YgoDuelState duel)
        {
            var turnField = duel.CurrentField;
            var oppField  = duel.OpponentField;
            var phase     = duel.IsActive ? PhaseLabel(duel.CurrentPhase) : "決鬥結束";
            var isGameOver = duel.CurrentPhase == DuelPhase.GameOver;

            var deckDef = _decks.TryGetValue(turnField.DeckName, out var d1) ? d1 : null;
            uint color  = deckDef?.Color ?? 0xFFD700;

            var eb = new EmbedBuilder()
                .WithTitle(isGameOver
                    ? $"🏆 決鬥結束！{duel.WinnerName} 獲勝！"
                    : $"⚔️ YU-GI-OH! DUEL  •  Turn {duel.TurnNumber}  •  {phase}")
                .WithColor(new Color(color));

            // Opponent field (top)
            eb.AddField(
                $"{(oppField.IsAi ? "🤖" : "👤")} {oppField.UserName}  {LpBar(oppField.LifePoints)}",
                BuildFieldString(oppField, true),
                inline: false);

            // Battle log
            if (duel.BattleLog.Any())
            {
                var log = string.Join("\n", duel.BattleLog.TakeLast(5).Select(l => $"> {l}"));
                eb.AddField("📋 決鬥記錄", log, inline: false);
            }

            // Player field (bottom)
            eb.AddField(
                $"{(turnField.IsAi ? "🤖" : "👤")} {turnField.UserName}  {LpBar(turnField.LifePoints)}",
                BuildFieldString(turnField, false),
                inline: false);

            if (!isGameOver)
            {
                var turnName = duel.CurrentField.IsAi ? "AI 回合中..." : $"⏳ {duel.CurrentField.UserName} 的回合";
                eb.WithFooter(turnName);
            }

            // 顯示最後打出的卡圖
            if (!string.IsNullOrWhiteSpace(duel.LastPlayedCardImageUrl))
                eb.WithImageUrl(duel.LastPlayedCardImageUrl);

            return eb.Build();
        }

        private string BuildFieldString(YgoPlayerField field, bool hideHand)
        {
            var sb = new StringBuilder();
            // Monster zones
            sb.Append("**怪獸區：**  ");
            for (int i = 0; i < 5; i++)
            {
                var c = i < field.MonsterZones.Count ? field.MonsterZones[i] : null;
                if (c == null)
                    sb.Append($"`[{i+1}]空` ");
                else if (c.FaceDown && hideHand)
                    sb.Append($"`[{i+1}]❓` ");
                else if (c.IsDefensePosition)
                    sb.Append($"`[{i+1}]{ShortName(c.Name)} DEF{c.Def}` ");
                else
                    sb.Append($"`[{i+1}]{ShortName(c.Name)} ATK{c.EffectiveAtk}` ");
            }
            sb.AppendLine();
            // ST zones
            sb.Append("**魔陷區：**  ");
            for (int i = 0; i < 5; i++)
            {
                var c = i < field.SpellTrapZones.Count ? field.SpellTrapZones[i] : null;
                if (c == null)
                    sb.Append($"`[{i+1}]空` ");
                else if (c.FaceDown && hideHand)
                    sb.Append($"`[{i+1}]❓` ");
                else
                    sb.Append($"`[{i+1}]{(c.IsSpell ? "魔" : "陷")}` ");
            }
            sb.AppendLine();
            sb.Append($"🃏 Deck:{field.DeckCount}  ✋ Hand:{field.HandCount}  🪦 GY:{field.Graveyard.Count}");
            return sb.ToString();
        }

        private static Embed BuildHandEmbed(YgoPlayerField field)
        {
            var eb = new EmbedBuilder()
                .WithTitle($"🤚 {field.UserName} 的手牌（{field.Hand.Count} 張）")
                .WithColor(Color.DarkGrey);
            for (int i = 0; i < field.Hand.Count; i++)
            {
                var c = field.Hand[i];
                string stats = c.IsMonster ? $"ATK {c.Atk} / DEF {c.Def}  Lv{c.Level}" : c.Type;
                eb.AddField($"{i+1}. {c.Name}", stats, inline: true);
            }
            return eb.Build();
        }

        private Embed BuildAttackTargetEmbed(YgoDuelState duel, YgoCard attacker, bool hasTargets)
        {
            var eb = new EmbedBuilder()
                .WithTitle($"⚔️ {attacker.Name}（ATK {attacker.EffectiveAtk}）宣告攻擊")
                .WithColor(Color.Red)
                .WithDescription(hasTargets ? "選擇攻擊目標（或選擇直接攻擊）" : "對方沒有怪獸！選擇直接攻擊");

            var opp = duel.OpponentField;
            for (int i = 0; i < 5; i++)
            {
                var c = opp.MonsterZones[i];
                if (c != null)
                {
                    string info = c.FaceDown ? "❓ 覆蓋" :
                                  c.IsDefensePosition ? $"守備 DEF {c.Def}" :
                                  $"攻擊 ATK {c.EffectiveAtk}";
                    eb.AddField($"格子 {i+1}：{c.Name}", info, inline: true);
                }
            }
            return eb.Build();
        }

        private Embed BuildTributeSelectEmbed(YgoDuelState duel, YgoCard summonCard, int needed)
        {
            var eb = new EmbedBuilder()
                .WithTitle($"選擇 {needed} 隻怪獸作為貢獻，召喚 {summonCard.Name}")
                .WithColor(Color.Orange);

            var field = duel.CurrentField;
            for (int i = 0; i < 5; i++)
            {
                var c = field.MonsterZones[i];
                if (c != null)
                    eb.AddField($"格子 {i+1}：{c.Name}", $"ATK {c.EffectiveAtk}", inline: true);
            }
            return eb.Build();
        }

        /// <summary>手牌列表下面附上每張牌的「看圖」按鈕</summary>
        private static ComponentBuilder BuildHandImageButtons(string duelId, YgoPlayerField field)
        {
            var cb  = new ComponentBuilder();
            var row = new ActionRowBuilder();
            int cnt = 0;
            for (int i = 0; i < field.Hand.Count; i++)
            {
                if (cnt == 5) { cb.AddRow(row); row = new ActionRowBuilder(); cnt = 0; }
                row.WithButton($"🖼️{i+1}", $"ygo_cardimg_{duelId}_{i}", ButtonStyle.Secondary);
                cnt++;
            }
            if (cnt > 0) cb.AddRow(row);
            return cb;
        }

        private ComponentBuilder BuildBoardButtons(YgoDuelState duel)
        {
            var cb  = new ComponentBuilder();
            var did = duel.DuelId;
            bool isActive = duel.IsActive;
            bool isMyTurn = duel.CurrentField.UserId == duel.Field1.UserId && !duel.CurrentField.IsAi;

            if (!isActive) return cb;

            // Row 1: Phase controls
            var row1 = new ActionRowBuilder();
            bool canDraw = duel.CurrentPhase == DuelPhase.DrawPhase && isMyTurn;
            row1.WithButton("🎴 抽牌", $"ygo_draw_{did}", ButtonStyle.Primary, disabled: !canDraw);
            row1.WithButton("▶ 下階段", $"ygo_phase_{did}", ButtonStyle.Secondary);
            row1.WithButton("⏩ 結束回合", $"ygo_endturn_{did}", ButtonStyle.Success);
            row1.WithButton("🤚 手牌", $"ygo_hand_{did}", ButtonStyle.Secondary);

            // Row 2: Action buttons based on phase
            var row2 = new ActionRowBuilder();
            bool isMainPhase = duel.CurrentPhase == DuelPhase.MainPhase1 || duel.CurrentPhase == DuelPhase.MainPhase2;
            bool isBattlePhase = duel.CurrentPhase == DuelPhase.BattlePhase;

            if (isMainPhase)
            {
                row2.WithButton("⬆️ 召喚", $"ygo_summonmenu_{did}", ButtonStyle.Primary);
                row2.WithButton("🔽 覆蓋", $"ygo_setmenu_{did}", ButtonStyle.Secondary);
                row2.WithButton("✨ 發動", $"ygo_activatemenu_{did}", ButtonStyle.Success);
                row2.WithButton("🔄 刷新", $"ygo_board_{did}", ButtonStyle.Secondary);
            }
            else if (isBattlePhase)
            {
                row2.WithButton("⚔️ 宣告攻擊", $"ygo_atkselmenu_{did}", ButtonStyle.Danger);
                row2.WithButton("🔄 刷新", $"ygo_board_{did}", ButtonStyle.Secondary);
            }
            else
            {
                row2.WithButton("🔄 刷新場地", $"ygo_board_{did}", ButtonStyle.Secondary);
            }
            row2.WithButton("🏳️ 投降", $"ygo_surrender_{did}", ButtonStyle.Danger);

            return cb.AddRow(row1).AddRow(row2);
        }

        private static ComponentBuilder BuildAttackTargetButtons(YgoDuelState duel, bool hasMonsters)
        {
            var cb  = new ComponentBuilder();
            var did = duel.DuelId;
            var opp = duel.OpponentField;
            var row = new ActionRowBuilder();

            if (!hasMonsters)
            {
                row.WithButton("⚡ 直接攻擊！", $"ygo_atktarget_{did}_direct", ButtonStyle.Danger);
            }
            else
            {
                for (int i = 0; i < 5; i++)
                {
                    if (opp.MonsterZones.Count > i && opp.MonsterZones[i] != null)
                        row.WithButton($"攻擊格 {i+1}", $"ygo_atktarget_{did}_{i}", ButtonStyle.Danger);
                }
                row.WithButton("直接攻擊", $"ygo_atktarget_{did}_direct", ButtonStyle.Secondary, disabled: hasMonsters);
            }

            return cb.AddRow(row);
        }

        private static ComponentBuilder BuildTributeButtons(YgoDuelState duel)
        {
            var cb  = new ComponentBuilder();
            var did = duel.DuelId;
            var row = new ActionRowBuilder();
            var field = duel.CurrentField;

            for (int i = 0; i < 5; i++)
            {
                if (field.MonsterZones.Count > i && field.MonsterZones[i] != null &&
                    !duel.PendingTributeZones.Contains(i))
                    row.WithButton($"獻祭 [{i+1}]", $"ygo_tribute_{did}_{i}", ButtonStyle.Danger);
            }

            return cb.AddRow(row);
        }

        // =================================================================
        // DECK BUILDING
        // =================================================================

        private async Task<List<YgoCard>> BuildDeckAsync(string deckKey)
        {
            if (!_decks.TryGetValue(deckKey, out var def)) return new();
            var deck = new List<YgoCard>();
            foreach (var name in def.MainDeckNames)
            {
                var data = await FetchCardAsync(name);
                if (data != null) deck.Add(DataToCard(data));
                else
                {
                    // Placeholder if API fails
                    deck.Add(new YgoCard { Name = name, Type = "Monster", Atk = 1000, Def = 1000, Level = 4 });
                }
                await Task.Delay(50); // Rate limit
            }
            return deck;
        }

        private async Task<List<YgoCard>> BuildExtraDeckAsync(string deckKey)
        {
            if (!_decks.TryGetValue(deckKey, out var def) || def.ExtraDeckNames.Count == 0) return new();
            var extra = new List<YgoCard>();
            foreach (var name in def.ExtraDeckNames)
            {
                var data = await FetchCardAsync(name);
                if (data != null) extra.Add(DataToCard(data));
                await Task.Delay(50);
            }
            return extra;
        }

        private static YgoCard DataToCard(YgoCardData d)
        {
            string type = d.Type.Contains("Monster") ? "Monster" :
                          d.Type.Contains("Spell")   ? "Spell"   : "Trap";
            return new YgoCard
            {
                ApiId     = d.Id,
                Name      = d.Name,
                Type      = type,
                FrameType = d.FrameType ?? "",
                Desc      = d.Desc ?? "",
                Atk       = d.Atk ?? 0,
                Def       = d.Def ?? 0,
                Level     = d.Level ?? 0,
                Attribute = d.Attribute ?? "",
                Race      = d.Race ?? "",
                ImageUrl     = d.CardImages?.FirstOrDefault()?.ImageUrlSmall ?? "",
                RareImageUrl = d.CardImages?.LastOrDefault()?.ImageUrl
                               ?? d.CardImages?.FirstOrDefault()?.ImageUrl ?? "",
            };
        }

        private void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // =================================================================
        // REDIS STORAGE
        // =================================================================

        private async Task SaveDuelAsync(ulong channelId, YgoDuelState duel)
        {
            duel.LastActionTime = DateTime.UtcNow;
            _memDuels[channelId] = duel;
            if (_useRedis)
            {
                try
                {
                    var json = JsonSerializer.Serialize(duel);
                    await _redisDb!.StringSetAsync(DUEL_KEY + channelId, json, TimeSpan.FromHours(12));
                    await _redisDb!.StringSetAsync(CHAN_KEY + channelId, duel.DuelId, TimeSpan.FromHours(12));
                }
                catch { }
            }
        }

        private async Task<YgoDuelState?> LoadDuelAsync(ulong channelId)
        {
            if (_memDuels.TryGetValue(channelId, out var m)) return m;
            if (_useRedis)
            {
                try
                {
                    var raw = await _redisDb!.StringGetAsync(DUEL_KEY + channelId);
                    if (raw.HasValue)
                    {
                        var state = JsonSerializer.Deserialize<YgoDuelState>(raw!);
                        if (state != null) { _memDuels[channelId] = state; return state; }
                    }
                }
                catch { }
            }
            return null;
        }

        public bool IsChannelInDuel(ulong channelId) =>
            _memDuels.TryGetValue(channelId, out var d) && d.IsActive;

        // =================================================================
        // UTILS
        // =================================================================

        private static string LpBar(int lp)
        {
            int filled = (int)Math.Round(lp / 800.0);
            filled = Math.Max(0, Math.Min(10, filled));
            return new string('█', filled) + new string('░', 10 - filled) + $" **{lp}** LP";
        }

        private static string PhaseLabel(DuelPhase p) => p switch
        {
            DuelPhase.DrawPhase    => "抽牌階段",
            DuelPhase.StandbyPhase => "準備階段",
            DuelPhase.MainPhase1   => "主要階段1",
            DuelPhase.BattlePhase  => "戰鬥階段",
            DuelPhase.MainPhase2   => "主要階段2",
            DuelPhase.EndPhase     => "結束階段",
            DuelPhase.GameOver     => "決鬥結束",
            _                      => "－"
        };

        private static string ShortName(string name)
        {
            if (name.Length <= 8) return name;
            var words = name.Split(' ');
            return words.Length >= 2 ? $"{words[0][0]}{words[1][0]}" : name[..5];
        }

        private static (Embed embed, ComponentBuilder component) Error(string msg)
            => (CommonHelper.BuildErrorResponse(msg).Item2, new ComponentBuilder());

        // =================================================================
        // ANIME DECK DEFINITIONS
        // =================================================================

        private static Dictionary<string, AnimeDeckDefinition> BuildDecks()
        {
            var list = new List<AnimeDeckDefinition>
            {
                new()
                {
                    Key = "yugi", CharacterName = "武藤遊戲", Series = "DM",
                    Emoji = "🔮", Color = 0x7B2FBE,
                    AiPersonality = "你是武藤遊戲，說話謙遜但在決鬥中充滿熱情與信念，相信心之牌。",
                    MainDeckNames = new()
                    {
                        "Dark Magician","Dark Magician","Dark Magician Girl","Kuriboh","Kuriboh",
                        "Summoned Skull","Celtic Guardian","Big Shield Gardna","Buster Blader",
                        "Skilled Dark Magician","Dark Blade","Magician of Faith",
                        "Dark Magic Attack","Polymerization","Monster Reborn","Pot of Greed",
                        "Swords of Revealing Light","Mirror Force","Magic Cylinder","Spellbinding Circle",
                    },
                    ExtraDeckNames = new() { "Dark Paladin" }
                },
                new()
                {
                    Key = "kaiba", CharacterName = "海馬瀨人", Series = "DM",
                    Emoji = "🐉", Color = 0x1565C0,
                    AiPersonality = "你是海馬瀨人，傲慢冷酷，把對手叫做 loser，但決鬥技術高超。",
                    MainDeckNames = new()
                    {
                        "Blue-Eyes White Dragon","Blue-Eyes White Dragon","Blue-Eyes White Dragon",
                        "Lord of D.","Vorse Raider","Battle Ox","Luster Dragon","Luster Dragon",
                        "Kaiser Sea Horse","X-Head Cannon","Y-Dragon Head","Z-Metal Tank",
                        "The Flute of Summoning Dragon","Cost Down","Enemy Controller",
                        "Shrink","Monster Reborn","Ring of Destruction","Crush Card Virus","Negate Attack",
                    },
                    ExtraDeckNames = new() { "Blue-Eyes Ultimate Dragon" }
                },
                new()
                {
                    Key = "joey", CharacterName = "城之內克也", Series = "DM",
                    Emoji = "🃏", Color = 0xC62828,
                    AiPersonality = "你是城之內克也，熱血直率、有時莽撞，用街頭智慧決鬥。",
                    MainDeckNames = new()
                    {
                        "Red-Eyes B. Dragon","Jinzo","Panther Warrior","Gearfried the Iron Knight",
                        "Alligator's Sword","Rocket Warrior","Garoozis","Little-Winguard",
                        "Swordsman of Landstar","Time Wizard","Baby Dragon","Flame Swordsman",
                        "Graceful Dice","Dragon Nails","Scapegoat","Giant Trunade","Monster Reborn",
                        "Reinforcements","Skull Dice","Graverobber",
                    },
                    ExtraDeckNames = new() { "Thousand Dragon" }
                },
                new()
                {
                    Key = "jaden", CharacterName = "遊城十代", Series = "GX",
                    Emoji = "⚡", Color = 0xE65100,
                    AiPersonality = "你是遊城十代，活潑不按牌理出牌，認為決鬥是最大的樂趣。",
                    MainDeckNames = new()
                    {
                        "Elemental HERO Neos","Elemental HERO Sparkman","Elemental HERO Burstinatrix",
                        "Elemental HERO Avian","Elemental HERO Clayman","Elemental HERO Bubbleman",
                        "Elemental HERO Wildheart","Wroughtweiler","Neo-Spacian Grand Mole",
                        "Neo-Spacian Air Hummingbird","Elemental HERO Heat","Winged Kuriboh",
                        "Polymerization","Miracle Fusion","O - Oversoul","A Hero Lives","Bubble Shuffle",
                        "Hero Signal","Negate Attack","Monster Reborn",
                    },
                    ExtraDeckNames = new() { "Elemental HERO Flame Wingman","Elemental HERO Thunder Giant","Elemental HERO Shining Flare Wingman" }
                },
                new()
                {
                    Key = "yusei", CharacterName = "不動遊星", Series = "5D's",
                    Emoji = "✨", Color = 0x263238,
                    AiPersonality = "你是不動遊星，沉著冷靜，相信羈絆的力量，言簡意賅。",
                    MainDeckNames = new()
                    {
                        "Junk Synchron","Junk Synchron","Speed Warrior","Speed Warrior",
                        "Quillbolt Hedgehog","Nitro Synchron","Turbo Synchron","Quickdraw Synchron",
                        "Debris Dragon","Hyper Synchron","Synchron Explorer","Unknown Synchron",
                        "Synchro Blast Wave","Graceful Revival","Fighting Spirit","Scrapstorm",
                        "Monster Reborn","Scrap-Iron Scarecrow","Synchro Strike","Urgent Tuning",
                    },
                    ExtraDeckNames = new() { "Stardust Dragon","Junk Warrior","Nitro Warrior" }
                },
                new()
                {
                    Key = "yuya", CharacterName = "榊遊矢", Series = "ARC-V",
                    Emoji = "🎭", Color = 0x2E7D32,
                    AiPersonality = "你是榊遊矢，充滿表演精神，相信決鬥能帶來笑容，喜歡以特技翻盤。",
                    MainDeckNames = new()
                    {
                        "Odd-Eyes Pendulum Dragon","Odd-Eyes Pendulum Dragon","Odd-Eyes Dragon",
                        "Performapal Sword Fish","Performapal Trampolynx","Performapal Springoose",
                        "Performapal Monkeyboard","Performapal Skullcrobat Joker","Performapal Partnaga",
                        "Performapal Whip Snake","Performapal Hip Hippo","Performapal Pendulum Sorcerer",
                        "Sky Iris","Duelist Alliance","Pendulum Shift","Smile World","Spiral Flame Strike",
                        "Pendulum Reborn","Performapal Popperup","Damage = Reptile",
                    },
                    ExtraDeckNames = new() { "Odd-Eyes Rebellion Dragon","Odd-Eyes Vortex Dragon" }
                },
                new()
                {
                    Key = "mai", CharacterName = "孔雀舞", Series = "DM",
                    Emoji = "🦅", Color = 0xE91E63,
                    AiPersonality = "你是孔雀舞，以哈比天使牌組決鬥，優雅強勢，輕視弱者，說話帶刺。",
                    MainDeckNames = new()
                    {
                        "Harpie Lady","Harpie Lady","Harpie Lady","Harpie Lady Sisters",
                        "Cyber Harpie Lady","Harpie Lady 1","Harpie Lady 2","Harpie Lady 3",
                        "Harpie's Pet Dragon","Harpie's Brother",
                        "Elegant Egotist","Elegant Egotist","Hysteric Party",
                        "Harpie's Hunting Ground","Harpie's Feather Duster",
                        "Mirror Wall","Windstorm of Etaqua","Trap Hole","Monster Reborn","Dark Hole",
                    },
                    ExtraDeckNames = new()
                },
                new()
                {
                    Key = "marik", CharacterName = "馬立克", Series = "DM",
                    Emoji = "☀️", Color = 0xF57F17,
                    AiPersonality = "你是馬立克，殘酷冷血，以太陽神巨神鳥為信仰，言語充滿威脅與嘲諷。",
                    MainDeckNames = new()
                    {
                        "The Winged Dragon of Ra","Newdoria","Drillago","Revival Jam",
                        "Jowgen the Spiritualist","Lava Golem","Metal Reflect Slime","Ectoplasmer",
                        "Coffin Seller","Nightmare Wheel","Card of Safe Return",
                        "Change of Heart","Monster Reborn","Dark Hole","Snatch Steal",
                        "Brain Control","Soul Exchange","Ring of Destruction","Mystik Wok","Stop Defense",
                    },
                    ExtraDeckNames = new()
                },
                new()
                {
                    Key = "pegasus", CharacterName = "佩加瑟斯", Series = "DM",
                    Emoji = "👁️", Color = 0x880E4F,
                    AiPersonality = "你是佩加瑟斯，輕浮優雅，以千眼邪教祭師和卡通牌組稱霸，說話帶點譏諷的微笑。",
                    MainDeckNames = new()
                    {
                        "Relinquished","Thousand-Eyes Idol","Toon Mermaid","Toon Mermaid",
                        "Toon Gemini Elf","Toon Summoned Skull","Blue-Eyes Toon Dragon",
                        "Toon Dark Magician Girl","Dark-Eyes Illusionist","One-Eyed Shield Dragon",
                        "Toon World","Toon Table of Contents","Comic Hand","Black Illusion Ritual",
                        "Polymerization","Messenger of Peace","Monster Reborn","Dark Hole",
                        "Mirror Force","Magic Jammer",
                    },
                    ExtraDeckNames = new() { "Thousand-Eyes Restrict" }
                },
                new()
                {
                    Key = "bakura", CharacterName = "獏良了", Series = "DM",
                    Emoji = "💀", Color = 0x37474F,
                    AiPersonality = "你是獏良了，被闇精靈附身，冰冷陰森，沉迷於讓對手陷入絕望。",
                    MainDeckNames = new()
                    {
                        "Dark Necrofear","Dark Necrofear","Earthbound Spirit","Man-Eater Bug",
                        "Pumpking the King of Ghosts","The Earl of Demise","Puppet King",
                        "Spirit of Flames","Headless Knight","Tainted Wisdom","Nightmare Horse",
                        "Destiny Board","Change of Heart","Card Destruction","Monster Reborn","Dark Hole",
                        "Shallow Grave","Premature Burial","Magic Jammer","Ring of Destruction",
                    },
                    ExtraDeckNames = new()
                },
                new()
                {
                    Key = "jack", CharacterName = "傑克·亞特拉斯", Series = "5D's",
                    Emoji = "👑", Color = 0xB71C1C,
                    AiPersonality = "你是傑克·亞特拉斯，自稱「King」，傲慢霸道，以紅蓮魔龍為榮耀。",
                    MainDeckNames = new()
                    {
                        "Red Dragon Archfiend","Vice Dragon","Strong Wind Dragon","Twin-Sword Marauder",
                        "Dark Resonator","Dark Resonator","Infernity Archfiend","Battle Fader",
                        "Exploder Dragon","Lancer Archfiend","Assault Beast",
                        "Assault Mode Activate","Trap Eater","Power Frame","Mirror Force",
                        "Solemn Judgment","Bottomless Trap Hole","Book of Moon","Scrap-Iron Scarecrow","Shock Wave",
                    },
                    ExtraDeckNames = new() { "Red Dragon Archfiend/Assault Mode","Exploder Dragonwing" }
                },
                new()
                {
                    Key = "crow", CharacterName = "克羅·霍根", Series = "5D's",
                    Emoji = "🐦", Color = 0x212121,
                    AiPersonality = "你是克羅·霍根，義氣當先，以黑羽牌組行俠仗義，言語直率豪邁。",
                    MainDeckNames = new()
                    {
                        "Blackwing - Gale the Whirlwind","Blackwing - Shura the Blue Flame",
                        "Blackwing - Blizzard the Far North","Blackwing - Bora the Spear",
                        "Blackwing - Kalut the Moon Shadow","Blackwing - Sirocco the Dawn",
                        "Dark Armed Dragon","Allure of Darkness","Black Whirlwind","Black Whirlwind",
                        "Delta Crow - Anti Reverse","Icarus Attack","Icarus Attack",
                        "Monster Reborn","Foolish Burial","Cards for Black Feathers",
                        "Book of Moon","Torrential Tribute","Bottomless Trap Hole","Mystical Space Typhoon",
                    },
                    ExtraDeckNames = new() { "Blackwing Armed Wing","Blackwing Armor Master","Black-Winged Dragon" }
                },
                new()
                {
                    Key = "yuma", CharacterName = "九十九遊馬", Series = "ZEXAL",
                    Emoji = "⭐", Color = 0xF9A825,
                    AiPersonality = "你是九十九遊馬，充滿熱情，相信「決鬥魂」，絕不放棄，口頭禪是「這就是我的決鬥！」。",
                    MainDeckNames = new()
                    {
                        "Gagaga Magician","Gagaga Magician","Gagaga Girl","Gogogo Giant",
                        "Gogogo Ghost","Dododo Warrior","Dododo Witch","Zubaba Knight",
                        "Ganbara Knight","Acorno","Pinecono","Bicular",
                        "Xyz Energy","Heartfelt Appeal","Monster Reborn","Dark Hole",
                        "Half Unbreak","Xyz Unit","Onomatopair","Bound Wand",
                    },
                    ExtraDeckNames = new() { "Number 39: Utopia","Number C39: Utopia Ray" }
                },
            };

            return list.ToDictionary(d => d.Key);
        }

        public IReadOnlyDictionary<string, AnimeDeckDefinition> GetDeckDefinitions() => _decks;
    }
}
