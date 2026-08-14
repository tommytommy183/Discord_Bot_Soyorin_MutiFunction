using Discord;
using MusicBot2.Helpers;
using MusicBot2.Models;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    /// <summary>
    /// 聖杯卡牌爬塔系統 (FGO 核心卡牌戰鬥與 Redis 儲存版本)
    /// </summary>
    public class HolyGrailTowerService
    {
        private readonly HttpClient _http;
        private readonly Random _rng = new();
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly IDatabase _db;

        private List<FgoBasicServant> _servantPool = new();
        private readonly Dictionary<int, TowerServant> _servantCache = new();
        private readonly Dictionary<ulong, List<HgwPendingVisual>> _pendingVisuals = new();
        private bool _initialized = false;

        private readonly Dictionary<ulong, HgwTowerRun> _runs = new();

        private const string RedisPlayerPrefix = "hgwt_player:";
        private const string BasicServantUrl = "https://api.atlasacademy.io/export/TW/basic_servant.json";
        private const string NiceServantUrl = "https://api.atlasacademy.io/nice/TW/servant/{0}?lore=false";

        public HolyGrailTowerService(string redisConnectionString)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            try
            {
                var redis = ConnectionMultiplexer.Connect(redisConnectionString);
                _db = redis.GetDatabase();
                Console.WriteLine("[HolyGrailTower] Redis 連線建立成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HolyGrailTower] Redis 連線異常: {ex.Message}");
            }
        }

        public List<HgwPendingVisual> ConsumePendingVisuals(ulong channelId)
        {
            if (!_pendingVisuals.TryGetValue(channelId, out var visuals))
                return new List<HgwPendingVisual>();

            _pendingVisuals.Remove(channelId);
            return visuals;
        }

        private void QueueVisual(ulong channelId, string title, string description, string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return;

            if (!_pendingVisuals.TryGetValue(channelId, out var visuals))
            {
                visuals = new List<HgwPendingVisual>();
                _pendingVisuals[channelId] = visuals;
            }

            visuals.Add(new HgwPendingVisual
            {
                Title = title,
                Description = description,
                ImageUrl = imageUrl
            });
        }

        // ═══════════════════════════════════════════════════════════
        //  Redis 儲存與讀取 (御主永久存檔)
        // ═══════════════════════════════════════════════════════════

        private HolyGrailTowerPlayer LoadPlayer(ulong userId, string defaultName = "")
        {
            try
            {
                var redisKey = RedisPlayerPrefix + userId;
                var json = _db.StringGet(redisKey);
                if (json.HasValue)
                {
                    var player = JsonSerializer.Deserialize<HolyGrailTowerPlayer>(json);
                    if (player != null) return player;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HolyGrailTower] 載入 {userId} 存檔失敗: {ex.Message}");
            }

            return new HolyGrailTowerPlayer
            {
                UserId = userId,
                UserName = defaultName,
                SummonTickets = 15, // 贈送 15 張
                SaintQuartz = 10
            };
        }

        private void SavePlayer(HolyGrailTowerPlayer player)
        {
            try
            {
                var redisKey = RedisPlayerPrefix + player.UserId;
                var json = JsonSerializer.Serialize(player, new JsonSerializerOptions { WriteIndented = true });
                _db.StringSet(redisKey, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HolyGrailTower] 儲存 player {player.UserId} 失敗: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  主要指令 (登錄, 查詢, 每日, 召喚)
        // ═══════════════════════════════════════════════════════════

        public async Task<(Embed embed, ComponentBuilder component)> RegisterPlayerAsync(ulong userId, string userName)
        {
            await EnsureInitAsync();

            var redisKey = RedisPlayerPrefix + userId;
            if (_db.KeyExists(redisKey))
            {
                var player = LoadPlayer(userId, userName);
                var text = $"**{userName}** 已是聖杯塔的資深御主了！\n現在就組建隊伍開始挑戰吧！";
                var embed = new EmbedBuilder()
                    .WithTitle("⚜️ 聖杯塔御主資訊")
                    .WithDescription(text)
                    .AddField("🎟️ 召喚券", player.SummonTickets, inline: true)
                    .AddField("💎 聖晶石", player.SaintQuartz, inline: true)
                    .AddField("🎴 招募英靈", $"{player.OwnedServants.Count} 位", inline: true)
                    .AddField("🗼 最高層數", $"第 {player.HighestFloor} 層", inline: true)
                    .WithColor(Color.Purple)
                    .WithCurrentTimestamp()
                    .Build();

                return (embed, new ComponentBuilder());
            }

            var newPlayer = new HolyGrailTowerPlayer
            {
                UserId = userId,
                UserName = userName,
                SummonTickets = 15,
                SaintQuartz = 10
            };

            SavePlayer(newPlayer);

            var welcome = new EmbedBuilder()
                .WithTitle("👑 歡迎來到 FGO 聖杯爬塔冒險！")
                .WithDescription($"**{userName}** 成功喚醒了迦勒底魔力迴路！\n\n" +
                    "獲得初始資源：\n" +
                    "🎟️ 15 張召喚券\n" +
                    "💎 10 顆聖晶石\n\n" +
                    "使用 `/fate聖杯塔召喚` 來召喚你首批並肩作戰的從者吧！")
                .WithColor(Color.Gold)
                .WithCurrentTimestamp()
                .Build();

            return (welcome, new ComponentBuilder());
        }

        public (Embed embed, ComponentBuilder component) GetPlayerInfo(ulong userId)
        {
            var player = LoadPlayer(userId);
            var embed = new EmbedBuilder()
                .WithTitle($"⚜️ 御主資訊 - {player.UserName}")
                .AddField("🎟️ 召喚券", player.SummonTickets, inline: true)
                .AddField("💎 聖晶石", player.SaintQuartz, inline: true)
                .AddField("🎴 招募英靈", $"{player.OwnedServants.Count} 位", inline: true)
                .AddField("🗼 本次冒險最高紀錄", $"第 {player.HighestFloor} 層", inline: true)
                .AddField("⚔️ 累計總挑戰次數", player.TotalRuns, inline: true)
                .AddField("💀 累計擊殺魔物數", player.TotalKills, inline: true)
                .WithColor(Color.Purple)
                .WithCurrentTimestamp()
                .Build();

            return (embed, new ComponentBuilder());
        }

        public async Task<(Embed embed, ComponentBuilder component)> ClaimDailyRewardAsync(ulong userId, string userName)
        {
            var player = LoadPlayer(userId, userName);
            var now = DateTime.UtcNow;

            if (player.LastDailyReward.HasValue && (now - player.LastDailyReward.Value).TotalHours < 20)
            {
                var remaining = player.LastDailyReward.Value.AddHours(20) - now;
                return (CommonHelper.BuildErrorResponse($"今日獎勵已領取！\n距離下次能領取時間：{remaining.Hours}小時 {remaining.Minutes}分鐘後").Item2, new ComponentBuilder());
            }

            player.SummonTickets += 5;
            player.SaintQuartz += 3;
            player.LastDailyReward = now;
            SavePlayer(player);

            var embed = new EmbedBuilder()
                .WithTitle("🎁 每日物資配給")
                .WithDescription($"**{userName}** 獲得了今日迦勒底的支援！\n\n🎟️ +5 召喚券\n💎 +3 聖晶石\n\n獲得重置召喚的歐氣吧！")
                .WithColor(Color.Green)
                .WithCurrentTimestamp()
                .Build();

            return (embed, new ComponentBuilder());
        }

        public async Task<(Embed embed, ComponentBuilder component)> SummonServantAsync(ulong userId, string userName)
        {
            await EnsureInitAsync();

            var player = LoadPlayer(userId, userName);
            if (player.SummonTickets < 1)
            {
                return (CommonHelper.BuildErrorResponse("召喚券不足！請等待每日領取。").Item2, new ComponentBuilder());
            }

            player.SummonTickets--;

            // FGO 卡牌抽卡機率大幅度上調（SSR 10%, SR 22%)
            var rarity = RollRarity();
            var candidates = _servantPool.Where(s => s.Rarity == rarity).ToList();
            if (candidates.Count == 0)
                candidates = _servantPool.Where(s => s.Rarity == 3).ToList();

            var basic = candidates[_rng.Next(candidates.Count)];

            // 取詳細寶具、5張指令卡與頭像
            var detailed = await FetchAndCacheServantAsync(basic.CollectionNo);

            var existing = player.OwnedServants.FirstOrDefault(s => s.CollectionNo == detailed.CollectionNo);
            bool isNew = existing == null;
            string resultText;

            if (isNew)
            {
                player.OwnedServants.Add(detailed);
                resultText = "✨ **NEW! 命定的邂逅！全新英靈加入！**";
            }
            else
            {
                existing.NpLevel = Math.Min(5, existing.NpLevel + 1);
                existing.Level = Math.Min(100, existing.Level + 5); // 重複抽到可提升等級
                resultText = $"🎭 **重複召喚！** 英靈等級上限提昇，寶具等級強化：**Lv.{existing.NpLevel}**！";
                detailed = existing;
            }

            SavePlayer(player);

            string classEmoji = GetClassEmoji(detailed.ClassName);
            string rarityStars = string.Concat(Enumerable.Repeat("★", detailed.Rarity));
            string cardsShow = string.Join(" | ", detailed.Cards.Select(c => c.ToUpper() switch
            {
                "BUSTER" => "🔴 B",
                "ARTS" => "🔵 A",
                "QUICK" => "🟢 Q",
                _ => c
            }));

            var embed = new EmbedBuilder()
                .WithTitle(resultText)
                .WithDescription($"**御主 {userName}** 透過聖杯召喚了：\n\n" +
                    $"{classEmoji} **{detailed.Name}**\n" +
                    $"{rarityStars}\n" +
                    $"寶具：『{detailed.NpName}』（{detailed.NpRuby ?? ""}）\n" +
                    $"指令卡配置：[{cardsShow}]\n" +
                    $"HP: {detailed.GetMaxHp()} | ATK: {detailed.GetAttack()}")
                .WithColor(GetRarityColor(detailed.Rarity))
                .WithImageUrl(detailed.FullImageUrl ?? "")
                .WithFooter($"剩餘召喚券：{player.SummonTickets} 張 | 圖鑑總數：{player.OwnedServants.Count} 位")
                .WithCurrentTimestamp()
                .Build();

            return (embed, new ComponentBuilder());
        }

        private int RollRarity()
        {
            var roll = _rng.Next(100);
            return roll switch
            {
                < 10 => 5,    // 10% SSR
                < 32 => 4,    // 22% SR
                < 67 => 3,    // 35% R
                < 90 => 2,    // 23% UC
                _ => 1        // 10% C
            };
        }

        public (Embed embed, ComponentBuilder component) ListServants(ulong userId)
        {
            var player = LoadPlayer(userId);
            if (player.OwnedServants.Count == 0)
                return (CommonHelper.BuildErrorResponse("你尚未召喚任何英靈！請先使用 `/fate聖杯塔召喚`").Item2, new ComponentBuilder());

            var embedBuilder = new EmbedBuilder()
                .WithTitle($"🎴 {player.UserName} 的英靈圖鑑")
                .WithColor(Color.Blue)
                .WithCurrentTimestamp();

            var sorted = player.OwnedServants.OrderByDescending(s => s.Rarity).ThenByDescending(s => s.Level).ToList();
            var displayLimit = Math.Min(15, sorted.Count);

            for (int i = 0; i < displayLimit; i++)
            {
                var s = sorted[i];
                string classEmoji = GetClassEmoji(s.ClassName);
                string stars = string.Concat(Enumerable.Repeat("★", s.Rarity));
                embedBuilder.AddField(
                    $"{classEmoji} {s.Name} Lv.{s.Level} (寶具 Lv.{s.NpLevel})",
                    $"{stars}\nATK: {s.GetAttack()} | HP: {s.GetMaxHp()}\nNo. {s.CollectionNo}",
                    inline: true);
            }

            if (sorted.Count > displayLimit)
                embedBuilder.WithFooter($"已顯示前 {displayLimit} /共 {sorted.Count} 位英靈");

            return (embedBuilder.Build(), new ComponentBuilder());
        }

        // ═══════════════════════════════════════════════════════════
        //  爬塔主控邏輯 (開局, 回合行為, 結算)
        // ═══════════════════════════════════════════════════════════

        public async Task<(Embed embed, ComponentBuilder component)> StartTowerRunAsync(ulong channelId, ulong userId, string userName)
        {
            await EnsureInitAsync();

            if (_runs.ContainsKey(channelId))
            {
                return (CommonHelper.BuildErrorResponse("頻道內正有一場聖杯旅途進行中！\n請使用 `/fate聖杯塔取消爬塔` 放棄。").Item2, new ComponentBuilder());
            }

            var player = LoadPlayer(userId, userName);
            if (player.OwnedServants.Count == 0)
            {
                return (CommonHelper.BuildErrorResponse("召喚名冊中尚無隨行英靈。請先抽取英靈！").Item2, new ComponentBuilder());
            }

            var run = new HgwTowerRun
            {
                ChannelId = channelId,
                PlayerId = userId,
                PlayerName = userName,
                CurrentFloor = 1,
                Gold = 100
            };

            _runs[channelId] = run;

            // 建立出征陣容組建 UI (一次至多選擇3名從者)
            return BuildTeamSelectionScreen(run, player);
        }

        public async Task<(Embed embed, ComponentBuilder component)> CancelTowerRunAsync(ulong channelId, ulong userId)
        {
            if (!_runs.TryGetValue(channelId, out var run))
            {
                return (CommonHelper.BuildErrorResponse("此頻道目前沒有進行中的聖杯塔征途。").Item2, new ComponentBuilder());
            }

            if (run.PlayerId != userId)
            {
                return (CommonHelper.BuildErrorResponse("這不是你的挑戰！只有當前挑戰者可放棄。").Item2, new ComponentBuilder());
            }

            _runs.Remove(channelId);

            var embed = new EmbedBuilder()
                .WithTitle("🏳️ 放棄聖杯探索")
                .WithDescription($"御主 **{run.PlayerName}** 放棄了本趟旅程，在 **第 {run.CurrentFloor} 層** 全員撤退！")
                .WithColor(Color.DarkRed)
                .WithCurrentTimestamp()
                .Build();

            return (embed, new ComponentBuilder());
        }

        private (Embed embed, ComponentBuilder component) BuildTeamSelectionScreen(HgwTowerRun run, HolyGrailTowerPlayer player)
        {
            var list = player.OwnedServants.OrderByDescending(s => s.Rarity).ToList();

            var embedBuilder = new EmbedBuilder()
                .WithTitle("🗼 聖杯探索隊伍組建")
                .WithDescription($"**御主 {player.UserName}**，請挑選出征英靈 (最多 3 位，目前已選：{run.Team.Count}/3)：\n" +
                    "點擊下方按鈕可加入或退出陣容")
                .WithColor(Color.LightOrange);

            if (run.Team.Count > 0)
            {
                string selectedNames = string.Join("\n", run.Team.Select((s, idx) => $"{idx + 1}. {GetClassEmoji(s.ClassName)} **{s.Name}**"));
                embedBuilder.AddField("🛡️ 已確認出征英靈", selectedNames);
            }
            else
            {
                embedBuilder.AddField("🛡️ 已確認出征英靈", "（尚未挑選，英靈戰死將結束副本）");
            }

            var cb = new ComponentBuilder();
            int buttonCount = 0;

            foreach (var servant in list.Take(15))
            {
                string emoji = GetClassEmoji(servant.ClassName);
                bool isChosen = run.Team.Any(x => x.CollectionNo == servant.CollectionNo);
                ButtonStyle style = isChosen ? ButtonStyle.Success : ButtonStyle.Secondary;
                string label = $"{emoji} {servant.Name} (Lv.{servant.Level})";

                cb.WithButton(label, $"hgwt_select_{servant.CollectionNo}", style, disabled: run.Team.Count >= 3 && !isChosen, row: buttonCount / 4);
                buttonCount++;
            }

            // 出征確認戰鬥按鈕
            cb.WithButton("🚀 誓約出征！進入第 1 層", "hgwt_start", ButtonStyle.Danger, disabled: run.Team.Count == 0, row: 4);

            return (embedBuilder.Build(), cb);
        }

        // ═══════════════════════════════════════════════════════════
        //  遭遇事件生成 (分岐路線模式)
        // ═══════════════════════════════════════════════════════════

        private void GenerateEncounter(HgwTowerRun run)
        {
            int floor = run.CurrentFloor;
            EncounterType type;

            if (floor % 10 == 0) type = EncounterType.BossBattle;
            else if (floor % 5 == 0) type = EncounterType.EliteBattle;
            else
            {
                // 普通、寶箱、商店隨機
                int roll = _rng.Next(100);
                if (roll < 55) type = EncounterType.NormalBattle;
                else if (roll < 75) type = EncounterType.Treasure;
                else if (roll < 90) type = EncounterType.Shop;
                else type = EncounterType.RestSite;
            }

            var encounter = new HgwTowerEncounter
            {
                Type = type,
                TurnCount = 1,
                CritStars = 0
            };

            Console.WriteLine($"[HolyGrailTower] 產生遭遇：channel={run.ChannelId}, floor={floor}, type={type}");

            if (type == EncounterType.NormalBattle || type == EncounterType.EliteBattle || type == EncounterType.BossBattle)
            {
                foreach (var servant in run.Team)
                {
                    servant.UsedSkillIndexes.Clear();
                }

                int count = type == EncounterType.BossBattle ? 1 : _rng.Next(1, 4);
                encounter.Enemies = GenerateMonsterSquad(type, count, floor);
                DrawCardsForTurn(run, encounter); // 初始手牌
            }

            run.CurrentEncounter = encounter;
        }

        private List<HgwTowerEnemy> GenerateMonsterSquad(EncounterType type, int count, int floor)
        {
            var list = new List<HgwTowerEnemy>();
            string[] classes = { "saber", "archer", "lancer", "rider", "caster", "assassin", "berserker" };

            if (type == EncounterType.BossBattle)
            {
                list.Add(new HgwTowerEnemy
                {
                    Name = $"👑 聖杯狂宿·領主魔神 (Floor {floor})",
                    ClassName = "berserker",
                    MaxHp = 8000 + floor * 1500,
                    CurrentHp = 8000 + floor * 1500,
                    Attack = 350 + floor * 45,
                    Defense = 120 + floor * 20,
                    IsBoss = true
                });
            }
            else if (type == EncounterType.EliteBattle)
            {
                list.Add(new HgwTowerEnemy
                {
                    Name = $"💀 護陵巨影者 (精英)",
                    ClassName = classes[_rng.Next(classes.Length)],
                    MaxHp = 3000 + floor * 700,
                    CurrentHp = 3000 + floor * 700,
                    Attack = 220 + floor * 30,
                    Defense = 80 + floor * 12,
                    IsElite = true
                });
            }
            else
            {
                for (int i = 1; i <= count; i++)
                {
                    string mName = _rng.Next(3) switch
                    {
                        0 => "古代骷髏兵",
                        1 => "骸骨弓箭手",
                        _ => "咒術石像"
                    };
                    list.Add(new HgwTowerEnemy
                    {
                        Name = $"👾 {mName} {i}號",
                        ClassName = classes[_rng.Next(classes.Length)],
                        MaxHp = 1000 + floor * 250,
                        CurrentHp = 1000 + floor * 250,
                        Attack = 120 + floor * 18,
                        Defense = 35 + floor * 6
                    });
                }
            }

            return list;
        }

        private void DrawCardsForTurn(HgwTowerRun run, HgwTowerEncounter encounter)
        {
            encounter.SelectedCards.Clear();

            // 収集所有存活從者的卡片構成
            var cardPool = new List<HgwCardPlay>();
            var aliveTeam = run.Team.Where(s => s.IsAlive).ToList();

            if (aliveTeam.Count == 0) return;

            for (int sIdx = 0; sIdx < run.Team.Count; sIdx++)
            {
                var s = run.Team[sIdx];
                if (!s.IsAlive) continue;

                for (int cardIdx = 0; cardIdx < s.Cards.Count; cardIdx++)
                {
                    cardPool.Add(new HgwCardPlay
                    {
                        ServantIndex = sIdx,
                        ServantName = s.Name,
                        CardType = s.Cards[cardIdx].ToLower(),
                        CardIndex = cardIdx
                    });
                }
            }

            // FGO隨機抽出5張牌
            var randomized = cardPool.OrderBy(_ => _rng.Next()).Take(5).ToList();

            // 暴擊星分配 (每發暴擊星+10機率)
            int stars = encounter.CritStars;
            encounter.CritStars = 0; // 當回合重置

            for (int i = 0; i < stars; i++)
            {
                if (randomized.Count == 0) break;
                var luckyCard = randomized[_rng.Next(randomized.Count)];
                luckyCard.CritChance = Math.Min(100, luckyCard.CritChance + 10);
            }

            encounter.HandCards = randomized;
            Console.WriteLine($"[HolyGrailTower] DrawCards floor={run.CurrentFloor}, hand={string.Join(",", randomized.Select(x => x.CardType))}");
        }

        // ═══════════════════════════════════════════════════════════
        //  事件渲染
        // ═══════════════════════════════════════════════════════════

        private (Embed embed, ComponentBuilder component) RenderCurrentEncounter(HgwTowerRun run)
        {
            var enc = run.CurrentEncounter;

            switch (enc.Type)
            {
                case EncounterType.NormalBattle:
                case EncounterType.EliteBattle:
                case EncounterType.BossBattle:
                    return RenderBattleEncounter(run);

                case EncounterType.Event:
                    return RenderAdvanceEncounter(run);

                case EncounterType.Shop:
                    return RenderShopEncounter(run);

                case EncounterType.RestSite:
                    return RenderRestEncounter(run);

                case EncounterType.Treasure:
                    return RenderTreasureEncounter(run);
            }

            return (CommonHelper.BuildErrorResponse("遭遇無效屬性").Item2, new ComponentBuilder());
        }

        private (Embed embed, ComponentBuilder component) RenderAdvanceEncounter(HgwTowerRun run)
        {
            var enc = run.CurrentEncounter;
            var embedBuilder = new EmbedBuilder()
                .WithTitle($"🏆 第 {run.CurrentFloor} 層已淨化")
                .WithDescription("這一層的魔力殘渣已被清除。整備好隊伍後，繼續往更深處邁進。")
                .WithColor(Color.Gold)
                .WithCurrentTimestamp();

            if (enc.BattleLog.Count > 0)
            {
                embedBuilder.AddField("📜 戰鬥結算", string.Join("\n", enc.BattleLog.TakeLast(10)));
            }

            embedBuilder.AddField("🎒 當前資源", $"金幣：**{run.Gold}**\n存活英靈：**{run.Team.Count(x => x.IsAlive)}/{run.Team.Count}**", inline: true);

            var cb = new ComponentBuilder()
                .WithButton("⏩ 前往下一層", "hgwt_next", ButtonStyle.Success);

            return (embedBuilder.Build(), cb);
        }

        private (Embed embed, ComponentBuilder component) RenderBattleEncounter(HgwTowerRun run)
        {
            var enc = run.CurrentEncounter;
            var embedBuilder = new EmbedBuilder()
                .WithTitle($"⚔️ 聖杯遭遇戰 — 第 {run.CurrentFloor} 層 ({enc.TurnCount} 回合(Turn))")
                .WithColor(enc.Type == EncounterType.BossBattle ? Color.Red : enc.Type == EncounterType.EliteBattle ? Color.Orange : Color.Blue)
                .WithCurrentTimestamp();

            // 1. 怪物陣容
            var enemyText = string.Join("\n", enc.Enemies.Select(e =>
                $"{(e.IsAlive ? "👾" : "💀")} {e.Name} (【{e.ClassName.ToUpper()}】職階) HP: **{e.CurrentHp}/{e.MaxHp}** (ATK {e.Attack})"));
            embedBuilder.AddField("😈 魔物法陣營", enemyText);

            // 2. 我方隊伍 HP / NP 
            var squadText = string.Join("\n", run.Team.Select((s, i) =>
                $"{(s.IsAlive ? "👤" : "💀")} {GetClassEmoji(s.ClassName)} **{s.Name}** HP: **{s.CurrentHp}/{s.MaxHp}** | NP Charge: **{s.NpCharge}%**"));
            embedBuilder.AddField("🛡️ 我方迦勒底英靈", squadText);

            var skillText = string.Join("\n", run.Team.Select((s, i) =>
            {
                if (s.Skills == null || s.Skills.Count == 0)
                    return $"{GetClassEmoji(s.ClassName)} **{s.Name}**：無資料";

                var desc = string.Join(" | ", s.Skills.Select((skill, idx) =>
                    $"{idx + 1}.{skill.Name}{(s.UsedSkillIndexes.Contains(idx) ? "✅" : string.Empty)}"));

                return $"{GetClassEmoji(s.ClassName)} **{s.Name}**：{desc}";
            }));
            embedBuilder.AddField("🧠 主動技能", skillText);

            // 3. 事件日誌 / 戰鬥歷程
            if (enc.BattleLog.Count > 0)
            {
                string log = string.Join("\n", enc.BattleLog.TakeLast(6));
                embedBuilder.AddField("📜 戰火回放", log);
            }

            // 4. 手卡配置與選中順序描述
            string handCardsDesc = "━━━━━━━━ 手牌 ━━━━━━━━\n";
            for (int i = 0; i < enc.HandCards.Count; i++)
            {
                var card = enc.HandCards[i];
                string colorIcon = card.CardType == "buster" ? "🔴 B" : card.CardType == "arts" ? "🔵 A" : "🟢 Q";
                handCardsDesc += $"【卡{i + 1}】 `{colorIcon}` {card.ServantName} (⚡ {card.CritChance}% 暴擊率)\n";
            }

            if (enc.SelectedCards.Count > 0)
            {
                string chosenChainStr = string.Join(" ➜ ", enc.SelectedCards.Select(c =>
                {
                    string col = c.CardType == "buster" ? "🔴 B" : c.CardType == "arts" ? "🔵 A" : c.CardType == "quick" ? "🟢 Q" : "💥 寶具";
                    return $"`[{col}]` {c.ServantName}";
                }));
                embedBuilder.AddField("🎴 當前出手預定連擊卡", chosenChainStr);
            }
            else
            {
                embedBuilder.AddField("🎴 當前出手預定連擊卡", "*(尚未選擇任何攻擊，請在下方挑選)*");
            }

            embedBuilder.WithDescription(handCardsDesc + $"\n🌟 上回合保留暴擊星額度：**{enc.CritStars} 顆**\n*(每回合只能發出一輪指令, 選擇3張釋放)*");

            // 5. 連擊操作按鈕組
            var cb = new ComponentBuilder();

            // Row 0: 5張手牌按鈕
            for (int i = 0; i < enc.HandCards.Count; i++)
            {
                var card = enc.HandCards[i];
                bool alreadySelected = enc.SelectedCards.Any(c => c.CardIndex == i);
                string colLabel = card.CardType == "buster" ? "🔴 B" : card.CardType == "arts" ? "🔵 A" : "🟢 Q";
                string finalLabel = $"{colLabel} [{card.ServantName[0]}] {card.CritChance}%";
                cb.WithButton(finalLabel, $"hgwt_card_{i}", ButtonStyle.Secondary, disabled: alreadySelected || enc.SelectedCards.Count >= 3, row: 0);
            }

            // Row 1: 滿充寶具按鈕 
            int npButtonCount = 0;
            for (int i = 0; i < run.Team.Count; i++)
            {
                var s = run.Team[i];
                if (s.IsAlive && s.NpCharge >= 100)
                {
                    bool isNpSelected = enc.SelectedCards.Any(c => c.ServantIndex == i && c.CardType == "np");
                    cb.WithButton($"💥寶具「{s.NpName}」({s.NpCard.ToUpper()})", $"hgwt_np_{i}", ButtonStyle.Danger, disabled: isNpSelected || enc.SelectedCards.Count >= 3, row: 1);
                    npButtonCount++;
                }
            }

            if (npButtonCount == 0)
            {
                cb.WithButton("(尚未充能寶具)", "hgwt_no_np", ButtonStyle.Secondary, disabled: true, row: 1);
            }

            // Row 2: 控制組
            cb.WithButton("🔄 取消與重置點牌", "hgwt_undo", ButtonStyle.Primary, disabled: enc.SelectedCards.Count == 0, row: 2);
            cb.WithButton("🔥 確認連鎖攻擊！", "hgwt_confirm", ButtonStyle.Success, disabled: enc.SelectedCards.Count < 3, row: 2);
            cb.WithButton("🏳️ 撤退探索", "hgwt_surrender", ButtonStyle.Danger, row: 2);

            int skillButtonCount = 0;
            for (int servantIndex = 0; servantIndex < run.Team.Count; servantIndex++)
            {
                var servant = run.Team[servantIndex];
                if (!servant.IsAlive || servant.Skills == null)
                    continue;

                for (int skillIndex = 0; skillIndex < servant.Skills.Count && skillIndex < 3; skillIndex++)
                {
                    var skill = servant.Skills[skillIndex];
                    int row = 3 + (skillButtonCount / 5);
                    if (row > 4)
                        break;

                    var label = $"{servant.Name[0]}-{TrimButtonLabel(skill.Name, 12)}";
                    cb.WithButton(
                        label,
                        $"hgwt_skill_{servantIndex}_{skillIndex}",
                        ButtonStyle.Primary,
                        disabled: servant.UsedSkillIndexes.Contains(skillIndex),
                        row: row);

                    skillButtonCount++;
                }
            }

            return (embedBuilder.Build(), cb);
        }

        private (Embed embed, ComponentBuilder component) RenderShopEncounter(HgwTowerRun run)
        {
            var embed = new EmbedBuilder()
                .WithTitle($"🏪 特里托聖杯交易所 — 第 {run.CurrentFloor} 層")
                .WithDescription($"**御主 {run.PlayerName}**，冒險旅途中遇到遊歷商人。\n\n" +
                    $"💰 金幣餘額：**{run.Gold} Gold**\n請點擊商品按鈕購買：")
                .AddField("🛍️ 迦勒底急救套包 (費用：40 金幣)", "恢復冒險陣容中所有存活英靈 40% 的生命力。")
                .AddField("🛍️ 起死回生黃金令咒 (費用：80 金幣)", "復活並治療冒險英靈隊伍中已戰死的全部隊員(基礎25%HP)。")
                .AddField("🛍️ 特殊靈基限界突破 (費用：70 金幣)", "本趟旅途內所有人攻擊能力永久 +15%！")
                .WithColor(Color.Purple)
                .WithCurrentTimestamp()
                .Build();

            var cb = new ComponentBuilder()
                .WithButton("💚 全員治療 (40金)", "hgwt_shop_heal", ButtonStyle.Success, disabled: run.Gold < 40, row: 0)
                .WithButton("💖 英靈復活 (80金)", "hgwt_shop_revive", ButtonStyle.Success, disabled: run.Gold < 80, row: 0)
                .WithButton("🗡️ 限界突破 (70金)", "hgwt_shop_buff", ButtonStyle.Success, disabled: run.Gold < 70, row: 0)
                .WithButton("🚪 離開交易所", "hgwt_shop_leave", ButtonStyle.Secondary, row: 1);

            return (embed, cb);
        }

        private (Embed embed, ComponentBuilder component) RenderRestEncounter(HgwTowerRun run)
        {
            var embed = new EmbedBuilder()
                .WithTitle($"🔥 營火防線基地 — 第 {run.CurrentFloor} 層")
                .WithDescription("這是短暫、安寧的宿營地。寒風呼嘯，但營火能恢復你乾涸的精力。\n\n" +
                    "請在下方做出選擇：\n" +
                    "🛌 **營帳憩息**：迦勒底全員修整，所有隊友恢復 50% HP。\n" +
                    "💪 **卡牌訓練**：本次旅途中所有人起始 NP 精力直接額外 +30%！")
                .WithColor(Color.Green)
                .WithCurrentTimestamp()
                .Build();

            var cb = new ComponentBuilder()
                .WithButton("🛌 憩息治療全員", "hgwt_rest_heal", ButtonStyle.Success, row: 0)
                .WithButton("💪 行前修行全員充能", "hgwt_rest_np", ButtonStyle.Primary, row: 0);

            return (embed, cb);
        }

        private (Embed embed, ComponentBuilder component) RenderTreasureEncounter(HgwTowerRun run)
        {
            var embed = new EmbedBuilder()
                .WithTitle($"🎁 迷之黃金寶藏箱 — 第 {run.CurrentFloor} 層")
                .WithDescription("走過廢棄迴廊，驚喜地發現了一尊刻印著古魔術師家族微章的珍貴聖杯寶箱！\n\n點擊下方開啟，可能有意想不到的重要補給。")
                .WithColor(Color.Gold)
                .WithCurrentTimestamp()
                .Build();

            var cb = new ComponentBuilder()
                .WithButton("🔑 注入魔力開啟寶箱", "hgwt_treasure_open", ButtonStyle.Success);

            return (embed, cb);
        }

        // ═══════════════════════════════════════════════════════════
        //  交互按鈕分流 HandleButtonInteractionAsync
        // ═══════════════════════════════════════════════════════════

        public async Task<(Embed embed, ComponentBuilder component)> HandleButtonInteractionAsync(ulong userId, ulong channelId, string customId)
        {
            if (!_runs.TryGetValue(channelId, out var run))
                return (CommonHelper.BuildErrorResponse("找不到進行中的聖杯冒險，可能已經結算或超時關閉。").Item2, new ComponentBuilder());

            if (run.PlayerId != userId)
                return (CommonHelper.BuildErrorResponse("提示：你不是發起此聖杯挑戰的御主！").Item2, new ComponentBuilder());

            var parts = customId.Split('_');
            var action = parts[1];
            Console.WriteLine($"[HolyGrailTower] HandleButton channel={channelId}, user={userId}, action={customId}, floor={run.CurrentFloor}");

            try
            {
                switch (action)
                {
                    // 1. 組建陣容階段
                    case "select":
                        int colNo = int.Parse(parts[2]);
                        var player = LoadPlayer(userId);
                        var target = player.OwnedServants.FirstOrDefault(x => x.CollectionNo == colNo);

                        if (target != null)
                        {
                            if (run.Team.Any(s => s.CollectionNo == colNo))
                            {
                                run.Team.RemoveAll(x => x.CollectionNo == colNo);
                            }
                            else if (run.Team.Count < 3)
                            {
                                run.Team.Add(HgwTowerServantInstance.FromServant(target));
                            }
                        }
                        return BuildTeamSelectionScreen(run, player);

                    case "start":
                        if (run.Team.Count == 0)
                        {
                            return (CommonHelper.BuildErrorResponse("必須至少配備一位從者出發！").Item2, new ComponentBuilder());
                        }
                        // 初始第一層遭遇
                        GenerateEncounter(run);
                        return RenderCurrentEncounter(run);

                    // 2. FGO 戰鬥卡牌選牌
                    case "card":
                        int handIdx = int.Parse(parts[2]);
                        if (run.CurrentEncounter.SelectedCards.Count < 3)
                        {
                            var hCard = run.CurrentEncounter.HandCards[handIdx];
                            // 判斷是否已被選
                            if (!run.CurrentEncounter.SelectedCards.Any(x => x.CardIndex == handIdx))
                            {
                                run.CurrentEncounter.SelectedCards.Add(new HgwCardPlay
                                {
                                    ServantIndex = hCard.ServantIndex,
                                    ServantName = hCard.ServantName,
                                    CardType = hCard.CardType,
                                    CardIndex = handIdx,
                                    CritChance = hCard.CritChance
                                });
                            }
                        }
                        return RenderCurrentEncounter(run);

                    case "np":
                        int sIdx = int.Parse(parts[2]);
                        if (run.CurrentEncounter.SelectedCards.Count < 3)
                        {
                            var servant = run.Team[sIdx];
                            if (servant.IsAlive && servant.NpCharge >= 100)
                            {
                                if (!run.CurrentEncounter.SelectedCards.Any(x => x.ServantIndex == sIdx && x.CardType == "np"))
                                {
                                    run.CurrentEncounter.SelectedCards.Add(new HgwCardPlay
                                    {
                                        ServantIndex = sIdx,
                                        ServantName = servant.Name,
                                        CardType = "np",
                                        CardIndex = -1,
                                        CritChance = 0
                                    });
                                }
                            }
                        }
                        return RenderCurrentEncounter(run);

                    case "skill":
                        int skillServantIndex = int.Parse(parts[2]);
                        int skillIndexToUse = int.Parse(parts[3]);
                        UseServantSkill(run, skillServantIndex, skillIndexToUse);
                        return RenderCurrentEncounter(run);

                    case "undo":
                        run.CurrentEncounter.SelectedCards.Clear();
                        return RenderCurrentEncounter(run);

                    case "no":
                        return RenderCurrentEncounter(run);

                    case "confirm":
                        if (run.CurrentEncounter.SelectedCards.Count == 3)
                        {
                            await ProcessBattleTurnAsync(run);
                            if (run.IsFinished)
                            {
                                _runs.Remove(channelId);
                                return (BuildRunEndEmbed(run));
                            }
                            return RenderCurrentEncounter(run);
                        }
                        return RenderCurrentEncounter(run);

                    case "surrender":
                        _runs.Remove(channelId);
                        var surrEmbed = new EmbedBuilder()
                            .WithTitle("🏳️ 御主倉惶撤離")
                            .WithDescription($"**御主 {run.PlayerName}** 指揮撤退，探索結束！\n本次止步於第 **{run.CurrentFloor}** 層。")
                            .WithColor(Color.DarkBlue)
                            .Build();
                        return (surrEmbed, new ComponentBuilder());

                    // 3. 交易所操作
                    case "shop":
                        string shopChoice = parts[2];
                        if (shopChoice == "heal" && run.Gold >= 40)
                        {
                            run.Gold -= 40;
                            foreach (var s in run.Team)
                            {
                                if (s.IsAlive) s.CurrentHp = Math.Min(s.MaxHp, s.CurrentHp + (int)(s.MaxHp * 0.4));
                            }
                            run.EventLog.Add("💸 購買全員治療：全隊存活英靈治癒 HP +40%！");
                        }
                        else if (shopChoice == "revive" && run.Gold >= 80)
                        {
                            run.Gold -= 80;
                            foreach (var s in run.Team)
                            {
                                if (!s.IsAlive)
                                {
                                    s.CurrentHp = s.MaxHp / 4;
                                }
                            }
                            run.EventLog.Add("💸 復活指令：戰死英靈攜 25% 的能量光環重返戰場！");
                        }
                        else if (shopChoice == "buff" && run.Gold >= 70)
                        {
                            run.Gold -= 70;
                            foreach (var s in run.Team)
                            {
                                s.BonusAtk += (int)(s.Attack * 0.15);
                            }
                            run.EventLog.Add("💸 魔力暴湧：全員英靈基礎 ATK 永久提昇 15%！");
                        }
                        else if (shopChoice == "leave")
                        {
                            // 進入下一層 
                            run.CurrentFloor++;
                            GenerateEncounter(run);
                            return RenderCurrentEncounter(run);
                        }
                        return RenderShopEncounter(run);

                    // 4. 憩息地操作
                    case "rest":
                        string restChoice = parts[2];
                        if (restChoice == "heal")
                        {
                            foreach (var s in run.Team)
                            {
                                if (s.IsAlive) s.CurrentHp = Math.Min(s.MaxHp, s.CurrentHp + s.MaxHp / 2);
                            }
                            run.EventLog.Add("🛌 全體治療：在營地度宿安恬清晨，全員恢復 50% 的生命力。");
                        }
                        else if (restChoice == "np")
                        {
                            foreach (var s in run.Team)
                            {
                                if (s.IsAlive) s.AddNpCharge(30);
                            }
                            run.EventLog.Add("💪 行前大突破：全體英靈直接灌注 +30% 的寶具充能能量！");
                        }
                        run.CurrentFloor++;
                        GenerateEncounter(run);
                        return RenderCurrentEncounter(run);

                    // 5. 寶藏
                    case "treasure":
                        string tChoice = parts[2];
                        if (tChoice == "open")
                        {
                            int gGain = _rng.Next(40, 101);
                            run.Gold += gGain;

                            // 20% 爆出召喚券
                            string bonus = "";
                            if (_rng.Next(100) < 30)
                            {
                                var pl = LoadPlayer(userId);
                                pl.SummonTickets++;
                                SavePlayer(pl);
                                bonus = "\n🎁 **幸運！開卡寶箱發現契约召喚券 ×1** (已匯入帳戶)！";
                            }

                            var embedBuilder = new EmbedBuilder()
                                .WithTitle("💎 寶箱已被解鎖")
                                .WithDescription($"寶箱爆射出耀眼光膜！\n\n獲得：💰 **+{gGain} 金幣**！{bonus}")
                                .WithColor(Color.Gold)
                                .WithCurrentTimestamp()
                                .Build();

                            var cbNext = new ComponentBuilder()
                                .WithButton("⏩ 征服前進下一層", "hgwt_next", ButtonStyle.Success);

                            return (embedBuilder, cbNext);
                        }
                        break;

                    case "next":
                        run.CurrentFloor++;
                        GenerateEncounter(run);
                        return RenderCurrentEncounter(run);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HolyGrailTower] 按鈕互動重大錯誤: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return (CommonHelper.BuildErrorResponse($"執行行動遭遇內部程式碼異常：{ex.Message}").Item2, new ComponentBuilder());
            }

            return RenderCurrentEncounter(run);
        }

        // ═══════════════════════════════════════════════════════════
        //  技能與戰鬥前處理
        // ═══════════════════════════════════════════════════════════

        private void UseServantSkill(HgwTowerRun run, int servantIndex, int skillIndex)
        {
            if (servantIndex < 0 || servantIndex >= run.Team.Count)
                return;

            var servant = run.Team[servantIndex];
            if (!servant.IsAlive || servant.Skills == null || skillIndex < 0 || skillIndex >= servant.Skills.Count)
                return;

            if (servant.UsedSkillIndexes.Contains(skillIndex))
                return;

            var skill = servant.Skills[skillIndex];
            servant.UsedSkillIndexes.Add(skillIndex);

            var effects = new List<string>();
            var encounter = run.CurrentEncounter;

            if (skill.FunctionTypes.Any(x => x.Contains("gainNp", StringComparison.OrdinalIgnoreCase) || x.Contains("hastenNpturn", StringComparison.OrdinalIgnoreCase)))
            {
                servant.AddNpCharge(20);
                effects.Add("NP +20%");
            }

            if (skill.FunctionTypes.Any(x => x.Contains("heal", StringComparison.OrdinalIgnoreCase)) || skill.Detail?.Contains("回復") == true)
            {
                int healAmount = Math.Max(100, servant.MaxHp / 4);
                servant.CurrentHp = Math.Min(servant.MaxHp, servant.CurrentHp + healAmount);
                effects.Add($"HP +{healAmount}");
            }

            if (skill.FunctionTypes.Any(x => x.Contains("gainStar", StringComparison.OrdinalIgnoreCase)) || skill.Detail?.Contains("暴擊星") == true)
            {
                encounter.CritStars += 10;
                effects.Add("暴擊星 +10");
            }

            if (skill.FunctionTypes.Any(x => x.Contains("addState", StringComparison.OrdinalIgnoreCase)) || skill.Detail?.Contains("提升") == true)
            {
                if (skill.Detail?.Contains("攻擊") == true || skill.BuffTypes.Any(x => x.Contains("atk", StringComparison.OrdinalIgnoreCase)))
                {
                    int atkUp = Math.Max(20, servant.Attack / 5);
                    servant.BonusAtk += atkUp;
                    effects.Add($"ATK +{atkUp}");
                }

                if (skill.Detail?.Contains("防禦") == true || skill.BuffTypes.Any(x => x.Contains("def", StringComparison.OrdinalIgnoreCase)))
                {
                    int defUp = Math.Max(10, servant.Defense / 5);
                    servant.BonusDef += defUp;
                    effects.Add($"DEF +{defUp}");
                }
            }

            if (effects.Count == 0)
            {
                servant.AddNpCharge(10);
                effects.Add("NP +10%");
            }

            encounter.BattleLog.Add($"🧠 **{servant.Name}** 發動技能 **{skill.Name}**！ ({string.Join(" / ", effects)})");
            if (!string.IsNullOrWhiteSpace(skill.Detail))
            {
                encounter.BattleLog.Add($"　└ {skill.Detail}");
            }

            QueueVisual(run.ChannelId, $"{servant.Name} - {skill.Name}", skill.Detail ?? string.Join(" / ", effects), skill.IconUrl ?? servant.FullImageUrl ?? servant.FaceUrl);
        }

        // ═══════════════════════════════════════════════════════════
        //  FGO 回合核心運算 (ProcessBattleTurnAsync)
        // ═══════════════════════════════════════════════════════════

        private async Task ProcessBattleTurnAsync(HgwTowerRun run)
        {
            var enc = run.CurrentEncounter;
            var previousLogs = enc.BattleLog.TakeLast(4).ToList();
            enc.BattleLog.Clear();
            enc.BattleLog.AddRange(previousLogs);
            Console.WriteLine($"[HolyGrailTower] ProcessBattleTurn floor={run.CurrentFloor}, selected={string.Join(",", enc.SelectedCards.Select(x => x.CardType))}");

            // 1. 各項卡牌連擊加成
            var sCards = enc.SelectedCards;

            // ① First Card Bonus 判定
            bool isFirstBuster = sCards[0].CardType == "buster" || (sCards[0].CardType == "np" && GetServantNpCard(run, sCards[0].ServantIndex) == "buster");
            bool isFirstArts = sCards[0].CardType == "arts" || (sCards[0].CardType == "np" && GetServantNpCard(run, sCards[0].ServantIndex) == "arts");
            bool isFirstQuick = sCards[0].CardType == "quick" || (sCards[0].CardType == "np" && GetServantNpCard(run, sCards[0].ServantIndex) == "quick");

            // ② Color chain 判定 
            string c1 = sCards[0].CardType == "np" ? GetServantNpCard(run, sCards[0].ServantIndex) : sCards[0].CardType;
            string c2 = sCards[1].CardType == "np" ? GetServantNpCard(run, sCards[1].ServantIndex) : sCards[1].CardType;
            string c3 = sCards[2].CardType == "np" ? GetServantNpCard(run, sCards[2].ServantIndex) : sCards[2].CardType;

            bool isBusterChain = c1 == "buster" && c2 == "buster" && c3 == "buster";
            bool isArtsChain = c1 == "arts" && c2 == "arts" && c3 == "arts";
            bool isQuickChain = c1 == "quick" && c2 == "quick" && c3 == "quick";

            // ③ Brave Chain (同一名從者出手3張)
            bool isBraveChain = sCards.All(x => x.ServantIndex == sCards[0].ServantIndex);

            if (isBusterChain) enc.BattleLog.Add("🔥 **BUSTER CHAIN 連攜！全體攻擊力激增！**");
            if (isArtsChain)
            {
                enc.BattleLog.Add("💧 **ARTS CHAIN 充能連攜！發動者全體獲得 +20% 寶具充能！**");
                foreach (var s in run.Team)
                {
                    if (s.IsAlive) s.AddNpCharge(20);
                }
            }
            if (isQuickChain)
            {
                enc.BattleLog.Add("⚡ **QUICK CHAIN 暴風爆星！直接生成 +10 顆寶具暴擊星！**");
                enc.CritStars += 10;
            }

            int turnStarsGenerated = 0;

            // 2. 打出卡牌攻擊序列
            for (int step = 0; cardStep(step, run, enc); step++)
            {
                var card = sCards[step];
                var s = run.Team[card.ServantIndex];
                if (!s.IsAlive) continue;

                var enemy = enc.GetCurrentEnemy();
                if (enemy == null) break; // 怪物全死

                // 爆擊機率判定
                bool isCrit = _rng.Next(100) < card.CritChance;

                // FGO 經典計算
                double cardBaseMultiplier = card.CardType switch
                {
                    "buster" => 1.5,
                    "arts" => 1.0,
                    "quick" => 0.8,
                    _ => 1.0
                };

                double positionMultiplier = step switch
                {
                    0 => 1.0,
                    1 => 1.2,
                    2 => 1.4,
                    _ => 1.0
                };

                if (isBusterChain && card.CardType == "buster") positionMultiplier += 0.2;

                double firstCardHpBonus = isFirstBuster ? 0.5 : 0.0;
                double classMultiplier = ClassAdvantage.GetMultiplier(s.ClassName, enemy.ClassName);
                double critMultiplier = isCrit ? 2.0 : 1.0;

                int damage = 0;

                if (card.CardType == "np")
                {
                    // 釋放寶具 (NP)
                    double npCardColorMulti = s.NpCard switch
                    {
                        "buster" => 1.5,
                        "arts" => 1.0,
                        "quick" => 0.8,
                        _ => 1.0
                    };

                    double npMultiplier = s.NpDmgMultiplier / 100.0;
                    double rawNpBase = (s.Attack + s.BonusAtk) * npMultiplier * 0.23;
                    damage = (int)(rawNpBase * npCardColorMulti * classMultiplier);
                    damage = Math.Max(120, damage);

                    // 判定全體(AOE)抑或單體阻擊 
                    bool isAoe = s.NpTargetType.Contains("All", StringComparison.OrdinalIgnoreCase);

                    s.UseNp();
                    QueueVisual(run.ChannelId, $"{s.Name} 寶具解放", $"『{s.NpName}』", s.FullImageUrl ?? s.FaceUrl);
                    QueueVisual(run.ChannelId, s.NpName, s.NpEffect ?? "寶具發動", s.NpIconUrl ?? s.FullImageUrl ?? s.FaceUrl);

                    if (isAoe)
                    {
                        enc.BattleLog.Add($"💫✨ **{s.Name}** 吟唱寶具：『**{s.NpName}**』 (AOE) 攻擊！");
                        foreach (var targetEnemy in enc.Enemies.Where(e => e.IsAlive).ToList())
                        {
                            targetEnemy.CurrentHp = Math.Max(0, targetEnemy.CurrentHp - damage);
                            enc.BattleLog.Add($"  ➔ 對魔物 **{targetEnemy.Name}** 造成 **{damage}** 點大範圍創傷！");
                        }
                    }
                    else
                    {
                        enemy.CurrentHp = Math.Max(0, enemy.CurrentHp - damage);
                        enc.BattleLog.Add($"💫✨ **{s.Name}** 吟唱解放寶具：『**{s.NpName}**』！\n  ➔ 對魔物 **{enemy.Name}** 造成 **{damage}** 點毀滅打擊！");
                    }
                }
                else
                {
                    // 一般指令卡普攻
                    double rawBase = (s.Attack + s.BonusAtk) * 0.23;
                    double damageRate = (cardBaseMultiplier * positionMultiplier) + firstCardHpBonus;
                    damage = (int)(rawBase * damageRate * critMultiplier * classMultiplier);
                    damage = Math.Max(10, damage);

                    enemy.CurrentHp = Math.Max(0, enemy.CurrentHp - damage);

                    string critStr = isCrit ? " 💥 **爆擊(CRITICAL)！**" : "";
                    string favor = classMultiplier > 1.0 ? " (職階相剋優勢)" : classMultiplier < 1.0 ? " (職階不利)" : "";

                    string typeLabel = card.CardType.ToUpper() switch
                    {
                        "BUSTER" => "🔴 Buster 紅卡",
                        "ARTS" => "🔵 Arts 藍卡",
                        "QUICK" => "🟢 Quick 綠卡",
                        _ => card.CardType
                    };

                    enc.BattleLog.Add($"⚔️ **{s.Name}** 的 {typeLabel} 轟擊 **{enemy.Name}**，造成 **{damage}** 點傷害！{critStr}{favor}");

                    // ① 產生 NP 充能
                    int baseGain = card.CardType switch
                    {
                        "arts" => 16,
                        "quick" => 8,
                        _ => 0
                    };
                    if (isFirstArts) baseGain += 6;
                    if (isArtsChain && card.CardType == "arts") baseGain = (int)(baseGain * 1.5);
                    if (isCrit) baseGain *= 2;

                    if (baseGain > 0)
                    {
                        s.AddNpCharge(baseGain);
                        enc.BattleLog.Add($"  ➔ {s.Name} NP Charge **+{baseGain}%** ({s.NpCharge}/100%)");
                    }

                    // ② 產生暴擊星
                    int baseStars = card.CardType switch
                    {
                        "quick" => 4,
                        "arts" => 1,
                        _ => 0
                    };
                    if (isFirstQuick) baseStars += 2;
                    if (isQuickChain) baseStars *= 2;

                    if (baseStars > 0)
                    {
                        turnStarsGenerated += baseStars;
                    }
                }
            }

            // Brave Chain Extra Attack 經典發作
            if (isBraveChain && run.Team.Any(s => s.IsAlive) && enc.GetCurrentEnemy() != null)
            {
                var braveServant = run.Team[sCards[0].ServantIndex];
                var targetE = enc.GetCurrentEnemy();
                if (braveServant.IsAlive && targetE != null)
                {
                    int extraDmg = (int)((braveServant.Attack + braveServant.BonusAtk) * 0.25);
                    targetE.CurrentHp = Math.Max(0, targetE.CurrentHp - extraDmg);
                    braveServant.AddNpCharge(10);
                    turnStarsGenerated += 3;

                    enc.BattleLog.Add($"🌈 **{braveServant.Name}** 誘發 **EXTRA ATTACK** 追加超段打擊！\n  ➔ 對魔物 {targetE.Name} 追加 **{extraDmg}** 點 flat 痛擊！NP +10%");
                }
            }

            if (turnStarsGenerated > 0)
            {
                enc.CritStars = Math.Min(50, enc.CritStars + turnStarsGenerated);
                enc.BattleLog.Add($"🌟 本回合指令生成了 **{turnStarsGenerated} 顆** 暴擊星！");
            }

            // 3. 判斷戰鬥是否由玩家完封
            if (enc.AllEnemiesDead())
            {
                enc.BattleLog.Add("\n🏆 **迦勒底英靈連攜陣成功鎮壓魔物法印！聖杯獲得淨化！**");

                // 獎勵結算
                int goldDrop = _rng.Next(25, 65) + run.CurrentFloor * 5;
                run.Gold += goldDrop;

                // 隨行永久數據增加
                foreach (var s in run.Team)
                {
                    s.AddNpCharge(20); // 保留充能
                }

                enc.BattleLog.Add($"💰 金幣額外掉落：**+{goldDrop} Gold**");

                var pl = LoadPlayer(run.PlayerId);
                pl.TotalKills += enc.Enemies.Count;
                if (run.CurrentFloor > pl.HighestFloor) pl.HighestFloor = run.CurrentFloor;
                SavePlayer(pl);

                // 升入下一層選擇通路 
                run.CurrentEncounter = new HgwTowerEncounter
                {
                    Type = EncounterType.Event // 轉換為事件，顯示前行按鈕
                };
                return;
            }

            // 4. 魔物法攻擊波 (回合反擊) 
            enc.BattleLog.Add("\n━━━━━━ 魔物反擊階段 ━━━━━━");
            foreach (var enemy in enc.Enemies.Where(e => e.IsAlive))
            {
                var aliveParty = run.Team.Where(s => s.IsAlive).ToList();
                if (aliveParty.Count == 0) break;

                var target = aliveParty[_rng.Next(aliveParty.Count)];
                int enemyBaseDamage = Math.Max(10, enemy.Attack - (target.Defense + target.BonusDef) / 2);
                // 波動浮動範圍 
                int finalEnemyDmg = _rng.Next((int)(enemyBaseDamage * 0.8), (int)(enemyBaseDamage * 1.2));
                finalEnemyDmg = Math.Max(10, finalEnemyDmg);

                target.CurrentHp = Math.Max(0, target.CurrentHp - finalEnemyDmg);
                enc.BattleLog.Add($"👿魔物 **{enemy.Name}** 發出反噬，咬痕重創 **{target.Name}**，造成 **{finalEnemyDmg}** 點傷害！");
            }

            // 5. 檢查玩家是否全員陣亡 (戰敗慘劇)
            if (run.Team.All(s => !s.IsAlive))
            {
                run.IsFinished = true;
                return;
            }

            // 6. 清理手牌並派發新指令手卡
            enc.TurnCount++;
            DrawCardsForTurn(run, enc);
        }

        private static bool cardStep(int currentStep, HgwTowerRun run, HgwTowerEncounter enc)
        {
            return currentStep < enc.SelectedCards.Count && run.Team.Any(s => s.IsAlive) && enc.Enemies.Any(e => e.IsAlive);
        }

        private string GetServantNpCard(HgwTowerRun run, int sIdx)
        {
            if (sIdx >= 0 && sIdx < run.Team.Count)
                return run.Team[sIdx].NpCard ?? "buster";
            return "buster";
        }

        // ═══════════════════════════════════════════════════════════
        //  通關/戰敗結算
        // ═══════════════════════════════════════════════════════════

        private (Embed embed, ComponentBuilder component) BuildRunEndEmbed(HgwTowerRun run)
        {
            var pl = LoadPlayer(run.PlayerId);
            pl.TotalRuns++;
            if (run.CurrentFloor > pl.HighestFloor) pl.HighestFloor = run.CurrentFloor;

            // 獎勵：每突破 2 層可獲得 1 張召喚券、1 顆聖晶石
            int ticketsAwarded = run.CurrentFloor / 2;
            int quartzAwarded = run.CurrentFloor / 5;

            pl.SummonTickets += ticketsAwarded;
            pl.SaintQuartz += quartzAwarded;

            SavePlayer(pl);

            var embed = new EmbedBuilder()
                .WithTitle("💀 探索全隊覆滅 — 迦勒底靈基折斷")
                .WithDescription($"**御主 {run.PlayerName}** 的英靈隨僕在第 **{run.CurrentFloor} 層** 全員慘遭除滅！\n" +
                    "靈子投影強行斷開，你被彈回了控制中樞。\n\n" +
                    "📈 **本趟收穫核算：**\n" +
                    $"🎟️ 召喚券補給：**+{ticketsAwarded} 張**\n" +
                    $"💎 聖晶石資助：**+{quartzAwarded} 塊**\n" +
                    $"💰 金幣儲備損毀：-{run.Gold} Gold")
                .WithColor(Color.Red)
                .WithFooter($"最高層突破：第 {pl.HighestFloor} 層 | 迦勒底為你感到驕傲！")
                .WithCurrentTimestamp()
                .Build();

            return (embed, new ComponentBuilder());
        }

        // ═══════════════════════════════════════════════════════════
        //  API 下載與快取 FGO 核心資料 lazy Cache
        // ═══════════════════════════════════════════════════════════

        private async Task EnsureInitAsync()
        {
            if (_initialized) return;
            await _initLock.WaitAsync();
            try
            {
                if (_initialized) return;
                await LoadServantPoolAsync();
                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task LoadServantPoolAsync()
        {
            try
            {
                var json = await _http.GetStringAsync(BasicServantUrl);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var all = JsonSerializer.Deserialize<List<FgoBasicServant>>(json, opts);
                _servantPool = all?
                    .Where(s => s.CollectionNo > 0 && s.Id < 1_000_000 && !string.IsNullOrEmpty(s.Name))
                    .OrderBy(s => s.CollectionNo)
                    .ToList() ?? new();

                Console.WriteLine($"[HolyGrailTower] 已從 Atlas Academy API 成功快取 {_servantPool.Count} 尊基礎英靈名冊");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HolyGrailTower] 遠端下載英靈名冊失效: {ex.Message}");
            }
        }

        private async Task<TowerServant> FetchAndCacheServantAsync(int collectionNo)
        {
            if (_servantCache.TryGetValue(collectionNo, out var cached))
                return cached;

            try
            {
                var url = string.Format(NiceServantUrl, collectionNo);
                var json = await _http.GetStringAsync(url);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<FgoNiceServant>(json, opts);

                string faceUrl = data?.ExtraAssets?.Faces?.Ascension?
                    .OrderBy(kv => kv.Key)
                    .Select(kv => kv.Value)
                    .FirstOrDefault();

                string fullImageUrl = data?.ExtraAssets?.CharaGraph?.Ascension?
                    .OrderBy(kv => kv.Key)
                    .Select(kv => kv.Value)
                    .LastOrDefault();

                string npName = "未知寶具";
                string npRuby = "";
                string npCard = "buster";
                string npTargetType = "enemy";
                int npDmgMultiplier = 600;
                string npEffect = "造成傷害";
                string npIconUrl = "";
                var skillList = new List<HgwSkillData>();

                if (data?.NoblePhantasms != null && data.NoblePhantasms.Count > 0)
                {
                    var np = data.NoblePhantasms
                        .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                        .OrderByDescending(x => x.Num)
                        .FirstOrDefault();

                    if (np != null)
                    {
                        npName = np.Name;
                        npRuby = np.Ruby;
                        npCard = NormalizeCardType(np.Card);
                        npIconUrl = np.Icon;
                        npEffect = string.IsNullOrWhiteSpace(np.Detail) ? npEffect : np.Detail;

                        if (np.Functions != null && np.Functions.Count > 0)
                        {
                            var f = np.Functions[0];
                            npTargetType = string.IsNullOrWhiteSpace(f.TargetType) ? "enemy" : f.TargetType;
                            if (!string.IsNullOrWhiteSpace(f.FuncType))
                                npEffect = string.IsNullOrWhiteSpace(np.Detail) ? f.FuncType : np.Detail;

                            var npValue = f.Svals?.FirstOrDefault(x => x.Value > 0)?.Value ?? 0;
                            if (npValue > 0)
                                npDmgMultiplier = Math.Max(100, npValue / 10);
                        }
                    }
                }

                if (data?.Skills != null && data.Skills.Count > 0)
                {
                    skillList = data.Skills
                        .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                        .GroupBy(x => x.Num)
                        .Select(g => g.OrderByDescending(x => x.Id).First())
                        .OrderBy(x => x.Num)
                        .Take(3)
                        .Select(skill => new HgwSkillData
                        {
                            Num = skill.Num,
                            Name = skill.Name,
                            Detail = skill.Detail,
                            IconUrl = skill.Icon,
                            FunctionTypes = skill.Functions?
                                .Where(f => !string.IsNullOrWhiteSpace(f.FuncType))
                                .Select(f => f.FuncType)
                                .Distinct()
                                .ToList() ?? new List<string>(),
                            BuffTypes = skill.Functions?
                                .SelectMany(f => f.Buffs ?? new List<FgoBuff>())
                                .Where(b => !string.IsNullOrWhiteSpace(b.Type))
                                .Select(b => b.Type)
                                .Distinct()
                                .ToList() ?? new List<string>()
                        })
                        .ToList();
                }

                // 指令卡配置
                var cardsList = data?.Cards != null && data.Cards.Count == 5 
                    ? data.Cards.Select(NormalizeCardType).ToList() 
                    : new List<string> { "buster", "buster", "arts", "quick", "quick" };

                var basic = _servantPool.FirstOrDefault(x => x.CollectionNo == collectionNo);

                var servant = new TowerServant
                {
                    CollectionNo = data?.CollectionNo > 0 ? data.CollectionNo : collectionNo,
                    Name = data?.Name ?? basic?.Name ?? "未知英靈",
                    ClassName = data?.ClassName ?? basic?.ClassName ?? "saber",
                    Rarity = basic?.Rarity ?? 3,
                    Level = 1,
                    NpLevel = 1,
                    NpName = npName,
                    NpRuby = npRuby,
                    NpCard = npCard,
                    NpTargetType = npTargetType,
                    NpDmgMultiplier = npDmgMultiplier,
                    NpEffect = npEffect,
                    NpIconUrl = npIconUrl,
                    Cards = cardsList,
                    Skills = skillList,
                    FaceUrl = faceUrl ?? basic?.Face,
                    FullImageUrl = fullImageUrl ?? ""
                };

                _servantCache[collectionNo] = servant;
                return servant;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HolyGrailTower] 加載英靈 No.{collectionNo} 細節異常: {ex.Message}");
                // 退火回退機制
                var basic = _servantPool.FirstOrDefault(x => x.CollectionNo == collectionNo);
                return new TowerServant
                {
                    CollectionNo = collectionNo,
                    Name = basic?.Name ?? "未知英靈",
                    ClassName = basic?.ClassName ?? "saber",
                    Rarity = basic?.Rarity ?? 3,
                    Level = 1,
                    NpLevel = 1,
                    NpName = "未知寶具",
                    NpCard = "buster",
                    NpTargetType = "enemy",
                    NpDmgMultiplier = 600,
                    Cards = new List<string> { "buster", "buster", "arts", "quick", "quick" },
                    Skills = new List<HgwSkillData>(),
                    FaceUrl = basic?.Face ?? ""
                };
            }
        }

        private static string NormalizeCardType(string rawCardType)
        {
            return rawCardType?.ToLower() switch
            {
                "1" => "buster",
                "2" => "quick",
                "3" => "arts",
                "buster" => "buster",
                "quick" => "quick",
                "arts" => "arts",
                _ => "quick"
            };
        }

        private static string TrimButtonLabel(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
                return text;

            return text[..maxLength];
        }

        private static string GetClassEmoji(string className) => className?.ToLower() switch
        {
            "saber" => "⚔️",
            "archer" => "🏹",
            "lancer" => "🔱",
            "rider" => "🐴",
            "caster" => "🔮",
            "assassin" => "🗡️",
            "berserker" => "💢",
            "ruler" => "⚖️",
            "avenger" => "🔥",
            "mooncancer" => "🌙",
            "alterego" => "🌀",
            "foreigner" => "🌌",
            "pretender" => "🎭",
            "shielder" => "🛡️",
            _ => "✨"
        };

        private static Color GetRarityColor(int rarity) => rarity switch
        {
            5 => new Color(255, 215, 0),
            4 => new Color(192, 192, 192),
            3 => new Color(205, 127, 50),
            _ => new Color(128, 128, 128)
        };
    }
}
