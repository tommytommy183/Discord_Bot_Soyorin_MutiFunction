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
        private const string CARD_KEY   = "ygo:cards";   // Redis Hash key（所有卡合併存一個 Hash）
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

        /// <summary>顯示玩家手牌＋所選卡圖（selectedIdx 決定目前顯示哪張圖）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> GetHandEmbedAsync(
            ulong channelId, ulong userId, int selectedIdx = 0)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("此頻道沒有進行中的決鬥。");

            var field = duel.Field1.UserId == userId ? duel.Field1 : duel.Field2;
            var hand  = field.Hand;

            if (hand.Count == 0)
            {
                var emptyEb = new EmbedBuilder()
                    .WithTitle($"🤚 {field.UserName} 的手牌（0 張）")
                    .WithColor(Color.DarkGrey)
                    .WithDescription("手牌是空的！");
                return (emptyEb.Build(), new ComponentBuilder());
            }

            int sel     = Math.Clamp(selectedIdx, 0, hand.Count - 1);
            var selCard = hand[sel];

            var eb = new EmbedBuilder()
                .WithTitle($"🤚 {field.UserName} 的手牌（{hand.Count} 張）　　▶ {selCard.Name}")
                .WithColor(selCard.IsMonster ? new Color(0xFFD700) :
                           selCard.IsSpell   ? new Color(0x1DB954) : new Color(0xE74C3C));

            for (int i = 0; i < hand.Count; i++)
            {
                var c     = hand[i];
                string stats = c.IsMonster
                    ? $"ATK {c.Atk} / DEF {c.Def}  Lv{c.Level}"
                    : c.Type;
                string name = i == sel ? $"**[{i+1}] {c.Name}**" : $"{i+1}. {c.Name}";
                eb.AddField(name, stats, inline: true);
            }

            // 顯示所選牌的卡圖（embed 大圖）
            string imgUrl = selCard.RareImageUrl;
            if (string.IsNullOrWhiteSpace(imgUrl)) imgUrl = selCard.ImageUrl;
            if (!string.IsNullOrWhiteSpace(imgUrl))
                eb.WithImageUrl(imgUrl);

            // 切換按鈕（所選高亮 Primary，其他 Secondary）
            var cb  = new ComponentBuilder();
            var row = new ActionRowBuilder();
            for (int i = 0; i < hand.Count; i++)
            {
                if (i > 0 && i % 5 == 0) { cb.AddRow(row); row = new ActionRowBuilder(); }
                var style = i == sel ? ButtonStyle.Primary : ButtonStyle.Secondary;
                row.WithButton($"🖼️{i+1}", $"ygo_cardimg_{duel.DuelId}_{i}", style);
            }
            cb.AddRow(row);
            return (eb.Build(), cb);
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
                await DeleteDuelAsync(channelId);
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

        /// <summary>顯示場上伏地的魔陷牌，供選擇發動</summary>
        public async Task<(Embed embed, ComponentBuilder component)> ShowSetSTMenuAsync(
            ulong channelId, ulong userId)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");

            var field = duel.CurrentField;
            var faceDown = field.SpellTrapZones
                .Select((c, i) => (c, i))
                .Where(x => x.c != null && x.c.FaceDown)
                .ToList();

            if (!faceDown.Any()) return Error("場上沒有可發動的伏地牌！");

            var eb = new EmbedBuilder()
                .WithTitle("⚡ 選擇要發動的伏地牌")
                .WithColor(Color.Orange);
            foreach (var (c, i) in faceDown)
                eb.AddField($"ST格 {i+1}：{c!.Name}", c.IsSpell ? "魔法" : "陷阱", inline: true);

            var cb  = new ComponentBuilder();
            var row = new ActionRowBuilder();
            foreach (var (c, i) in faceDown)
                row.WithButton($"ST[{i+1}] {c!.ShortName}", $"ygo_stact_{duel.DuelId}_{i}", ButtonStyle.Primary);
            cb.AddRow(row);
            return (eb.Build(), cb);
        }

        /// <summary>發動場上指定格子的伏地魔陷</summary>
        public async Task<(Embed embed, ComponentBuilder component)> ActivateSetSTAsync(
            ulong channelId, ulong userId, int stZone)
        {
            var duel = await LoadDuelAsync(channelId);
            if (duel == null) return Error("沒有進行中的決鬥。");
            if (duel.CurrentTurnPlayerId != userId) return Error("還沒輪到你！");

            var field = duel.CurrentField;
            if (stZone < 0 || stZone >= field.SpellTrapZones.Count || field.SpellTrapZones[stZone] == null)
                return Error("無效的格子。");
            var card = field.SpellTrapZones[stZone]!;
            if (!card.FaceDown) return Error("這張牌已經是表側了。");

            field.SpellTrapZones[stZone] = null;
            var msg = ApplySpellEffect(card, duel);
            field.Graveyard.Add(card);
            duel.LastPlayedCardImageUrl = card.RareImageUrl;
            duel.AddLog($"⚡ {field.UserName} 發動伏地牌 **{card.Name}**");
            if (!string.IsNullOrWhiteSpace(msg)) duel.AddLog($"　→ {msg}");

            var (ended, winner) = CheckGameOver(duel);
            if (ended)
            {
                duel.IsActive = false;
                duel.WinnerName = winner;
                duel.CurrentPhase = DuelPhase.GameOver;
                duel.AddLog($"🏆 **{winner} 獲勝！**");
            }

            if (!duel.IsActive)
                await DeleteDuelAsync(channelId);
            else
                await SaveDuelAsync(channelId, duel);
            return (BuildBoardEmbed(duel), BuildBoardButtons(duel));
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
            if (duel.CurrentField.CannotDeclareAttackThisTurn) return Error("本回合無法宣告攻擊！（Threatening Roar）");

            var field = duel.CurrentField;
            var atkers = field.MonsterZones
                .Select((c, i) => (c, i))
                .Where(x => x.c != null && !x.c.SummonedThisTurn &&
                            !x.c.AttackedThisTurn && !x.c.IsDefensePosition && !x.c.CannotAttack)
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

            if (!duel.IsActive)
                await DeleteDuelAsync(channelId);
            else
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
                if (!defField.WabokuActive)
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

            if (!duel.IsActive)
                await DeleteDuelAsync(channelId);
            else
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

            await DeleteDuelAsync(channelId);
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
                await DeleteDuelAsync(channelId);
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
                        await DeleteDuelAsync(channelId);
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

            // Redis Hash 快取（ygo:cards → field=卡名, value=JSON）
            if (_useRedis)
            {
                try
                {
                    var raw = await _redisDb!.HashGetAsync(CARD_KEY, cacheKey);
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
                        await _redisDb!.HashSetAsync(CARD_KEY, cacheKey,
                            JsonSerializer.Serialize(card));
                        // Hash 本身設定 TTL（每次存入都重設，確保不過期）
                        await _redisDb!.KeyExpireAsync(CARD_KEY, TimeSpan.FromHours(48));
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
                    if (!defField.WabokuActive)
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
                    if (!atkField.WabokuActive)
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
                    if (!atkField.WabokuActive)
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

            // ── 通用工具 ────────────────────────────────────────────────
            YgoCard? StrongestOpponentMonster() =>
                opponent.MonsterZones.Where(c => c != null).OrderByDescending(c => c!.EffectiveAtk).FirstOrDefault();

            YgoCard? StrongestCasterGYMonster() =>
                caster.Graveyard.Where(c => c.IsMonster).OrderByDescending(c => c.EffectiveAtk).FirstOrDefault();

            // ── 基本抽牌魔法 ─────────────────────────────────────────────
            if (spell.Name == "Pot of Greed" || spell.Name == "Graceful Charity")
            {
                int draw = spell.Name == "Graceful Charity" ? 3 : 2;
                DrawCards(caster, draw);
                if (spell.Name == "Graceful Charity" && caster.Hand.Count > 0)
                {
                    int discard = Math.Min(2, caster.Hand.Count);
                    for (int i = 0; i < discard; i++)
                    {
                        caster.Graveyard.Add(caster.Hand[^1]);
                        caster.Hand.RemoveAt(caster.Hand.Count - 1);
                    }
                    return $"抽了 {draw} 張牌，棄掉 {discard} 張（手牌 {caster.HandCount} 張）";
                }
                return $"抽了 {draw} 張牌（手牌 {caster.HandCount} 張）";
            }
            if (spell.Name == "Allure of Darkness" || spell.Name == "Cards for Black Feathers")
            {
                DrawCards(caster, 2);
                return $"抽了 2 張牌（手牌 {caster.HandCount} 張）";
            }
            if (spell.Name == "Card Destruction")
            {
                int casterDraw = caster.Hand.Count;
                int oppDraw    = opponent.Hand.Count;
                caster.Graveyard.AddRange(caster.Hand);
                caster.Hand.Clear();
                opponent.Graveyard.AddRange(opponent.Hand);
                opponent.Hand.Clear();
                DrawCards(caster, casterDraw);
                DrawCards(opponent, oppDraw);
                return $"雙方棄掉手牌並重新抽牌！（{caster.UserName} 抽 {casterDraw}，{opponent.UserName} 抽 {oppDraw}）";
            }

            // ── 場地清除 ─────────────────────────────────────────────────
            if (spell.Name == "Dark Hole")
            {
                int count = 0;
                foreach (var f in new[] { caster, opponent })
                    for (int i = 0; i < f.MonsterZones.Count; i++)
                        if (f.MonsterZones[i] != null) { f.Graveyard.Add(f.MonsterZones[i]!); f.MonsterZones[i] = null; count++; }
                return $"毀滅了場上全部 {count} 隻怪獸！";
            }
            if (spell.Name == "Raigeki")
            {
                int count = 0;
                for (int i = 0; i < opponent.MonsterZones.Count; i++)
                    if (opponent.MonsterZones[i] != null) { opponent.Graveyard.Add(opponent.MonsterZones[i]!); opponent.MonsterZones[i] = null; count++; }
                return $"毀滅了對方 {count} 隻怪獸！";
            }
            if (spell.Name == "Harpie's Feather Duster")
            {
                int count = 0;
                for (int i = 0; i < opponent.SpellTrapZones.Count; i++)
                    if (opponent.SpellTrapZones[i] != null) { opponent.Graveyard.Add(opponent.SpellTrapZones[i]!); opponent.SpellTrapZones[i] = null; count++; }
                return $"掃場！摧毀了對方 {count} 張魔陷！";
            }
            if (spell.Name == "Mystical Space Typhoon")
            {
                for (int i = 0; i < opponent.SpellTrapZones.Count; i++)
                    if (opponent.SpellTrapZones[i] != null)
                    {
                        var target = opponent.SpellTrapZones[i]!;
                        opponent.Graveyard.Add(target);
                        opponent.SpellTrapZones[i] = null;
                        return $"摧毀了對方的 {target.Name}！";
                    }
                return "對方魔陷區是空的！";
            }
            if (spell.Name == "Dark Magic Attack")
            {
                bool hasDM = caster.GetMonstersOnField().Any(m =>
                    m.Name.Contains("Dark Magician"));
                if (!hasDM) return "場上沒有黑魔術師，無法發動效果！";
                int count = 0;
                for (int i = 0; i < opponent.SpellTrapZones.Count; i++)
                    if (opponent.SpellTrapZones[i] != null) { opponent.Graveyard.Add(opponent.SpellTrapZones[i]!); opponent.SpellTrapZones[i] = null; count++; }
                return $"黑魔術師的力量！摧毀對方 {count} 張魔陷！";
            }
            if (spell.Name == "Crush Card Virus")
            {
                int count = 0;
                for (int i = 0; i < opponent.MonsterZones.Count; i++)
                    if (opponent.MonsterZones[i] != null && opponent.MonsterZones[i]!.Atk >= 1500)
                    { opponent.Graveyard.Add(opponent.MonsterZones[i]!); opponent.MonsterZones[i] = null; count++; }
                int handDest = 0;
                for (int i = opponent.Hand.Count - 1; i >= 0; i--)
                    if (opponent.Hand[i].IsMonster && opponent.Hand[i].Atk >= 1500)
                    { opponent.Graveyard.Add(opponent.Hand[i]); opponent.Hand.RemoveAt(i); handDest++; }
                return $"病毒作戰！場上摧毀 {count} 隻、手牌摧毀 {handDest} 隻（ATK 1500以上）";
            }

            // ── 怪獸控制 / 復活 ──────────────────────────────────────────
            if (spell.Name == "Monster Reborn" || spell.Name == "Premature Burial" || spell.Name == "Shallow Grave")
            {
                if (spell.Name == "Premature Burial")
                {
                    caster.LifePoints -= 800;
                    if (caster.LifePoints <= 0) caster.LifePoints = 1;
                }
                var pool = spell.Name == "Shallow Grave"
                    ? (IEnumerable<YgoCard>)caster.Graveyard.Where(c => c.IsMonster)
                    : caster.Graveyard.Concat(opponent.Graveyard).Where(c => c.IsMonster);
                var target = pool.OrderByDescending(c => c.EffectiveAtk).FirstOrDefault();
                if (target == null) return "墓地沒有怪獸！";
                if (caster.FirstEmptyMonsterZone() < 0) return "怪獸區已滿！";
                caster.Graveyard.Remove(target);
                opponent.Graveyard.Remove(target);
                var revived = target.Clone();
                revived.SummonedThisTurn = spell.Name != "Premature Burial"; // Premature Burial no SS-sickness? simplified: yes sickness
                if (spell.Name == "Shallow Grave") { revived.FaceDown = true; revived.IsDefensePosition = true; }
                int slot = caster.FirstEmptyMonsterZone();
                while (caster.MonsterZones.Count <= slot) caster.MonsterZones.Add(null);
                caster.MonsterZones[slot] = revived;
                string prefix = spell.Name == "Premature Burial" ? "（付出 800 LP）" : "";
                return $"{prefix}特殊召喚 **{revived.Name}** (ATK {revived.Atk})";
            }
            if (spell.Name == "Change of Heart" || spell.Name == "Brain Control" || spell.Name == "Snatch Steal")
            {
                if (spell.Name == "Brain Control") { caster.LifePoints -= 800; if (caster.LifePoints <= 0) caster.LifePoints = 1; }
                var target = StrongestOpponentMonster();
                if (target == null) return "對方沒有怪獸！";
                int oZone = opponent.MonsterZones.IndexOf(target);
                if (oZone < 0) return "找不到目標怪獸！";
                if (caster.FirstEmptyMonsterZone() < 0) return "我方怪獸區已滿！";
                opponent.MonsterZones[oZone] = null;
                var stolen = target.Clone();
                stolen.SummonedThisTurn = false;
                stolen.AttackedThisTurn = false;
                int slot = caster.FirstEmptyMonsterZone();
                while (caster.MonsterZones.Count <= slot) caster.MonsterZones.Add(null);
                caster.MonsterZones[slot] = stolen;
                string prefix2 = spell.Name == "Brain Control" ? "（付 800 LP）" : "";
                return $"{prefix2}取得 **{stolen.Name}**（ATK {stolen.Atk}）的控制權！";
            }
            if (spell.Name == "O - Oversoul" || spell.Name == "A Hero Lives")
            {
                YgoCard? hero = null;
                if (spell.Name == "O - Oversoul")
                    hero = caster.Graveyard.Where(c => c.IsMonster && c.Name.Contains("Elemental HERO"))
                                 .OrderByDescending(c => c.EffectiveAtk).FirstOrDefault();
                else
                {
                    caster.LifePoints -= caster.LifePoints / 2;
                    hero = caster.Deck.FirstOrDefault(c => c.IsMonster && c.Name.Contains("Elemental HERO"));
                    if (hero != null) caster.Deck.Remove(hero);
                }
                if (hero == null) return "找不到 Elemental HERO 怪獸！";
                if (caster.FirstEmptyMonsterZone() < 0) return "怪獸區已滿！";
                if (spell.Name == "O - Oversoul") { caster.Graveyard.Remove(hero); }
                var summoned = hero.Clone();
                summoned.SummonedThisTurn = true;
                int slot = caster.FirstEmptyMonsterZone();
                while (caster.MonsterZones.Count <= slot) caster.MonsterZones.Add(null);
                caster.MonsterZones[slot] = summoned;
                return $"特殊召喚 **{summoned.Name}** (ATK {summoned.Atk})！";
            }

            // ── 封印 / 強化 ───────────────────────────────────────────────
            if (spell.Name == "Swords of Revealing Light")
            {
                opponent.SwordsCounter = 3;
                return "對方怪獸 3 回合內無法攻擊（光之護封劍）";
            }
            if (spell.Name == "Messenger of Peace")
            {
                opponent.SwordsCounter = Math.Max(opponent.SwordsCounter, 2);
                return "和平使者！對方 ATK 1500 以上的怪獸 2 回合內無法攻擊";
            }
            if (spell.Name == "Enemy Controller")
            {
                foreach (var m in opponent.GetMonstersOnField())
                { m.IsDefensePosition = true; m.FaceDown = false; }
                return "Enemy Controller！對方所有怪獸轉守備表示！";
            }
            if (spell.Name == "Windstorm of Etaqua")
            {
                foreach (var m in opponent.GetMonstersOnField())
                { m.IsDefensePosition = true; m.FaceDown = false; }
                return "哈比的旋風！對方所有怪獸轉守備表示！";
            }
            if (spell.Name == "Mirror Wall")
            {
                foreach (var m in opponent.GetMonstersOnField())
                    m.TempAtk = m.EffectiveAtk / 2;
                return "魔鏡牆！對方所有攻擊表示怪獸 ATK 減半！";
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
                return $"場上怪獸各 +{boost} ATK";
            }

            // ── 傷害 ────────────────────────────────────────────────────
            if (spell.Name == "Ring of Destruction")
            {
                var target = StrongestOpponentMonster();
                if (target == null) return "對方沒有怪獸！";
                int dmg = target.EffectiveAtk;
                int tZone = opponent.MonsterZones.IndexOf(target);
                if (tZone >= 0) { opponent.MonsterZones[tZone] = null; opponent.Graveyard.Add(target); }
                caster.LifePoints  -= dmg;
                opponent.LifePoints -= dmg;
                return $"毀滅 **{target.Name}**（ATK {dmg}）！雙方各 -{dmg} LP";
            }
            if (spell.Name == "Coffin Seller")
            {
                opponent.LifePoints -= 300 * Math.Max(1, opponent.Graveyard.Count(c => c.IsMonster));
                return $"棺材販子！{opponent.UserName} -{300 * Math.Max(1, opponent.Graveyard.Count(c => c.IsMonster))} LP";
            }

            // ── 融合 ─────────────────────────────────────────────────────
            if (spell.Name == "Polymerization" || spell.Name == "Miracle Fusion")
            {
                // 從手牌或場上找怪獸，嘗試特殊召喚額外牌組中的融合怪獸
                var extraFusion = caster.ExtraDeck.FirstOrDefault(c => c.IsFusion);
                if (extraFusion == null) return "額外牌組沒有融合怪獸！";
                if (caster.FirstEmptyMonsterZone() < 0) return "怪獸區已滿！";
                var fusion = extraFusion.Clone();
                fusion.SummonedThisTurn = true;
                int fSlot = caster.FirstEmptyMonsterZone();
                while (caster.MonsterZones.Count <= fSlot) caster.MonsterZones.Add(null);
                caster.MonsterZones[fSlot] = fusion;
                duel.LastPlayedCardImageUrl = fusion.RareImageUrl;
                return $"融合召喚！特殊召喚 **{fusion.Name}** (ATK {fusion.Atk})！";
            }

            // ── 場地破壞/返回 ──────────────────────────────────────────────
            if (spell.Name == "Giant Trunade")
            {
                var cHand = new List<YgoCard>();
                var oHand = new List<YgoCard>();
                for (int i = 0; i < caster.SpellTrapZones.Count; i++)
                    if (caster.SpellTrapZones[i] != null) { cHand.Add(caster.SpellTrapZones[i]!); caster.SpellTrapZones[i] = null; }
                for (int i = 0; i < opponent.SpellTrapZones.Count; i++)
                    if (opponent.SpellTrapZones[i] != null) { oHand.Add(opponent.SpellTrapZones[i]!); opponent.SpellTrapZones[i] = null; }
                caster.Hand.AddRange(cHand);
                opponent.Hand.AddRange(oHand);
                return $"大龍捲！場上全部魔陷回到手牌（我方 +{cHand.Count}，對方 +{oHand.Count}）";
            }
            if (spell.Name == "Fissure")
            {
                var weakest = opponent.MonsterZones
                    .Where(c => c != null && !c.IsDefensePosition)
                    .OrderBy(c => c!.EffectiveAtk).FirstOrDefault();
                if (weakest == null) return "對方沒有攻擊表示怪獸！";
                int z = opponent.MonsterZones.IndexOf(weakest);
                opponent.MonsterZones[z] = null;
                opponent.Graveyard.Add(weakest);
                return $"裂縫！摧毀對方 **{weakest.Name}**（ATK 最低 {weakest.EffectiveAtk}）";
            }
            if (spell.Name == "Stamping Destruction")
            {
                bool hasDragon = caster.GetMonstersOnField().Any(m =>
                    m.Attribute?.Contains("WIND") == false || m.Race?.Contains("Dragon") == true || m.Name.Contains("Dragon"));
                for (int i = 0; i < opponent.SpellTrapZones.Count; i++)
                    if (opponent.SpellTrapZones[i] != null)
                    {
                        var t = opponent.SpellTrapZones[i]!;
                        opponent.Graveyard.Add(t);
                        opponent.SpellTrapZones[i] = null;
                        opponent.LifePoints -= 500;
                        return $"踏擊破壞！摧毀對方 **{t.Name}**，{opponent.UserName} -500 LP";
                    }
                return "對方魔陷區是空的！";
            }
            if (spell.Name == "Stop Defense")
            {
                var defM = opponent.MonsterZones.FirstOrDefault(c => c != null && c.IsDefensePosition);
                if (defM == null) return "對方沒有守備表示怪獸！";
                defM.IsDefensePosition = false;
                defM.FaceDown = false;
                return $"撤除守備！**{defM.Name}** 轉為攻擊表示";
            }

            // ── ATK 強化 ─────────────────────────────────────────────────────
            if (spell.Name == "Shrink")
            {
                var target = StrongestOpponentMonster();
                if (target == null) return "對方沒有怪獸！";
                target.TempAtk = target.EffectiveAtk / 2;
                return $"**{target.Name}** ATK 減半 → {target.TempAtk}";
            }
            if (spell.Name == "Dragon Nails")
            {
                var myDragon = caster.GetMonstersOnField().OrderByDescending(m => m.EffectiveAtk).FirstOrDefault();
                if (myDragon == null) return "場上沒有我方怪獸！";
                myDragon.TempAtk = myDragon.EffectiveAtk + 600;
                return $"龍爪！**{myDragon.Name}** +600 ATK → {myDragon.TempAtk}";
            }
            if (spell.Name == "Reinforcements")
            {
                var myM = caster.GetMonstersOnField().OrderByDescending(m => m.EffectiveAtk).FirstOrDefault();
                if (myM == null) return "場上沒有我方怪獸！";
                myM.TempAtk = myM.EffectiveAtk + 500;
                return $"**{myM.Name}** +500 ATK → {myM.TempAtk}";
            }
            if (spell.Name == "Limiter Removal")
            {
                var machines = caster.GetMonstersOnField().Where(m => m.Race == "Machine" || m.Name.Contains("Cyber") || m.Name.Contains("Dragon")).ToList();
                if (!machines.Any()) return "場上沒有機械族怪獸！";
                foreach (var m in machines) m.TempAtk = m.EffectiveAtk * 2;
                return $"解除限制器！{machines.Count} 隻機械族 ATK 加倍！（本回合結束後摧毀）";
            }
            if (spell.Name == "The A. Forces")
            {
                var warriors = caster.GetMonstersOnField().ToList();
                int boost = warriors.Count * 200;
                foreach (var m in warriors) m.TempAtk = m.EffectiveAtk + boost;
                return $"A部隊！我方 {warriors.Count} 隻怪獸各 +{boost} ATK";
            }
            if (spell.Name == "Graceful Dice")
            {
                var rng = new Random();
                int roll = rng.Next(1, 7);
                var myM = caster.GetMonstersOnField().OrderByDescending(m => m.EffectiveAtk).FirstOrDefault();
                if (myM == null) return $"骰子結果 {roll}，但場上沒有怪獸！";
                myM.TempAtk = myM.EffectiveAtk * roll;
                return $"骰子結果 🎲{roll}！**{myM.Name}** ATK × {roll} → {myM.TempAtk}";
            }
            if (spell.Name == "Skull Dice")
            {
                var rng2 = new Random();
                int roll2 = rng2.Next(1, 7);
                if (roll2 == 1) roll2 = 2;
                foreach (var m in opponent.GetMonstersOnField()) m.TempAtk = m.EffectiveAtk / roll2;
                return $"骷髏骰子 🎲{roll2}！對方所有怪獸 ATK ÷ {roll2}";
            }

            // ── 手牌 / 牌組操作 ────────────────────────────────────────────
            if (spell.Name == "The Flute of Summoning Dragon")
            {
                var dragon = caster.Hand.FirstOrDefault(c => c.IsMonster &&
                    (c.Race?.Contains("Dragon") == true || c.Name.Contains("Dragon")));
                if (dragon == null) return "手牌沒有龍族怪獸！";
                if (caster.FirstEmptyMonsterZone() < 0) return "怪獸區已滿！";
                caster.Hand.Remove(dragon);
                var d = dragon.Clone(); d.SummonedThisTurn = true;
                int sl = caster.FirstEmptyMonsterZone();
                while (caster.MonsterZones.Count <= sl) caster.MonsterZones.Add(null);
                caster.MonsterZones[sl] = d;
                duel.LastPlayedCardImageUrl = d.RareImageUrl;
                return $"龍笛！從手牌特殊召喚 **{d.Name}** (ATK {d.Atk})";
            }
            if (spell.Name == "Cost Down")
            {
                if (caster.Hand.Count == 0) return "沒有手牌可棄！";
                caster.Graveyard.Add(caster.Hand[^1]);
                caster.Hand.RemoveAt(caster.Hand.Count - 1);
                return "降低費用！棄 1 張牌，本回合手牌中的怪獸需貢獻數 -1（請自行協議）";
            }
            if (spell.Name == "Scapegoat")
            {
                int placed = 0;
                for (int i = 0; i < 4; i++)
                {
                    int sl2 = caster.FirstEmptyMonsterZone();
                    if (sl2 < 0) break;
                    var token = new YgoCard { Name = "Sheep Token", Type = "Monster", FrameType = "normal",
                        Atk = 0, Def = 0, Level = 1, Race = "Beast", Attribute = "EARTH",
                        SummonedThisTurn = true };
                    while (caster.MonsterZones.Count <= sl2) caster.MonsterZones.Add(null);
                    caster.MonsterZones[sl2] = token;
                    placed++;
                }
                return $"替代羊！特殊召喚 {placed} 隻綿羊衍生物（ATK/DEF 0）";
            }
            if (spell.Name == "Reinforcement of the Army")
            {
                var warrior = caster.Deck.FirstOrDefault(c => c.IsMonster && c.Level <= 4 &&
                    (c.Race == "Warrior" || c.Name.Contains("Armed Dragon")));
                if (warrior == null) return "牌組沒有 Level 4 以下戰士族！";
                caster.Deck.Remove(warrior);
                caster.Hand.Add(warrior);
                return $"戰士的生還！**{warrior.Name}** 加入手牌";
            }
            if (spell.Name == "Cyber Repair Plant")
            {
                var cyber = caster.Deck.FirstOrDefault(c => c.IsMonster && c.Name.Contains("Cyber Dragon"));
                if (cyber == null) return "牌組沒有電子龍！";
                caster.Deck.Remove(cyber);
                caster.Hand.Add(cyber);
                return $"電子修復廠！**{cyber.Name}** 加入手牌";
            }
            if (spell.Name == "Toon Table of Contents")
            {
                var toon = caster.Deck.FirstOrDefault(c => c.Name.Contains("Toon") || c.Name.Contains("Blue-Eyes Toon"));
                if (toon == null) return "牌組沒有卡通卡片！";
                caster.Deck.Remove(toon);
                caster.Hand.Add(toon);
                return $"卡通目錄！**{toon.Name}** 加入手牌";
            }
            if (spell.Name == "Toon World")
            {
                return "卡通世界！（持續魔法，卡通怪獸現在可以攻擊對手直接攻擊）";
            }
            if (spell.Name == "Card of Safe Return")
            {
                DrawCards(caster, 1);
                return $"安全回歸！抽 1 張牌（手牌 {caster.HandCount} 張）";
            }
            if (spell.Name == "Graverobber")
            {
                var oppSpell = opponent.Graveyard.FirstOrDefault(c => c.IsSpell);
                if (oppSpell == null) return "對方墓地沒有魔法牌！";
                opponent.Graveyard.Remove(oppSpell);
                caster.LifePoints -= 2000;
                var effectMsg = ApplySpellEffect(oppSpell, duel);
                return $"掘墓賊！使用對方的 **{oppSpell.Name}**，付出 2000 LP。{effectMsg}";
            }
            if (spell.Name == "Level Up!")
            {
                var lv3 = caster.GetMonstersOnField().FirstOrDefault(m => m.Name.Contains("Armed Dragon LV3"));
                var lv5 = caster.GetMonstersOnField().FirstOrDefault(m => m.Name.Contains("Armed Dragon LV5"));
                YgoCard? nextLv = null;
                if (lv3 != null)
                {
                    caster.MonsterZones[caster.MonsterZones.IndexOf(lv3)] = null;
                    caster.Graveyard.Add(lv3);
                    nextLv = caster.Hand.FirstOrDefault(c => c.Name.Contains("Armed Dragon LV5"))
                          ?? caster.Deck.FirstOrDefault(c => c.Name.Contains("Armed Dragon LV5"));
                    if (nextLv != null) { caster.Hand.Remove(nextLv); caster.Deck.Remove(nextLv); }
                }
                else if (lv5 != null)
                {
                    caster.MonsterZones[caster.MonsterZones.IndexOf(lv5)] = null;
                    caster.Graveyard.Add(lv5);
                    nextLv = caster.Hand.FirstOrDefault(c => c.Name.Contains("Armed Dragon LV7"))
                          ?? caster.Deck.FirstOrDefault(c => c.Name.Contains("Armed Dragon LV7"));
                    if (nextLv != null) { caster.Hand.Remove(nextLv); caster.Deck.Remove(nextLv); }
                }
                if (nextLv == null) return "找不到可升級的武裝龍！";
                int sl3 = caster.FirstEmptyMonsterZone();
                if (sl3 < 0) return "怪獸區已滿！";
                var upgraded = nextLv.Clone(); upgraded.SummonedThisTurn = true;
                while (caster.MonsterZones.Count <= sl3) caster.MonsterZones.Add(null);
                caster.MonsterZones[sl3] = upgraded;
                duel.LastPlayedCardImageUrl = upgraded.RareImageUrl;
                return $"等級提升！特殊召喚 **{upgraded.Name}** (ATK {upgraded.Atk})";
            }

            // ── 儀式召喚 ─────────────────────────────────────────────────────
            if (spell.Name == "Machine Angel Ritual" || spell.Name == "Hymn of Light")
            {
                var ritualTarget = spell.Name == "Hymn of Light"
                    ? (caster.Hand.FirstOrDefault(c => c.IsMonster && c.Name.Contains("Cyber Angel Benten"))
                    ?? caster.Deck.FirstOrDefault(c => c.IsMonster && c.Name.Contains("Cyber Angel Benten")))
                    : (caster.Hand.FirstOrDefault(c => c.IsMonster && c.Name.Contains("Cyber Angel"))
                    ?? caster.Deck.FirstOrDefault(c => c.IsMonster && c.Name.Contains("Cyber Angel")));
                if (ritualTarget == null) return "手牌/牌組找不到網路天使！";
                var tribute = caster.Hand.FirstOrDefault(c => c.IsMonster && !c.Name.Contains("Cyber Angel"))
                           ?? caster.GetMonstersOnField().FirstOrDefault();
                if (tribute == null) return "沒有可貢獻的怪獸！";
                // Remove tribute
                int tribZone = caster.MonsterZones.IndexOf(tribute);
                if (tribZone >= 0) { caster.Graveyard.Add(tribute); caster.MonsterZones[tribZone] = null; }
                else { caster.Graveyard.Add(tribute); caster.Hand.Remove(tribute); }
                // Summon ritual
                caster.Hand.Remove(ritualTarget); caster.Deck.Remove(ritualTarget);
                if (caster.FirstEmptyMonsterZone() < 0) return "怪獸區已滿！";
                var ritual = ritualTarget.Clone(); ritual.SummonedThisTurn = true;
                int rslot = caster.FirstEmptyMonsterZone();
                while (caster.MonsterZones.Count <= rslot) caster.MonsterZones.Add(null);
                caster.MonsterZones[rslot] = ritual;
                duel.LastPlayedCardImageUrl = ritual.RareImageUrl;
                return $"儀式召喚！**{ritual.Name}** (ATK {ritual.Atk}) 降臨！";
            }
            if (spell.Name == "Black Illusion Ritual")
            {
                var relinquished = caster.Hand.FirstOrDefault(c => c.IsMonster && c.Name.Contains("Relinquished"))
                                ?? caster.Deck.FirstOrDefault(c => c.IsMonster && c.Name.Contains("Relinquished"));
                if (relinquished == null) return "手牌/牌組沒有解除（Relinquished）！";
                var tribute2 = caster.Hand.FirstOrDefault(c => c.IsMonster && !c.Name.Contains("Relinquished"))
                            ?? caster.GetMonstersOnField().FirstOrDefault();
                if (tribute2 == null) return "沒有可貢獻的怪獸！";
                int t2zone = caster.MonsterZones.IndexOf(tribute2);
                if (t2zone >= 0) { caster.Graveyard.Add(tribute2); caster.MonsterZones[t2zone] = null; }
                else { caster.Graveyard.Add(tribute2); caster.Hand.Remove(tribute2); }
                caster.Hand.Remove(relinquished); caster.Deck.Remove(relinquished);
                if (caster.FirstEmptyMonsterZone() < 0) return "怪獸區已滿！";
                var rSlot = caster.FirstEmptyMonsterZone();
                var r2 = relinquished.Clone(); r2.SummonedThisTurn = true;
                while (caster.MonsterZones.Count <= rSlot) caster.MonsterZones.Add(null);
                caster.MonsterZones[rSlot] = r2;
                duel.LastPlayedCardImageUrl = r2.RareImageUrl;
                return $"黑暗儀式！儀式召喚 **{r2.Name}** (ATK {r2.Atk})";
            }

            // ── 特殊召喚 ─────────────────────────────────────────────────────
            if (spell.Name == "Machine Duplication")
            {
                var cyberD = caster.Deck.Where(c => c.IsMonster && c.Name.Contains("Cyber Dragon")).Take(2).ToList();
                int placed2 = 0;
                foreach (var cd in cyberD)
                {
                    int sl4 = caster.FirstEmptyMonsterZone();
                    if (sl4 < 0) break;
                    caster.Deck.Remove(cd);
                    var cdC = cd.Clone(); cdC.SummonedThisTurn = true;
                    while (caster.MonsterZones.Count <= sl4) caster.MonsterZones.Add(null);
                    caster.MonsterZones[sl4] = cdC;
                    placed2++;
                }
                return placed2 > 0 ? $"機械增殖！從牌組特殊召喚 {placed2} 隻電子龍" : "牌組沒有電子龍！";
            }
            if (spell.Name == "Elegant Egotist")
            {
                bool hasHarpie = caster.GetMonstersOnField().Any(m => m.Name.Contains("Harpie"));
                if (!hasHarpie) return "場上沒有哈彼！";
                var harpieNext = caster.Deck.FirstOrDefault(c => c.IsMonster && c.Name.Contains("Harpie"));
                if (harpieNext == null) return "牌組沒有哈彼怪獸！";
                if (caster.FirstEmptyMonsterZone() < 0) return "怪獸區已滿！";
                caster.Deck.Remove(harpieNext);
                var hC = harpieNext.Clone(); hC.SummonedThisTurn = true;
                int hSlot = caster.FirstEmptyMonsterZone();
                while (caster.MonsterZones.Count <= hSlot) caster.MonsterZones.Add(null);
                caster.MonsterZones[hSlot] = hC;
                duel.LastPlayedCardImageUrl = hC.RareImageUrl;
                return $"高傲自我！從牌組特殊召喚 **{hC.Name}** (ATK {hC.Atk})";
            }
            if (spell.Name == "Harpie's Hunting Ground")
            {
                // Field spell: SS Harpie → destroy 1 opponent S/T
                for (int i = 0; i < opponent.SpellTrapZones.Count; i++)
                    if (opponent.SpellTrapZones[i] != null)
                    {
                        var st = opponent.SpellTrapZones[i]!;
                        opponent.Graveyard.Add(st);
                        opponent.SpellTrapZones[i] = null;
                        return $"哈彼的狩獵場！摧毀對方 **{st.Name}**";
                    }
                return "哈彼的狩獵場啟動！（對方無魔陷可摧毀）";
            }
            if (spell.Name == "Bubble Shuffle")
            {
                var hero = caster.GetMonstersOnField().FirstOrDefault(m => m.Name.Contains("Elemental HERO"));
                if (hero != null) hero.IsDefensePosition = true;
                DrawCards(caster, 1);
                return $"泡泡混洗！{(hero != null ? hero.Name + " 轉守備" : "")} 抽 1 張牌";
            }

            // ── LP 增減 / 犧牲效果 ─────────────────────────────────────────
            if (spell.Name == "Mystik Wok")
            {
                var tribute3 = caster.GetMonstersOnField().OrderByDescending(m => m.EffectiveAtk).FirstOrDefault();
                if (tribute3 == null) return "場上沒有怪獸可以貢獻！";
                int gain = tribute3.EffectiveAtk;
                int tz = caster.MonsterZones.IndexOf(tribute3);
                if (tz >= 0) { caster.MonsterZones[tz] = null; caster.Graveyard.Add(tribute3); }
                caster.LifePoints += gain;
                return $"神秘中華料理！貢獻 **{tribute3.Name}**，回復 {gain} LP → {caster.LifePoints}";
            }
            if (spell.Name == "Ectoplasmer")
            {
                var tribute4 = caster.GetMonstersOnField().OrderByDescending(m => m.EffectiveAtk).FirstOrDefault();
                if (tribute4 == null) return "場上沒有怪獸！";
                int dmgEcto = tribute4.EffectiveAtk / 2;
                int ez = caster.MonsterZones.IndexOf(tribute4);
                if (ez >= 0) { caster.MonsterZones[ez] = null; caster.Graveyard.Add(tribute4); }
                opponent.LifePoints -= dmgEcto;
                return $"靈體外出！貢獻 **{tribute4.Name}**，{opponent.UserName} -{dmgEcto} LP";
            }
            if (spell.Name == "Soul Exchange")
            {
                var target2 = StrongestOpponentMonster();
                if (target2 == null) return "對方沒有怪獸！";
                int oIdx = opponent.MonsterZones.IndexOf(target2);
                opponent.MonsterZones[oIdx] = null;
                opponent.Graveyard.Add(target2);
                return $"靈魂交換！將對方的 **{target2.Name}** 作為貢獻（送墓地）";
            }
            if (spell.Name == "My Body as a Shield")
            {
                caster.LifePoints -= 1500;
                if (caster.LifePoints <= 1) caster.LifePoints = 1;
                return $"以我的身體為盾！付出 1500 LP，下次我方怪獸不會被破壞（本次已模擬付費）";
            }
            if (spell.Name == "Evolution Burst")
            {
                var advCyber = caster.GetMonstersOnField().FirstOrDefault(m => m.Level >= 6 && m.Name.Contains("Cyber"));
                if (advCyber == null) return "場上沒有高等電子龍！";
                caster.MonsterZones[caster.MonsterZones.IndexOf(advCyber)] = null;
                caster.Graveyard.Add(advCyber);
                var targetCard = StrongestOpponentMonster();
                if (targetCard != null)
                {
                    int tcIdx = opponent.MonsterZones.IndexOf(targetCard);
                    opponent.MonsterZones[tcIdx] = null;
                    opponent.Graveyard.Add(targetCard);
                    return $"進化爆炸！送 **{advCyber.Name}** 到墓地，摧毀對方 **{targetCard.Name}**！";
                }
                return $"進化爆炸！送 **{advCyber.Name}** 到墓地（對方無怪獸）";
            }
            if (spell.Name == "System Down")
            {
                caster.LifePoints -= 1000;
                int removed = 0;
                for (int i = 0; i < opponent.MonsterZones.Count; i++)
                    if (opponent.MonsterZones[i] != null &&
                        (opponent.MonsterZones[i]!.Race == "Machine" || opponent.MonsterZones[i]!.Name.Contains("Cyber")))
                    { opponent.Graveyard.Add(opponent.MonsterZones[i]!); opponent.MonsterZones[i] = null; removed++; }
                return $"系統重置！付 1000 LP，除外對方 {removed} 隻機械族！";
            }
            if (spell.Name == "Power Bond")
            {
                // Fusion summon with ATK doubled, caster takes original ATK as damage at end of turn
                var extraF = caster.ExtraDeck.FirstOrDefault(c => c.IsFusion && c.Name.Contains("Cyber"));
                if (extraF == null) return "額外牌組沒有電子融合怪獸！";
                if (caster.FirstEmptyMonsterZone() < 0) return "怪獸區已滿！";
                var fusion = extraF.Clone(); fusion.SummonedThisTurn = true;
                int origAtk = fusion.Atk;
                fusion.TempAtk = fusion.Atk * 2;
                int pbSlot = caster.FirstEmptyMonsterZone();
                while (caster.MonsterZones.Count <= pbSlot) caster.MonsterZones.Add(null);
                caster.MonsterZones[pbSlot] = fusion;
                caster.PendingEndTurnDamage += origAtk;
                duel.LastPlayedCardImageUrl = fusion.RareImageUrl;
                return $"力量紐帶！融合召喚 **{fusion.Name}** (ATK {fusion.TempAtk})！回合結束時 -{origAtk} LP";
            }
            if (spell.Name == "Comic Hand")
            {
                var target3 = StrongestOpponentMonster();
                if (target3 == null) return "對方沒有怪獸！";
                int ct3z = opponent.MonsterZones.IndexOf(target3);
                opponent.MonsterZones[ct3z] = null;
                var stolen2 = target3.Clone();
                int stSlot = caster.FirstEmptyMonsterZone();
                if (stSlot < 0) return "我方怪獸區已滿！";
                while (caster.MonsterZones.Count <= stSlot) caster.MonsterZones.Add(null);
                caster.MonsterZones[stSlot] = stolen2;
                return $"漫畫之手！取得 **{stolen2.Name}** 的控制權！";
            }

            // ── 陷阱效果（手動發動版）────────────────────────────────────────
            if (spell.Name == "Mirror Force")
            {
                int cnt2 = 0;
                for (int i = 0; i < opponent.MonsterZones.Count; i++)
                    if (opponent.MonsterZones[i] != null && !opponent.MonsterZones[i]!.IsDefensePosition)
                    { opponent.Graveyard.Add(opponent.MonsterZones[i]!); opponent.MonsterZones[i] = null; cnt2++; }
                return $"聖光護盾！摧毀對方 {cnt2} 隻攻擊表示怪獸！";
            }
            if (spell.Name == "Magic Cylinder")
            {
                var strongestOpp = StrongestOpponentMonster();
                if (strongestOpp == null) return "對方沒有怪獸！";
                int cylDmg = strongestOpp.EffectiveAtk;
                opponent.LifePoints -= cylDmg;
                return $"魔法筒！無效攻擊，{opponent.UserName} -{cylDmg} LP → {opponent.LifePoints}";
            }
            if (spell.Name == "Spellbinding Circle")
            {
                var sTarget = StrongestOpponentMonster();
                if (sTarget == null) return "對方沒有怪獸！";
                sTarget.CannotAttack = true;
                sTarget.CannotChangePosition = true;
                return $"封印魔輪！**{sTarget.Name}** 無法攻擊或改變表示形式";
            }
            if (spell.Name == "Negate Attack")
            {
                if (duel.CurrentPhase == DuelPhase.BattlePhase)
                {
                    duel.CurrentPhase = DuelPhase.MainPhase2;
                    return "攻擊無效！戰鬥階段結束，進入主要階段 2";
                }
                return "攻擊無效！（無效對方 1 次攻擊，結束戰鬥階段）";
            }
            if (spell.Name == "Magic Jammer")
            {
                if (caster.Hand.Count == 0) return "沒有手牌可棄！";
                caster.Graveyard.Add(caster.Hand[^1]);
                caster.Hand.RemoveAt(caster.Hand.Count - 1);
                for (int i = opponent.SpellTrapZones.Count - 1; i >= 0; i--)
                    if (opponent.SpellTrapZones[i] != null)
                    {
                        opponent.Graveyard.Add(opponent.SpellTrapZones[i]!);
                        opponent.SpellTrapZones[i] = null;
                        return "魔法干擾！棄 1 張牌，無效對方 1 張魔陷！";
                    }
                return "魔法干擾！棄 1 張牌（對方無魔陷可無效）";
            }
            if (spell.Name == "OJAMA Trio")
            {
                int tokensPlaced = 0;
                for (int i = 0; i < 3; i++)
                {
                    int oSlot = 0;
                    for (int j = 0; j < 5; j++)
                        if (j >= opponent.MonsterZones.Count || opponent.MonsterZones[j] == null) { oSlot = j; break; }
                    var ojTok = new YgoCard { Name = "OJAMA Token", Type = "Monster", FrameType = "normal",
                        Atk = 0, Def = 1000, Level = 2, Race = "Beast", Attribute = "LIGHT",
                        IsDefensePosition = true, SummonedThisTurn = true };
                    while (opponent.MonsterZones.Count <= oSlot) opponent.MonsterZones.Add(null);
                    if (opponent.MonsterZones[oSlot] == null) { opponent.MonsterZones[oSlot] = ojTok; tokensPlaced++; }
                }
                return $"腐叫聲三連！在對方場上放置 {tokensPlaced} 隻 OJAMA Token（ATK 0/DEF 1000）";
            }
            if (spell.Name == "Threatening Roar")
            {
                opponent.CannotDeclareAttackThisTurn = true;
                return $"威嚇咆哮！{opponent.UserName} 本回合無法宣告攻擊！";
            }
            if (spell.Name == "Waboku")
            {
                caster.WabokuActive = true;
                return "和睦的使者！本回合戰鬥傷害無效！";
            }
            if (spell.Name == "Trap Hole")
            {
                var tHole = opponent.GetMonstersOnField()
                    .Where(m => m.EffectiveAtk >= 1000 && m.SummonedThisTurn)
                    .OrderByDescending(m => m.EffectiveAtk).FirstOrDefault()
                    ?? opponent.GetMonstersOnField().Where(m => m.EffectiveAtk >= 1000)
                    .OrderByDescending(m => m.EffectiveAtk).FirstOrDefault();
                if (tHole == null) return "對方沒有 ATK 1000 以上的怪獸！";
                int thz = opponent.MonsterZones.IndexOf(tHole);
                opponent.MonsterZones[thz] = null;
                opponent.Graveyard.Add(tHole);
                return $"落穴！摧毀 **{tHole.Name}**（ATK {tHole.EffectiveAtk}）";
            }
            if (spell.Name == "Nightmare Wheel")
            {
                var nwTarget = StrongestOpponentMonster();
                if (nwTarget == null) return "對方沒有怪獸！";
                nwTarget.CannotAttack = true;
                nwTarget.CannotChangePosition = true;
                opponent.LifePoints -= 500;
                return $"惡夢之輪！**{nwTarget.Name}** 無法攻擊/換表示，{opponent.UserName} -500 LP";
            }
            if (spell.Name == "Hysteric Party")
            {
                if (caster.Hand.Count == 0) return "沒有手牌可棄！";
                caster.Graveyard.Add(caster.Hand[^1]);
                caster.Hand.RemoveAt(caster.Hand.Count - 1);
                var harpiesInGY = caster.Graveyard.Where(c => c.IsMonster && c.Name.Contains("Harpie")).Take(3).ToList();
                int hpPlaced = 0;
                foreach (var hp in harpiesInGY)
                {
                    int hpSlot = caster.FirstEmptyMonsterZone();
                    if (hpSlot < 0) break;
                    caster.Graveyard.Remove(hp);
                    var hpC2 = hp.Clone(); hpC2.SummonedThisTurn = true;
                    while (caster.MonsterZones.Count <= hpSlot) caster.MonsterZones.Add(null);
                    caster.MonsterZones[hpSlot] = hpC2;
                    hpPlaced++;
                }
                return $"歇斯底里派對！棄 1 張牌，從墓地特殊召喚 {hpPlaced} 隻哈彼！";
            }
            if (spell.Name == "Hero Signal")
            {
                var heroInHand = caster.Hand.FirstOrDefault(c => c.IsMonster && c.Name.Contains("HERO"));
                if (heroInHand == null) return "手牌沒有HERO！";
                int hsSlot = caster.FirstEmptyMonsterZone();
                if (hsSlot < 0) return "怪獸區已滿！";
                caster.Hand.Remove(heroInHand);
                var hsC = heroInHand.Clone(); hsC.SummonedThisTurn = true;
                while (caster.MonsterZones.Count <= hsSlot) caster.MonsterZones.Add(null);
                caster.MonsterZones[hsSlot] = hsC;
                duel.LastPlayedCardImageUrl = hsC.RareImageUrl;
                return $"英雄信號！從手牌特殊召喚 **{hsC.Name}** (ATK {hsC.Atk})";
            }
            if (spell.Name == "Destiny Board")
            {
                opponent.LifePoints -= 500;
                return $"命運之板！F-I-N-A-L 計劃開始！（{opponent.UserName} -500 LP，完整效果請手動協議）";
            }

            return $"效果：{spell.Desc?.Split('.').FirstOrDefault() ?? "（無法自動執行，請手動協議）"}";
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

            // 重置回合旗標
            duel.Field1.WabokuActive = false;
            duel.Field2.WabokuActive = false;
            duel.Field1.CannotDeclareAttackThisTurn = false;
            duel.Field2.CannotDeclareAttackThisTurn = false;
            foreach (var f in new[] { duel.Field1, duel.Field2 })
                foreach (var m in f.GetMonstersOnField())
                { m.CannotAttack = false; m.CannotChangePosition = false; }

            // Power Bond 結算傷害
            if (duel.CurrentField.PendingEndTurnDamage > 0)
            {
                duel.CurrentField.LifePoints -= duel.CurrentField.PendingEndTurnDamage;
                duel.AddLog($"⚡ Power Bond 結算：{duel.CurrentField.UserName} -{duel.CurrentField.PendingEndTurnDamage} LP → {duel.CurrentField.LifePoints}");
                duel.CurrentField.PendingEndTurnDamage = 0;
            }

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

            cb.AddRow(row1);
            cb.AddRow(row2);

            // Row 3: 發動伏地牌（若場上有伏地的魔陷）
            bool hasSetST = isMyTurn && duel.CurrentField.SpellTrapZones.Any(c => c != null && c.FaceDown);
            if (hasSetST)
            {
                var row3 = new ActionRowBuilder();
                row3.WithButton("⚡ 發動伏地牌", $"ygo_stmenu_{did}", ButtonStyle.Primary);
                cb.AddRow(row3);
            }

            return cb;
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

        private async Task DeleteDuelAsync(ulong channelId)
        {
            _memDuels.Remove(channelId);
            if (_useRedis)
            {
                try
                {
                    await _redisDb!.KeyDeleteAsync(DUEL_KEY + channelId);
                    await _redisDb!.KeyDeleteAsync(CHAN_KEY + channelId);
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
                new() {
                Key = "chazz", CharacterName = "万丈目準", Series = "GX",
                Emoji = "🏆", Color = 0xFFD700,
                AiPersonality = "你是萬丈目準，傲慢自大的決鬥者，使用武裝龍和腐叫聲牌組",
                MainDeckNames = new()
                {
                    "Armed Dragon LV3","Armed Dragon LV3","Armed Dragon LV5","Armed Dragon LV5",
                    "Armed Dragon LV7","OJAMA Yellow","OJAMA Green","OJAMA Black",
                    "Sangan","Big Shield Gardna",
                    "Level Up!","Level Up!","The A. Forces","Reinforcement of the Army",
                    "Graceful Charity","Monster Reborn","Fissure","Stamping Destruction",
                    "OJAMA Trio","Threatening Roar"
                },
                ExtraDeckNames = new() { "OJAMA King" }
            },
            new() {
                Key = "alexis", CharacterName = "天上院明日香", Series = "GX",
                Emoji = "🌸", Color = 0xFF69B4,
                AiPersonality = "你是天上院明日香，高雅的決鬥者，使用網路天使儀式牌組",
                MainDeckNames = new()
                {
                    "Cyber Angel Benten","Cyber Angel Benten","Cyber Angel Idaten","Cyber Angel Idaten",
                    "Etoile Cyber","Etoile Cyber","Blade Skater","Blade Skater",
                    "Cyber Gymnast","Cyber Tutu","Shining Angel","Shining Angel",
                    "Machine Angel Ritual","Machine Angel Ritual","Hymn of Light","Graceful Charity",
                    "Monster Reborn","Dark Hole","Negate Attack","My Body as a Shield"
                },
                ExtraDeckNames = new()
            },
            new() {
                Key = "zane", CharacterName = "丸藤亮", Series = "GX",
                Emoji = "⚙️", Color = 0x4169E1,
                AiPersonality = "你是丸藤亮，冷酷強大的決鬥者，使用電子龍機械牌組",
                MainDeckNames = new()
                {
                    "Cyber Dragon","Cyber Dragon","Cyber Dragon",
                    "Proto-Cyber Dragon","Proto-Cyber Dragon","Cyber Dragon Core","Cyber Dragon Core",
                    "Cyber Barrier Dragon","Cyber Laser Dragon","Attachment Cybern",
                    "Power Bond","Power Bond","Machine Duplication","Cyber Repair Plant",
                    "Limiter Removal","System Down","Evolution Burst",
                    "Monster Reborn","Dark Hole","Negate Attack"
                },
                ExtraDeckNames = new() { "Cyber Twin Dragon","Cyber End Dragon","Chimeratech Overdragon" }
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
            };

            return list.ToDictionary(d => d.Key);
        }

        public IReadOnlyDictionary<string, AnimeDeckDefinition> GetDeckDefinitions() => _decks;
    }
}
