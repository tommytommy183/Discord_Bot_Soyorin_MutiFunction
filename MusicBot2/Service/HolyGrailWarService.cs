using Discord;
using MusicBot2.Helpers;
using MusicBot2.Models;
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
    /// 聖杯戰爭 RPG 系統
    /// 玩家可召喚從者、進行戰鬥、培養角色
    /// </summary>
    public class HolyGrailWarService
    {
        private readonly HttpClient _http;
        private readonly Random _rng = new();
        private readonly SemaphoreSlim _initLock = new(1, 1);

        private List<FgoBasicServant> _servantPool = new();
        private readonly Dictionary<int, (string npName, string fullImage)> _servantCache = new();
        private bool _initialized = false;

        private readonly Dictionary<ulong, HgwPlayer> _players = new();
        private readonly Dictionary<ulong, HgwBattle> _battles = new();
        private readonly string _dataPath = "Data/HolyGrailWar";

        private const string BasicServantUrl = "https://api.atlasacademy.io/export/TW/basic_servant.json";
        private const string NiceServantUrl = "https://api.atlasacademy.io/nice/TW/servant/{0}?lore=false";

        private int _nextInstanceId = 1;

        public HolyGrailWarService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            Directory.CreateDirectory(_dataPath);
            LoadAllPlayers();
        }

        // ═══════════════════════════════════════════════════════════
        //  玩家系統
        // ═══════════════════════════════════════════════════════════

        public async Task<(Embed embed, ComponentBuilder component)> RegisterPlayerAsync(ulong userId, string userName)
        {
            await EnsureInitAsync();

            if (_players.ContainsKey(userId))
            {
                var existing = _players[userId];
                var embed = new EmbedBuilder()
                    .WithTitle("?? 御主資訊")
                    .WithDescription($"**{userName}** 已經是註冊的御主了！")
                    .AddField("?? 魔力", existing.Mana, inline: true)
                    .AddField("?? 令咒", existing.CommandSeals, inline: true)
                    .AddField("?? 從者數量", existing.Servants.Count, inline: true)
                    .AddField("?? 戰績", $"{existing.Wins}勝 {existing.Losses}敗", inline: true)
                    .WithColor(new Color(0x9B59B6))
                    .WithCurrentTimestamp()
                    .Build();

                return (embed, new ComponentBuilder());
            }

            var player = new HgwPlayer
            {
                UserId = userId,
                UserName = userName,
                Mana = 100,
                CommandSeals = 3
            };

            _players[userId] = player;
            SavePlayer(player);

            var welcomeEmbed = new EmbedBuilder()
                .WithTitle("? 歡迎來到聖杯戰爭！")
                .WithDescription($"**{userName}** 成為了新的御主！\n\n" +
                    "你獲得了：\n" +
                    "?? 100 魔力\n" +
                    "?? 3 令咒\n\n" +
                    "使用 `/hgw summon` 來召喚你的第一位從者吧！")
                .WithColor(Color.Gold)
                .WithCurrentTimestamp()
                .Build();

            return (welcomeEmbed, new ComponentBuilder());
        }

        public (Embed embed, ComponentBuilder component) GetPlayerInfo(ulong userId)
        {
            if (!_players.TryGetValue(userId, out var player))
                return (CommonHelper.BuildErrorResponse("你還不是御主！請先使用 `/hgw register` 註冊").Item2, new ComponentBuilder());

            var activeServant = player.ActiveServantId.HasValue
                ? player.Servants.FirstOrDefault(s => s.InstanceId == player.ActiveServantId.Value)
                : null;

            var embedBuilder = new EmbedBuilder()
                .WithTitle($"?? 御主資訊 - {player.UserName}")
                .AddField("?? 魔力", player.Mana, inline: true)
                .AddField("?? 令咒", player.CommandSeals, inline: true)
                .AddField("?? 從者數量", player.Servants.Count, inline: true)
                .AddField("?? 戰績", $"{player.Wins}勝 {player.Losses}敗", inline: true)
                .AddField("?? 召喚次數", player.SummonCount, inline: true)
                .WithColor(new Color(0x9B59B6))
                .WithCurrentTimestamp();

            if (activeServant != null)
            {
                string classEmoji = GetClassEmoji(activeServant.ClassName);
                embedBuilder.AddField("?? 當前從者",
                    $"{classEmoji} **{activeServant.Name}** Lv.{activeServant.Level}\n" +
                    $"HP: {activeServant.CurrentHp}/{activeServant.MaxHp} | ATK: {activeServant.Attack}",
                    inline: false);

                if (!string.IsNullOrEmpty(activeServant.FaceUrl))
                    embedBuilder.WithThumbnailUrl(activeServant.FaceUrl);
            }
            else if (player.Servants.Count > 0)
            {
                embedBuilder.AddField("?? 提示", "請使用 `/hgw select` 選擇出戰從者", inline: false);
            }

            return (embedBuilder.Build(), new ComponentBuilder());
        }

        public async Task<(Embed embed, ComponentBuilder component)> ClaimDailyBonusAsync(ulong userId, string userName)
        {
            if (!_players.TryGetValue(userId, out var player))
                return (CommonHelper.BuildErrorResponse("你還不是御主！請先使用 `/hgw register` 註冊").Item2, new ComponentBuilder());

            var now = DateTime.UtcNow;
            if (player.LastDailyBonus.HasValue && (now - player.LastDailyBonus.Value).TotalHours < 24)
            {
                var nextTime = player.LastDailyBonus.Value.AddHours(24);
                var remaining = nextTime - now;
                return (CommonHelper.BuildErrorResponse(
                    $"今天已經領取過了！\n下次領取時間：{remaining.Hours}小時 {remaining.Minutes}分鐘後").Item2, 
                    new ComponentBuilder());
            }

            player.Mana += 50;
            player.LastDailyBonus = now;
            SavePlayer(player);

            var embed = new EmbedBuilder()
                .WithTitle("?? 每日獎勵")
                .WithDescription($"**{userName}** 獲得了每日獎勵！\n\n" +
                    "?? +50 魔力\n" +
                    $"目前魔力：{player.Mana}")
                .WithColor(Color.Gold)
                .WithCurrentTimestamp()
                .Build();

            return (embed, new ComponentBuilder());
        }

        // ═══════════════════════════════════════════════════════════
        //  召喚系統
        // ═══════════════════════════════════════════════════════════

        public async Task<(Embed embed, ComponentBuilder component)> SummonServantAsync(ulong userId, string userName)
        {
            await EnsureInitAsync();

            if (!_players.TryGetValue(userId, out var player))
                return (CommonHelper.BuildErrorResponse("你還不是御主！請先使用 `/hgw register` 註冊").Item2, new ComponentBuilder());

            const int summonCost = 30;
            if (player.Mana < summonCost)
                return (CommonHelper.BuildErrorResponse($"魔力不足！需要 {summonCost} 魔力（當前：{player.Mana}）").Item2, new ComponentBuilder());

            player.Mana -= summonCost;
            player.SummonCount++;

            var rarity = RollRarity();
            var candidates = _servantPool.Where(s => s.Rarity == rarity).ToList();
            if (candidates.Count == 0)
                candidates = _servantPool.Where(s => s.Rarity == 3).ToList();

            var basicServant = candidates[_rng.Next(candidates.Count)];
            var (npName, fullImage) = await FetchServantDetailsAsync(basicServant.CollectionNo);

            var servant = new HgwServant
            {
                InstanceId = _nextInstanceId++,
                CollectionNo = basicServant.CollectionNo,
                Name = basicServant.Name,
                ClassName = basicServant.ClassName,
                Rarity = basicServant.Rarity,
                Level = 1,
                NpName = npName,
                FaceUrl = basicServant.Face,
                FullImageUrl = fullImage
            };
            servant.InitializeStats();

            player.Servants.Add(servant);
            if (player.ActiveServantId == null)
                player.ActiveServantId = servant.InstanceId;

            SavePlayer(player);

            string classEmoji = GetClassEmoji(servant.ClassName);
            string rarityStars = new string('★', servant.Rarity);

            var embed = new EmbedBuilder()
                .WithTitle("? 召喚成功！")
                .WithDescription($"**{userName}** 召喚了新的從者！\n\n" +
                    $"{classEmoji} **{servant.Name}**\n" +
                    $"{rarityStars}\n" +
                    $"寶具：{npName ?? "無"}\n\n" +
                    $"HP: {servant.MaxHp} | ATK: {servant.Attack} | DEF: {servant.Defense}")
                .WithColor(GetRarityColor(servant.Rarity))
                .WithImageUrl(fullImage)
                .WithFooter($"剩餘魔力：{player.Mana} | 從者總數：{player.Servants.Count}")
                .WithCurrentTimestamp()
                .Build();

            return (embed, new ComponentBuilder());
        }

        private int RollRarity()
        {
            var roll = _rng.Next(100);
            return roll switch
            {
                < 1 => 5,    // 1% SSR
                < 5 => 4,    // 4% SR
                < 25 => 3,   // 20% R
                < 55 => 2,   // 30% UC
                _ => 1       // 45% C
            };
        }

        public (Embed embed, ComponentBuilder component) ListServants(ulong userId)
        {
            if (!_players.TryGetValue(userId, out var player))
                return (CommonHelper.BuildErrorResponse("你還不是御主！").Item2, new ComponentBuilder());

            if (player.Servants.Count == 0)
                return (CommonHelper.BuildErrorResponse("你還沒有任何從者！使用 `/hgw summon` 來召喚吧！").Item2, new ComponentBuilder());

            var embedBuilder = new EmbedBuilder()
                .WithTitle($"?? {player.UserName} 的從者列表")
                .WithColor(new Color(0x3498DB))
                .WithCurrentTimestamp();

            var sorted = player.Servants.OrderByDescending(s => s.Rarity).ThenByDescending(s => s.Level).ToList();
            var displayCount = Math.Min(10, sorted.Count);

            for (int i = 0; i < displayCount; i++)
            {
                var s = sorted[i];
                string classEmoji = GetClassEmoji(s.ClassName);
                string activeMarker = s.InstanceId == player.ActiveServantId ? "?? " : "";
                string rarityStars = new string('★', s.Rarity);

                embedBuilder.AddField(
                    $"{activeMarker}{classEmoji} {s.Name} Lv.{s.Level}",
                    $"{rarityStars}\n" +
                    $"HP: {s.CurrentHp}/{s.MaxHp} | ATK: {s.Attack} | DEF: {s.Defense}\n" +
                    $"ID: {s.InstanceId}",
                    inline: true);
            }

            if (sorted.Count > displayCount)
                embedBuilder.WithFooter($"顯示 {displayCount}/{sorted.Count} 位從者");

            return (embedBuilder.Build(), new ComponentBuilder());
        }

        public (Embed embed, ComponentBuilder component) SelectServant(ulong userId, int instanceId)
        {
            if (!_players.TryGetValue(userId, out var player))
                return (CommonHelper.BuildErrorResponse("你還不是御主！").Item2, new ComponentBuilder());

            var servant = player.Servants.FirstOrDefault(s => s.InstanceId == instanceId);
            if (servant == null)
                return (CommonHelper.BuildErrorResponse($"找不到 ID 為 {instanceId} 的從者").Item2, new ComponentBuilder());

            player.ActiveServantId = instanceId;
            SavePlayer(player);

            string classEmoji = GetClassEmoji(servant.ClassName);
            var embed = new EmbedBuilder()
                .WithTitle("?? 從者已選擇")
                .WithDescription($"{classEmoji} **{servant.Name}** Lv.{servant.Level}\n" +
                    $"已設為出戰從者！\n\n" +
                    $"HP: {servant.CurrentHp}/{servant.MaxHp}\n" +
                    $"ATK: {servant.Attack} | DEF: {servant.Defense}")
                .WithColor(Color.Green)
                .WithThumbnailUrl(servant.FaceUrl)
                .WithCurrentTimestamp()
                .Build();

            return (embed, new ComponentBuilder());
        }

        // ═══════════════════════════════════════════════════════════
        //  戰鬥系統
        // ═══════════════════════════════════════════════════════════

        public async Task<(Embed embed, ComponentBuilder component)> StartBattleAsync(
            ulong channelId, ulong player1Id, string player1Name, ulong? player2Id = null, string player2Name = null)
        {
            await EnsureInitAsync();

            if (_battles.ContainsKey(channelId))
                return (CommonHelper.BuildErrorResponse("此頻道已有戰鬥進行中！").Item2, new ComponentBuilder());

            if (!_players.TryGetValue(player1Id, out var p1))
                return (CommonHelper.BuildErrorResponse($"{player1Name} 還不是御主！").Item2, new ComponentBuilder());

            if (!p1.ActiveServantId.HasValue)
                return (CommonHelper.BuildErrorResponse("請先選擇出戰從者！").Item2, new ComponentBuilder());

            var servant1 = p1.Servants.First(s => s.InstanceId == p1.ActiveServantId.Value);
            if (!servant1.IsAlive)
                return (CommonHelper.BuildErrorResponse("你的從者已陣亡！請先治療").Item2, new ComponentBuilder());

            HgwServant servant2;
            bool isVsNpc = player2Id == null;

            if (isVsNpc)
            {
                player2Id = 0;
                player2Name = "NPC 御主";
                servant2 = await GenerateNpcServantAsync();
            }
            else
            {
                if (!_players.TryGetValue(player2Id.Value, out var p2))
                    return (CommonHelper.BuildErrorResponse($"{player2Name} 還不是御主！").Item2, new ComponentBuilder());

                if (!p2.ActiveServantId.HasValue)
                    return (CommonHelper.BuildErrorResponse($"{player2Name} 還沒選擇出戰從者！").Item2, new ComponentBuilder());

                servant2 = p2.Servants.First(s => s.InstanceId == p2.ActiveServantId.Value);
                if (!servant2.IsAlive)
                    return (CommonHelper.BuildErrorResponse($"{player2Name} 的從者已陣亡！").Item2, new ComponentBuilder());
            }

            var battle = new HgwBattle
            {
                ChannelId = channelId,
                Player1Id = player1Id,
                Player2Id = player2Id.Value,
                Player1Name = player1Name,
                Player2Name = player2Name,
                Player1Servant = CloneServant(servant1),
                Player2Servant = CloneServant(servant2),
                IsVsNpc = isVsNpc
            };

            _battles[channelId] = battle;

            var embed = new EmbedBuilder()
                .WithTitle("?? 聖杯戰爭開始！")
                .WithDescription($"**{player1Name}** VS **{player2Name}**\n\n" +
                    $"{GetClassEmoji(servant1.ClassName)} {servant1.Name} Lv.{servant1.Level}\n" +
                    $"HP: {servant1.CurrentHp}/{servant1.MaxHp} | ATK: {servant1.Attack}\n\n" +
                    $"VS\n\n" +
                    $"{GetClassEmoji(servant2.ClassName)} {servant2.Name} Lv.{servant2.Level}\n" +
                    $"HP: {servant2.CurrentHp}/{servant2.MaxHp} | ATK: {servant2.Attack}\n\n" +
                    $"輪到 **{player1Name}** 行動！")
                .WithColor(new Color(0xE74C3C))
                .WithCurrentTimestamp()
                .Build();

            var component = BuildBattleButtons(channelId, servant1);

            return (embed, component);
        }

        public async Task<(Embed embed, ComponentBuilder component)> ExecuteBattleActionAsync(
            ulong channelId, ulong userId, BattleAction action)
        {
            if (!_battles.TryGetValue(channelId, out var battle))
                return (CommonHelper.BuildErrorResponse("此頻道沒有戰鬥進行中").Item2, new ComponentBuilder());

            if (battle.GetCurrentPlayerId() != userId)
                return (CommonHelper.BuildErrorResponse("還沒輪到你！").Item2, new ComponentBuilder());

            var attacker = battle.GetCurrentAttacker();
            var defender = battle.GetCurrentDefender();
            var result = new HgwBattleResult();

            switch (action)
            {
                case BattleAction.Attack:
                    result = ExecuteAttack(attacker, defender, battle);
                    break;
                case BattleAction.NoblePhantasm:
                    if (!attacker.CanUseNp)
                        return (CommonHelper.BuildErrorResponse("寶具未充能！").Item2, new ComponentBuilder());
                    result = ExecuteNoblePhantasm(attacker, defender, battle);
                    break;
                case BattleAction.Defend:
                    result = ExecuteDefend(attacker, battle);
                    break;
            }

            battle.BattleLog.Add(result.ActionDescription);

            if (defender.IsAlive && battle.IsVsNpc && !battle.IsPlayer1Turn)
            {
                await Task.Delay(500);
                var npcAction = _rng.Next(100) < 70 ? BattleAction.Attack : BattleAction.Defend;
                var npcResult = npcAction == BattleAction.Attack
                    ? ExecuteAttack(defender, attacker, battle)
                    : ExecuteDefend(defender, battle);
                battle.BattleLog.Add(npcResult.ActionDescription);
                result = npcResult;
            }

            if (!defender.IsAlive)
            {
                result.IsFinished = true;
                result.WinnerId = battle.GetCurrentPlayerId();
                result.WinnerName = battle.GetCurrentPlayerName();
                await FinishBattleAsync(battle, result.WinnerId.Value);
            }
            else
            {
                battle.NextTurn();
            }

            var embed = BuildBattleEmbed(battle, result);
            var component = result.IsFinished ? new ComponentBuilder() : BuildBattleButtons(channelId, battle.GetCurrentAttacker());

            return (embed, component);
        }

        private HgwBattleResult ExecuteAttack(HgwServant attacker, HgwServant defender, HgwBattle battle)
        {
            var classMultiplier = ClassAdvantage.GetMultiplier(attacker.ClassName, defender.ClassName);
            var isCrit = _rng.Next(100) < attacker.CritRate;
            var critMultiplier = isCrit ? 1.5 : 1.0;

            var baseDamage = attacker.Attack - (defender.Defense / 2);
            var damage = (int)(baseDamage * classMultiplier * critMultiplier);
            damage = Math.Max(10, damage);

            defender.TakeDamage(damage);
            attacker.AddNpCharge(20);

            string classMsg = classMultiplier > 1 ? " (職階相剋！)" : classMultiplier < 1 ? " (職階不利)" : "";
            string critMsg = isCrit ? " **爆擊！**" : "";

            return new HgwBattleResult
            {
                Damage = damage,
                IsCritical = isCrit,
                ActionDescription = $"**{attacker.Name}** 攻擊 **{defender.Name}**，造成 **{damage}** 傷害{classMsg}{critMsg}\n" +
                    $"NP +20 ({attacker.NpCharge}/100)"
            };
        }

        private HgwBattleResult ExecuteNoblePhantasm(HgwServant attacker, HgwServant defender, HgwBattle battle)
        {
            var classMultiplier = ClassAdvantage.GetMultiplier(attacker.ClassName, defender.ClassName);
            var baseDamage = attacker.Attack * 3;
            var damage = (int)(baseDamage * classMultiplier);

            defender.TakeDamage(damage);
            attacker.UseNp();

            string npName = attacker.NpName ?? "寶具";

            return new HgwBattleResult
            {
                Damage = damage,
                UsedNoblePhantasm = true,
                ActionDescription = $"**{attacker.Name}** 使用寶具 **『{npName}』**！\n" +
                    $"對 **{defender.Name}** 造成 **{damage}** 傷害！"
            };
        }

        private HgwBattleResult ExecuteDefend(HgwServant attacker, HgwBattle battle)
        {
            var healAmount = attacker.MaxHp / 10;
            attacker.Heal(healAmount);
            attacker.AddNpCharge(10);

            return new HgwBattleResult
            {
                ActionDescription = $"**{attacker.Name}** 防禦，恢復 **{healAmount}** HP，NP +10"
            };
        }

        private async Task FinishBattleAsync(HgwBattle battle, ulong winnerId)
        {
            _battles.Remove(battle.ChannelId);

            if (_players.TryGetValue(battle.Player1Id, out var p1))
            {
                if (winnerId == battle.Player1Id)
                {
                    p1.Wins++;
                    p1.Mana += 20;
                }
                else
                {
                    p1.Losses++;
                }

                var s1 = p1.Servants.First(s => s.InstanceId == p1.ActiveServantId);
                s1.CurrentHp = battle.Player1Servant.CurrentHp;
                s1.NpCharge = 0;
                SavePlayer(p1);
            }

            if (!battle.IsVsNpc && _players.TryGetValue(battle.Player2Id, out var p2))
            {
                if (winnerId == battle.Player2Id)
                {
                    p2.Wins++;
                    p2.Mana += 20;
                }
                else
                {
                    p2.Losses++;
                }

                var s2 = p2.Servants.First(s => s.InstanceId == p2.ActiveServantId);
                s2.CurrentHp = battle.Player2Servant.CurrentHp;
                s2.NpCharge = 0;
                SavePlayer(p2);
            }
        }

        private Embed BuildBattleEmbed(HgwBattle battle, HgwBattleResult result)
        {
            var s1 = battle.Player1Servant;
            var s2 = battle.Player2Servant;

            var embedBuilder = new EmbedBuilder()
                .WithTitle($"?? 回合 {battle.TurnCount}")
                .WithDescription(result.ActionDescription)
                .AddField($"{GetClassEmoji(s1.ClassName)} {s1.Name}",
                    $"HP: {s1.CurrentHp}/{s1.MaxHp}\nNP: {s1.NpCharge}/100",
                    inline: true)
                .AddField("VS", "??", inline: true)
                .AddField($"{GetClassEmoji(s2.ClassName)} {s2.Name}",
                    $"HP: {s2.CurrentHp}/{s2.MaxHp}\nNP: {s2.NpCharge}/100",
                    inline: true)
                .WithColor(new Color(0xE74C3C))
                .WithCurrentTimestamp();

            if (result.IsFinished)
            {
                embedBuilder.WithTitle("?? 戰鬥結束！")
                    .WithDescription($"**{result.WinnerName}** 獲勝！\n\n{result.ActionDescription}\n\n" +
                        $"獲得：?? +20 魔力")
                    .WithColor(Color.Gold);
            }
            else
            {
                embedBuilder.WithFooter($"輪到 {battle.GetCurrentPlayerName()} 行動");
            }

            return embedBuilder.Build();
        }

        private ComponentBuilder BuildBattleButtons(ulong channelId, HgwServant attacker)
        {
            var cb = new ComponentBuilder()
                .WithButton("?? 攻擊", $"hgw_attack_{channelId}", ButtonStyle.Danger)
                .WithButton("??? 防禦", $"hgw_defend_{channelId}", ButtonStyle.Primary);

            if (attacker.CanUseNp)
            {
                cb.WithButton($"?? 寶具 ({attacker.NpCharge}/100)", 
                    $"hgw_np_{channelId}", 
                    ButtonStyle.Success);
            }
            else
            {
                cb.WithButton($"?? 寶具 ({attacker.NpCharge}/100)", 
                    $"hgw_np_{channelId}", 
                    ButtonStyle.Secondary, 
                    disabled: true);
            }

            cb.WithButton("??? 投降", $"hgw_surrender_{channelId}", ButtonStyle.Secondary, row: 1);

            return cb;
        }

        public (Embed embed, ComponentBuilder component) HealServant(ulong userId, int instanceId)
        {
            if (!_players.TryGetValue(userId, out var player))
                return (CommonHelper.BuildErrorResponse("你還不是御主！").Item2, new ComponentBuilder());

            var servant = player.Servants.FirstOrDefault(s => s.InstanceId == instanceId);
            if (servant == null)
                return (CommonHelper.BuildErrorResponse($"找不到 ID 為 {instanceId} 的從者").Item2, new ComponentBuilder());

            const int healCost = 10;
            if (player.Mana < healCost)
                return (CommonHelper.BuildErrorResponse($"魔力不足！需要 {healCost} 魔力").Item2, new ComponentBuilder());

            if (servant.CurrentHp == servant.MaxHp)
                return (CommonHelper.BuildErrorResponse("從者已是滿血狀態！").Item2, new ComponentBuilder());

            player.Mana -= healCost;
            servant.Heal(servant.MaxHp);
            SavePlayer(player);

            var embed = new EmbedBuilder()
                .WithTitle("?? 治療成功")
                .WithDescription($"{GetClassEmoji(servant.ClassName)} **{servant.Name}** 已完全恢復！\n" +
                    $"HP: {servant.MaxHp}/{servant.MaxHp}\n\n" +
                    $"剩餘魔力：{player.Mana}")
                .WithColor(Color.Green)
                .WithCurrentTimestamp()
                .Build();

            return (embed, new ComponentBuilder());
        }

        // ═══════════════════════════════════════════════════════════
        //  輔助方法
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

                Console.WriteLine($"[HGW] 載入 {_servantPool.Count} 位從者");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HGW] LoadPool 失敗: {ex.Message}");
            }
        }

        private async Task<(string npName, string fullImage)> FetchServantDetailsAsync(int collectionNo)
        {
            if (_servantCache.TryGetValue(collectionNo, out var cached))
                return cached;

            try
            {
                var url = string.Format(NiceServantUrl, collectionNo);
                var json = await _http.GetStringAsync(url);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<FgoNiceServant>(json, opts);

                string fullImage = data?.ExtraAssets?.CharaGraph?.Ascension?
                    .OrderBy(kv => kv.Key)
                    .Select(kv => kv.Value)
                    .LastOrDefault();

                string npName = data?.NoblePhantasms?
                    .Where(np => !string.IsNullOrWhiteSpace(np.Name))
                    .OrderByDescending(np => np.Num)
                    .Select(np => np.Name)
                    .FirstOrDefault();

                var result = (npName, fullImage);
                _servantCache[collectionNo] = result;
                return result;
            }
            catch
            {
                return (null, null);
            }
        }

        private async Task<HgwServant> GenerateNpcServantAsync()
        {
            var basic = _servantPool[_rng.Next(_servantPool.Count)];
            var (npName, fullImage) = await FetchServantDetailsAsync(basic.CollectionNo);

            var servant = new HgwServant
            {
                InstanceId = -1,
                CollectionNo = basic.CollectionNo,
                Name = basic.Name,
                ClassName = basic.ClassName,
                Rarity = basic.Rarity,
                Level = _rng.Next(1, 5),
                NpName = npName,
                FaceUrl = basic.Face,
                FullImageUrl = fullImage
            };
            servant.InitializeStats();
            return servant;
        }

        private HgwServant CloneServant(HgwServant original)
        {
            return new HgwServant
            {
                InstanceId = original.InstanceId,
                CollectionNo = original.CollectionNo,
                Name = original.Name,
                ClassName = original.ClassName,
                Rarity = original.Rarity,
                Level = original.Level,
                MaxHp = original.MaxHp,
                CurrentHp = original.CurrentHp,
                Attack = original.Attack,
                Defense = original.Defense,
                CritRate = original.CritRate,
                NpCharge = original.NpCharge,
                NpName = original.NpName,
                FaceUrl = original.FaceUrl,
                FullImageUrl = original.FullImageUrl
            };
        }

        private void LoadAllPlayers()
        {
            try
            {
                var files = Directory.GetFiles(_dataPath, "*.json");
                foreach (var file in files)
                {
                    var json = File.ReadAllText(file);
                    var player = JsonSerializer.Deserialize<HgwPlayer>(json);
                    if (player != null)
                    {
                        _players[player.UserId] = player;
                        foreach (var servant in player.Servants)
                        {
                            if (servant.InstanceId >= _nextInstanceId)
                                _nextInstanceId = servant.InstanceId + 1;
                        }
                    }
                }
                Console.WriteLine($"[HGW] 載入 {_players.Count} 位玩家");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HGW] 載入玩家資料失敗: {ex.Message}");
            }
        }

        private void SavePlayer(HgwPlayer player)
        {
            try
            {
                var json = JsonSerializer.Serialize(player, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(_dataPath, $"{player.UserId}.json"), json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HGW] 儲存玩家失敗: {ex.Message}");
            }
        }

        private static string GetClassEmoji(string className) => className?.ToLower() switch
        {
            "saber" => "??",
            "archer" => "??",
            "lancer" => "??",
            "rider" => "??",
            "caster" => "??",
            "assassin" => "???",
            "berserker" => "??",
            "ruler" => "??",
            "avenger" => "??",
            "mooncancer" => "??",
            "alterego" => "??",
            "foreigner" => "??",
            "pretender" => "??",
            "shielder" => "???",
            _ => "?"
        };

        private static Color GetRarityColor(int rarity) => rarity switch
        {
            5 => new Color(255, 215, 0),   // Gold
            4 => new Color(192, 192, 192), // Silver
            3 => new Color(205, 127, 50),  // Bronze
            _ => new Color(128, 128, 128)  // Gray
        };
    }
}
