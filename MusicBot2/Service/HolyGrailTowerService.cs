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
    /// 聖杯塔 Roguelike 系統 (Redis 持久化版本)
    /// </summary>
    public class HolyGrailTowerService
    {
        private readonly HttpClient _http;
        private readonly Random _rng = new();
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly IDatabase _db;

        private List<FgoBasicServant> _servantPool = new();
        private readonly Dictionary<int, (string npName, string faceUrl)> _servantCache = new();
        private bool _initialized = false;

        // 當前 Run (儲存在記憶體或 Redis)
        private readonly Dictionary<ulong, HgwTowerRun> _runs = new();

        private const string RedisPlayerPrefix = "hgwt_player:";
        private const string BasicServantUrl = "https://api.atlasacademy.io/export/TW/basic_servant.json";
        private const string NiceServantUrl = "https://api.atlasacademy.io/nice/TW/servant/{0}?lore=false";

        public HolyGrailTowerService(string redisConnectionString)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            // 初始化 Redis
            var redis = ConnectionMultiplexer.Connect(redisConnectionString);
            _db = redis.GetDatabase();
            Console.WriteLine("[HolyGrailTower] Redis 連線成功");
        }

        // ═══════════════════════════════════════════════════════════
        //  Redis 儲存與讀取 (御主存檔)
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
                SummonTickets = 10,
                SaintQuartz = 0
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
                Console.WriteLine($"[HolyGrailTower] 儲存 {player.UserId} 存檔失敗: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  主指令
        // ═══════════════════════════════════════════════════════════

        public async Task<(Embed embed, ComponentBuilder component)> RegisterPlayerAsync(ulong userId, string userName)
        {
            await EnsureInitAsync();

            var redisKey = RedisPlayerPrefix + userId;
            if (_db.KeyExists(redisKey))
            {
                var existing = LoadPlayer(userId, userName);
                var embed = new EmbedBuilder()
                    .WithTitle("?? 聖杯塔 - 御主資訊")
                    .WithDescription($"**{userName}** 已經是註冊的御主了！")
                    .AddField("?? 召喚券", existing.SummonTickets, inline: true)
                    .AddField("?? 聖晶石", existing.SaintQuartz, inline: true)
                    .AddField("?? 從者圖鑑", $"{existing.OwnedServants.Count} 位", inline: true)
                    .AddField("?? 最高紀錄", $"第 {existing.HighestFloor} 層", inline: true)
                    .WithColor(new Color(0xFFD700))
                    .WithFooter("輸入 /fate召喚 開始抽卡！")
                    .WithCurrentTimestamp()
                    .Build();

                return (embed, new ComponentBuilder());
            }

            var player = new HolyGrailTowerPlayer
            {
                UserId = userId,
                UserName = userName,
                SummonTickets = 10,
                SaintQuartz = 5
            };

            SavePlayer(player);

            var welcomeEmbed = new EmbedBuilder()
                .WithTitle("? 歡迎來到聖杯塔！")
                .WithDescription($"**{userName}** 成為了新的御主！\n\n" +
                    "?? **遊戲目標**：\n" +
                    "組建你的3人英靈小隊，登上 100 層聖杯塔！\n\n" +
                    "?? **新手禮包**：\n" +
                    "?? 10 張召喚券\n" +
                    "?? 5 顆聖晶石\n\n" +
                    "?? **下一步**：\n" +
                    "1. 使用 `/聖杯塔召喚` 抽取從者\n" +
                    "2. 使用 `/開始爬塔` 開始探索每一層遭遇！\n" +
                    "3. 使用 `/聖杯塔每日` 領取每日召喚補助")
                .WithColor(Color.Gold)
                .WithCurrentTimestamp()
                .Build();

            return (welcomeEmbed, new ComponentBuilder());
        }

        public (Embed embed, ComponentBuilder component) GetPlayerInfo(ulong userId)
        {
            var redisKey = RedisPlayerPrefix + userId;
            if (!_db.KeyExists(redisKey))
                return (CommonHelper.BuildErrorResponse("你還不是御主！請先使用 `/聖杯塔註冊` 註冊").Item2, new ComponentBuilder());

            var player = LoadPlayer(userId);
            var topServants = player.OwnedServants
                .OrderByDescending(s => s.Rarity)
                .ThenByDescending(s => s.Level)
                .Take(3)
                .ToList();

            var embedBuilder = new EmbedBuilder()
                .WithTitle($"?? 聖杯塔 - {player.UserName}")
                .AddField("?? 資源", $"?? 召喚券: {player.SummonTickets}\n?? 聖晶石: {player.SaintQuartz}", inline: true)
                .AddField("?? 記錄", $"?? 最高層數: {player.HighestFloor}\n?? 總挑戰: {player.TotalRuns}\n?? 總擊殺: {player.TotalKills}", inline: true)
                .AddField("?? 圖鑑", $"{player.OwnedServants.Count} 位從者", inline: true)
                .WithColor(new Color(0x9B59B6))
                .WithCurrentTimestamp();

            if (topServants.Count > 0)
            {
                var servantList = string.Join("\n", topServants.Select(s =>
                    $"{GetClassEmoji(s.ClassName)} **{s.Name}** Lv.{s.Level} ({string.Concat(Enumerable.Repeat("★", s.Rarity))})"));
                embedBuilder.AddField("? 頂級從者", servantList, inline: false);
            }

            return (embedBuilder.Build(), new ComponentBuilder());
        }

        public async Task<(Embed embed, ComponentBuilder component)> ClaimDailyRewardAsync(ulong userId, string userName)
        {
            var redisKey = RedisPlayerPrefix + userId;
            if (!_db.KeyExists(redisKey))
                return (CommonHelper.BuildErrorResponse("你還不是御主！請使用 `/聖杯塔註冊`").Item2, new ComponentBuilder());

            var player = LoadPlayer(userId, userName);
            var now = DateTime.UtcNow;
            if (player.LastDailyReward.HasValue && (now - player.LastDailyReward.Value).TotalHours < 24)
            {
                var nextTime = player.LastDailyReward.Value.AddHours(24);
                var remaining = nextTime - now;
                return (CommonHelper.BuildErrorResponse(
                    $"今天已領取過了！下次領取時間：{remaining.Hours}小時 {remaining.Minutes}分鐘後").Item2,
                    new ComponentBuilder());
            }

            player.SummonTickets += 3;
            player.LastDailyReward = now;
            SavePlayer(player);

            var embed = new EmbedBuilder()
                .WithTitle("?? 每日福利")
                .WithDescription($"**{userName}** 獲得了每日召喚補助！\n\n" +
                    "?? +3 召喚券\n" +
                    $"目前擁有：{player.SummonTickets} 張！")
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

            var redisKey = RedisPlayerPrefix + userId;
            if (!_db.KeyExists(redisKey))
                return (CommonHelper.BuildErrorResponse("你還不是御主！請先使用 `/聖杯塔註冊`").Item2, new ComponentBuilder());

            var player = LoadPlayer(userId, userName);

            if (player.SummonTickets < 1)
                return (CommonHelper.BuildErrorResponse($"召喚券不足！需要 1 張（當前：{player.SummonTickets}）\n請明天領取 `/聖杯塔每日` 或挑戰登塔！").Item2, new ComponentBuilder());

            player.SummonTickets--;

            var rarity = RollRarity();
            var candidates = _servantPool.Where(s => s.Rarity == rarity).ToList();
            if (candidates.Count == 0)
                candidates = _servantPool.Where(s => s.Rarity == 3).ToList();

            var basicServant = candidates[_rng.Next(candidates.Count)];
            var (npName, faceUrl) = await FetchServantDetailsAsync(basicServant.CollectionNo);

            var existing = player.OwnedServants.FirstOrDefault(s => s.CollectionNo == basicServant.CollectionNo);
            bool isNew = existing == null;
            string resultText;

            if (isNew)
            {
                var servant = new TowerServant
                {
                    CollectionNo = basicServant.CollectionNo,
                    Name = basicServant.Name,
                    ClassName = basicServant.ClassName,
                    Rarity = basicServant.Rarity,
                    Level = 1,
                    NpLevel = 1,
                    NpName = npName,
                    FaceUrl = faceUrl ?? basicServant.Face
                };
                player.OwnedServants.Add(servant);
                resultText = "? **NEW! 新英靈加入！**";
            }
            else
            {
                existing.NpLevel = Math.Min(5, existing.NpLevel + 1);
                existing.Level = Math.Min(100, existing.Level + 2); // 重複抽到等級、屬性微增幅
                resultText = $"?? 英靈重疊！等級 +2 | 寶具階段提升至 Lv.{existing.NpLevel}";
            }

            SavePlayer(player);

            string classEmoji = GetClassEmoji(basicServant.ClassName);
            string rarityStars = string.Concat(Enumerable.Repeat("★", basicServant.Rarity));

            var embed = new EmbedBuilder()
                .WithTitle("?? 聖杯大召喚")
                .WithDescription($"**{userName}** 成功召喚：\n\n" +
                    $"{classEmoji} **{basicServant.Name}**\n" +
                    $"{rarityStars}\n" +
                    $"*{resultText}*\n\n" +
                    $"剩餘召喚券：{player.SummonTickets} 張")
                .WithColor(GetRarityColor(basicServant.Rarity))
                .WithThumbnailUrl(faceUrl ?? basicServant.Face)
                .WithFooter($"英靈陣容：已解鎖 {player.OwnedServants.Count} 名")
                .WithCurrentTimestamp()
                .Build();

            return (embed, new ComponentBuilder());
        }

        public (Embed embed, ComponentBuilder component) ListServants(ulong userId)
        {
            var redisKey = RedisPlayerPrefix + userId;
            if (!_db.KeyExists(redisKey))
                return (CommonHelper.BuildErrorResponse("你還不是御主！").Item2, new ComponentBuilder());

            var player = LoadPlayer(userId);
            if (player.OwnedServants.Count == 0)
                return (CommonHelper.BuildErrorResponse("你還沒有任何從者！使用 `/聖杯塔召喚` 來抽卡英靈吧！").Item2, new ComponentBuilder());

            var sorted = player.OwnedServants.OrderByDescending(s => s.Rarity).ThenByDescending(s => s.Level).ToList();
            var displayCount = Math.Min(15, sorted.Count);

            var embedBuilder = new EmbedBuilder()
                .WithTitle($"?? {player.UserName} 的英靈小隊圖鑑")
                .WithColor(new Color(0x3498DB))
                .WithCurrentTimestamp();

            for (int i = 0; i < displayCount; i++)
            {
                var s = sorted[i];
                string classEmoji = GetClassEmoji(s.ClassName);
                string rarityStars = string.Concat(Enumerable.Repeat("★", s.Rarity));

                embedBuilder.AddField(
                    $"{classEmoji} {s.Name} Lv.{s.Level}",
                    $"{rarityStars}\n寶具 Lv.{s.NpLevel}\n等階生命值: {s.GetMaxHp()} | 攻擊力: {s.GetAttack()}",
                    inline: true);
            }

            if (sorted.Count > displayCount)
                embedBuilder.WithFooter($"顯示前 {displayCount}/{sorted.Count} 位英靈");

            return (embedBuilder.Build(), new ComponentBuilder());
        }

        // ═══════════════════════════════════════════════════════════
        //  爬塔 Roguelike 系統
        // ═══════════════════════════════════════════════════════════

        public async Task<(Embed embed, ComponentBuilder component)> StartTowerRunAsync(
            ulong channelId, ulong userId, string userName)
        {
            await EnsureInitAsync();

            if (_runs.ContainsKey(channelId))
            {
                return (CommonHelper.BuildErrorResponse(
                    "此頻道已有進行中的登塔戰役！\n可以使用 `/fate取消爬塔` 放棄先前未完成的冒險，重新開始。").Item2, 
                    new ComponentBuilder());
            }

            var redisKey = RedisPlayerPrefix + userId;
            if (!_db.KeyExists(redisKey))
                return (CommonHelper.BuildErrorResponse("你還不是御主！請先註冊：`/聖杯塔註冊`").Item2, new ComponentBuilder());

            var player = LoadPlayer(userId, userName);
            if (player.OwnedServants.Count == 0)
                return (CommonHelper.BuildErrorResponse("你召喚空間沒有任何英靈！請使用 `/聖杯塔召喚` 抽取從者加入小隊！").Item2, new ComponentBuilder());

            player.TotalRuns++;
            SavePlayer(player);

            var run = new HgwTowerRun
            {
                ChannelId = channelId,
                PlayerId = userId,
                PlayerName = userName,
                CurrentFloor = 1,
                Gold = 100,
                IsFinished = false
            };

            _runs[channelId] = run;

            var embed = new EmbedBuilder()
                .WithTitle("?? 聖杯塔 - 御組隊出征")
                .WithDescription($"**{userName}** 已抵達聖杯塔底部基座！\n" +
                    "請從你解鎖的高階英靈中**點選加入隊伍** (最多可帶3名)。")
                .WithColor(new Color(0xE74C3C))
                .WithFooter($"目前層數：第 {run.CurrentFloor} 層")
                .WithCurrentTimestamp()
                .Build();

            var component = BuildTeamSelectionButtons(userId, run);

            return (embed, component);
        }

        private ComponentBuilder BuildTeamSelectionButtons(ulong userId, HgwTowerRun run)
        {
            var player = LoadPlayer(userId);
            var cb = new ComponentBuilder();
            var servants = player.OwnedServants.OrderByDescending(s => s.Rarity).ThenByDescending(s => s.Level).Take(9).ToList();

            for (int i = 0; i < servants.Count && i < 9; i++)
            {
                var s = servants[i];
                string label = $"{GetClassEmoji(s.ClassName)} {s.Name.Substring(0, Math.Min(10, s.Name.Length))} Lv.{s.Level}";
                bool isSelected = run.Team.Any(x => x.CollectionNo == s.CollectionNo);

                cb.WithButton(
                    label: isSelected ? "? " + s.Name.Substring(0, Math.Min(8, s.Name.Length)) : label,
                    customId: $"hgwt_select_{userId}_{s.CollectionNo}",
                    style: isSelected ? ButtonStyle.Success : ButtonStyle.Primary,
                    row: i / 3,
                    disabled: run.IsFinished
                );
            }

            cb.WithButton("?? 開始爬塔！出征第 1 層", $"hgwt_start_run_{userId}", ButtonStyle.Danger, row: 3, disabled: run.Team.Count == 0);

            return cb;
        }

        public async Task<(Embed embed, ComponentBuilder component)> CancelTowerRunAsync(ulong channelId, ulong userId)
        {
            if (!_runs.TryGetValue(channelId, out var run))
            {
                return (CommonHelper.BuildErrorResponse("此頻道目前沒有正在進行的聖杯塔挑戰哦！").Item2, new ComponentBuilder());
            }

            if (run.PlayerId != userId)
                return (CommonHelper.BuildErrorResponse("你不是發起這回聖杯塔挑戰的御主，無法強制撤銷隊伍！").Item2, new ComponentBuilder());

            _runs.Remove(channelId);

            var embed = new EmbedBuilder()
                .WithTitle("?? 聖杯塔 - 挑戰中途徹兵")
                .WithDescription($"御主 **{run.PlayerName}** 宣告戰略撤退，解散英靈小隊離塔！\n挑戰結束在第 **{run.CurrentFloor}** 層。")
                .WithColor(Color.DarkOrange)
                .WithCurrentTimestamp()
                .Build();

            return (embed, new ComponentBuilder());
        }

        // ═══════════════════════════════════════════════════════════
        //  遭遇與回合核心 (事件驅動)
        // ═══════════════════════════════════════════════════════════

        public async Task<(Embed embed, ComponentBuilder component)> HandleButtonInteractionAsync(ulong userId, ulong channelId, string customId)
        {
            if (!_runs.TryGetValue(channelId, out var run))
                return (CommonHelper.BuildErrorResponse("找不到進行中的爬塔，可能已經因中途徹退或戰敗關閉。").Item2, new ComponentBuilder());

            if (run.PlayerId != userId)
                return (CommonHelper.BuildErrorResponse("這不是你的爬塔！必須由發起冒險的御主按鈕操作。").Item2, new ComponentBuilder());

            var parts = customId.Split('_');
            var action = parts[1];

            try
            {
                switch (action)
                {
                    case "select":
                        int colNo = int.Parse(parts[3]);
                        if (run.Team.Any(s => s.CollectionNo == colNo))
                        {
                            // 已經在隊伍中，點擊即是取消
                            run.Team.RemoveAll(x => x.CollectionNo == colNo);
                        }
                        else
                        {
                            if (run.Team.Count >= 3)
                                return (CommonHelper.BuildErrorResponse("極限英靈小隊最多配載 3 位哦！").Item2, BuildTeamSelectionButtons(userId, run));

                            var player = LoadPlayer(userId);
                            var serv = player.OwnedServants.FirstOrDefault(x => x.CollectionNo == colNo);
                            if (serv != null)
                                run.Team.Add(HgwTowerServantInstance.FromServant(serv));
                        }
                        return (new EmbedBuilder()
                            .WithTitle("?? 英靈陣型調整中")
                            .WithDescription($"目前已選入英靈數：{run.Team.Count}/3 名！\n\n**當前小隊**：\n" + 
                                string.Join("\n", run.Team.Select(s => $"{GetClassEmoji(s.ClassName)} **{s.Name}** HP: {s.MaxHp}")))
                            .WithColor(Color.Blue).Build(), BuildTeamSelectionButtons(userId, run));

                    case "start":
                        if (run.Team.Count == 0)
                            return (CommonHelper.BuildErrorResponse("請先加指派至少 1 位從者！").Item2, BuildTeamSelectionButtons(userId, run));

                        run.MaxHp = run.Team.Sum(s => s.MaxHp);
                        run.CurrentHp = run.MaxHp;
                        run.CurrentEncounter = GenerateEncounter(run.CurrentFloor);

                        return (BuildFloorEmbed(run), BuildFloorButtons(channelId, run));

                    case "attack":
                    case "np":
                    case "defend":
                        int index = int.Parse(parts[3]);
                        var battleResult = ExecuteCombatAction(run, index, action);

                        if (battleResult.IsFinished)
                        {
                            // 戰鬥勝利
                            var player = LoadPlayer(userId);
                            player.TotalKills += run.CurrentEncounter.Enemies.Count;

                            // 更新最高層數
                            if (run.CurrentFloor > player.HighestFloor)
                                player.HighestFloor = run.CurrentFloor;

                            // 關卡進度獎勵
                            int goldReward = 20 + run.CurrentFloor * 2;
                            run.Gold += goldReward;
                            player.SummonTickets += (run.CurrentFloor % 5 == 0) ? 2 : 0;
                            player.SaintQuartz += (run.CurrentFloor % 10 == 0) ? 1 : 0;
                            SavePlayer(player);

                            string rewardsText = $"?? +{goldReward} 本次金幣\n";
                            if (run.CurrentFloor % 5 == 0) rewardsText += "?? +2 召喚券！\n";
                            if (run.CurrentFloor % 10 == 0) rewardsText += "?? +1 聖晶石！\n";

                            var winEmbed = new EmbedBuilder()
                                .WithTitle($"?? 戰鬥勝利！第 {run.CurrentFloor} 層通過！")
                                .WithDescription($"{battleResult.ActionDescription}\n\n" +
                                    $"恭喜擊退第 {run.CurrentFloor} 層魔物！小隊獲得獎勵：\n\n{rewardsText}")
                                .WithColor(Color.Gold)
                                .Build();

                            // 前進到下一層！
                            run.CurrentFloor++;
                            var nextCb = new ComponentBuilder()
                                .WithButton($"?? 探索第 {run.CurrentFloor} 層", $"hgwt_next_floor_{userId}", ButtonStyle.Danger);

                            return (winEmbed, nextCb);
                        }

                        // 檢查玩家是否全滅
                        if (run.Team.All(s => !s.IsAlive))
                        {
                            _runs.Remove(channelId);
                            var defeatEmbed = new EmbedBuilder()
                                .WithTitle($"?? 隊伍全滅 - 挑戰落敗")
                                .WithDescription($"{battleResult.ActionDescription}\n\n聖杯碎片魔能消散！冒險終結在第 {run.CurrentFloor} 層。\n" +
                                    $"你可以召喚更多英靈、鍛鍊或進行每日好運抽卡後再次整隊！")
                                .WithColor(Color.Red)
                                .WithCurrentTimestamp()
                                .Build();

                            return (defeatEmbed, new ComponentBuilder());
                        }

                        return (BuildFloorEmbed(run), BuildFloorButtons(channelId, run));

                    case "next":
                        run.CurrentEncounter = GenerateEncounter(run.CurrentFloor);
                        return (BuildFloorEmbed(run), BuildFloorButtons(channelId, run));

                    case "treasure":
                        int sqGift = (_rng.Next(100) < 15) ? 1 : 0;
                        int ticksGift = (_rng.Next(100) < 30) ? 1 : 0;
                        int gGift = 50 + run.CurrentFloor * 5;
                        run.Gold += gGift;

                        var pl = LoadPlayer(userId);
                        pl.SummonTickets += ticksGift;
                        pl.SaintQuartz += sqGift;
                        SavePlayer(pl);

                        string treasureDesc = $"你在華麗的聖杯密箱中搜刮出了：\n\n" +
                            $"?? +{gGift} 金幣\n";
                        if (ticksGift > 0) treasureDesc += "?? +1 召喚券！\n";
                        if (sqGift > 0) treasureDesc += "?? +1 聖晶石！\n";

                        var chestEmbed = new EmbedBuilder()
                            .WithTitle($"? 第 {run.CurrentFloor} 層 - 寶藏")
                            .WithDescription(treasureDesc)
                            .WithColor(Color.Orange)
                            .Build();

                        run.CurrentFloor++;
                        var nextChestCb = new ComponentBuilder()
                            .WithButton($"?? 探索第 {run.CurrentFloor} 層", $"hgwt_next_floor_{userId}", ButtonStyle.Danger);

                        return (chestEmbed, nextChestCb);

                    case "shop":
                        if (parts[2] == "leave")
                        {
                            run.CurrentFloor++;
                            var nextShopCb = new ComponentBuilder()
                                .WithButton($"?? 探索第 {run.CurrentFloor} 層", $"hgwt_next_floor_{userId}", ButtonStyle.Danger);
                            return (new EmbedBuilder()
                                .WithTitle("?? 離開商店")
                                .WithDescription("向流浪商人致意後，小隊整裝繼續進發。")
                                .WithColor(Color.DarkBlue).Build(), nextShopCb);
                        }
                        else if (parts[2] == "heal")
                        {
                            if (run.Gold < 30)
                                return (CommonHelper.BuildErrorResponse("金幣不足（需要30金金幣！）").Item2, BuildFloorButtons(channelId, run));

                            run.Gold -= 30;
                            foreach (var s in run.Team)
                            {
                                if (s.IsAlive)
                                {
                                    s.CurrentHp = Math.Min(s.MaxHp, s.CurrentHp + (int)(s.MaxHp * 0.4));
                                }
                            }

                            return (new EmbedBuilder()
                                .WithTitle("?? 購買魔藥治療成功")
                                .WithDescription($"商人的靈草秘藥治癒了隊伍！存活中的英靈 HP 恢復 40%！\n剩餘金幣：{run.Gold} 金")
                                .WithColor(Color.Green).Build(), BuildFloorButtons(channelId, run));
                        }
                        break;

                    case "rest":
                        foreach (var s in run.Team)
                        {
                            if (s.IsAlive)
                                s.CurrentHp = Math.Min(s.MaxHp, s.CurrentHp + (int)(s.MaxHp * 0.5));
                        }
                        run.CurrentFloor++;
                        var nextRestCb = new ComponentBuilder()
                            .WithButton($"?? 探索第 {run.CurrentFloor} 層", $"hgwt_next_floor_{userId}", ButtonStyle.Danger);
                        return (new EmbedBuilder()
                            .WithTitle("?? 舒適的餘燼營火")
                            .WithDescription("在清靜的魔力源泉旁紮營野宿，全員 HP 大幅恢復了 50%！")
                            .WithColor(Color.Green).Build(), nextRestCb);

                    case "train":
                        foreach (var s in run.Team)
                        {
                            if (s.IsAlive)
                            {
                                s.BonusAtk += 10;
                                s.Attack += 10;
                            }
                        }
                        run.CurrentFloor++;
                        var nextTrainCb = new ComponentBuilder()
                            .WithButton($"?? 探索第 {run.CurrentFloor} 層", $"hgwt_next_floor_{userId}", ButtonStyle.Danger);
                        return (new EmbedBuilder()
                            .WithTitle("?? 訓練英靈體技")
                            .WithDescription("英靈隊伍進行了短暫的魔能演練，全員攻擊力永久提神 +10 額外增幅！")
                            .WithColor(Color.Blue).Build(), nextTrainCb);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HolyGrailTower] HandleButtonInteraction 錯誤: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            return (null, null);
        }

        private HgwBattleResult ExecuteCombatAction(HgwTowerRun run, int servantIndex, string actionType)
        {
            var result = new HgwBattleResult();
            var attacker = run.Team[servantIndex];
            var enemies = run.CurrentEncounter.Enemies;
            var targetEnemy = enemies.FirstOrDefault(e => e.IsAlive);

            if (targetEnemy == null)
            {
                result.IsFinished = true;
                return result;
            }

            string actDesc = "";

            // 1. 玩家英靈的行動
            if (actionType == "attack")
            {
                double multiplier = ClassAdvantage.GetMultiplier(attacker.ClassName, targetEnemy.Type);
                int baseDmg = attacker.Attack - (targetEnemy.Defense / 2);
                int damage = Math.Max(15, (int)(baseDmg * multiplier));
                targetEnemy.CurrentHp = Math.Max(0, targetEnemy.CurrentHp - damage);
                attacker.AddNpCharge(25);
                actDesc += $"?? **{attacker.Name}** 使用普通攻擊對 **{targetEnemy.Name}** 造成了 **{damage}** 傷害！（NP +25）\n";
            }
            else if (actionType == "np")
            {
                attacker.UseNp();
                int damage = attacker.Attack * 3;
                targetEnemy.CurrentHp = Math.Max(0, targetEnemy.CurrentHp - damage);
                actDesc += $"?? **{attacker.Name}** 釋放必殺寶具『**{attacker.NpName ?? "寶具"}**』對 **{targetEnemy.Name}** 轟炸造成了 **{damage}** 巨量核心傷害！\n";
            }

            // 2. 判斷怪物是否死亡
            if (!targetEnemy.IsAlive)
            {
                actDesc += $"?? **{targetEnemy.Name}** 倒解體死亡！\n";
                targetEnemy = enemies.FirstOrDefault(e => e.IsAlive);
            }

            // 3. 所有敵人都倒下
            if (targetEnemy == null)
            {
                result.IsFinished = true;
                result.ActionDescription = actDesc;
                return result;
            }

            // 4. 餘存怪物自動群體反擊
            actDesc += "\n?? **魔物咆哮！進入魔物反擊階段：**\n";
            foreach (var enemy in enemies.Where(e => e.IsAlive))
            {
                var targetServant = run.Team.Where(s => s.IsAlive).OrderBy(_ => _rng.Next()).FirstOrDefault();
                if (targetServant != null)
                {
                    int dmg = Math.Max(10, enemy.Attack - (targetServant.Defense / 2));
                    targetServant.CurrentHp = Math.Max(0, targetServant.CurrentHp - dmg);
                    targetServant.AddNpCharge(15); // 受擊充能
                    actDesc += $"  ?? **{enemy.Name}** 對 **{targetServant.Name}** 發射魔彈造成了 **{dmg}** 點反擊傷害！(英靈 NP+15)\n";
                }
            }

            result.IsFinished = false;
            result.ActionDescription = actDesc;
            return result;
        }

        private HgwTowerEncounter GenerateEncounter(int floor)
        {
            if (floor % 10 == 0) return GenerateBossFight(floor);
            if (floor % 5 == 0) return GenerateEliteFight(floor);

            int roll = _rng.Next(100);
            if (roll < 55) return GenerateNormalFight(floor);
            if (roll < 75) return GenerateTreasure(floor);
            if (roll < 90) return GenerateShop(floor);
            return GenerateRestSite(floor);
        }

        private HgwTowerEncounter GenerateNormalFight(int floor)
        {
            int enemyCount = Math.Min(1 + floor / 15, 3);
            var enemies = new List<HgwTowerEnemy>();
            var enemyNames = new[] { "骷髏雜兵群", "墮天使黑鳥", "荒野巨石兵", "暗森林狂狼", "骸骨咒術師" };

            for (int i = 0; i < enemyCount; i++)
            {
                enemies.Add(new HgwTowerEnemy
                {
                    Name = enemyNames[_rng.Next(enemyNames.Length)],
                    Type = "berserker",
                    MaxHp = 80 + floor * 25,
                    CurrentHp = 80 + floor * 25,
                    Attack = 15 + floor * 4,
                    Defense = 10 + floor * 2
                });
            }

            return new HgwTowerEncounter
            {
                Type = EncounterType.NormalBattle,
                Enemies = enemies
            };
        }

        private HgwTowerEncounter GenerateEliteFight(int floor)
        {
            var enemy = new HgwTowerEnemy
            {
                Name = "【精英魔將】熔岩百首魔巨像",
                Type = "berserker",
                MaxHp = 300 + floor * 60,
                CurrentHp = 300 + floor * 60,
                Attack = 35 + floor * 8,
                Defense = 25 + floor * 4,
                IsElite = true,
                Skills = new() { "熔岩踐踏", "自愈" }
            };

            return new HgwTowerEncounter
            {
                Type = EncounterType.EliteBattle,
                Enemies = new() { enemy }
            };
        }

        private HgwTowerEncounter GenerateBossFight(int floor)
        {
            var bossNames = new[] { "【天災巨獸】虛空末日龍王", "【詛咒誓王】死靈大騎士", "【暴君主宰】黑暗審判者" };
            var enemy = new HgwTowerEnemy
            {
                Name = bossNames[_rng.Next(bossNames.Length)],
                Type = "berserker",
                MaxHp = 500 + floor * 120,
                CurrentHp = 500 + floor * 120,
                Attack = 55 + floor * 12,
                Defense = 35 + floor * 6,
                IsBoss = true,
                Skills = new() { "九重雷擊", "群體重擊", "能量竊取" }
            };

            return new HgwTowerEncounter
            {
                Type = EncounterType.BossBattle,
                Enemies = new() { enemy }
            };
        }

        private HgwTowerEncounter GenerateTreasure(int floor) => new() { Type = EncounterType.Treasure };
        private HgwTowerEncounter GenerateShop(int floor) => new() { Type = EncounterType.Shop };
        private HgwTowerEncounter GenerateRestSite(int floor) => new() { Type = EncounterType.RestSite };

        private Embed BuildFloorEmbed(HgwTowerRun run)
        {
            var encounter = run.CurrentEncounter;
            var embedBuilder = new EmbedBuilder()
                .WithTitle($"?? 聖杯塔 - 第 {run.CurrentFloor} 層")
                .WithColor(GetEncounterColor(encounter.Type))
                .WithCurrentTimestamp();

            switch (encounter.Type)
            {
                case EncounterType.NormalBattle:
                case EncounterType.EliteBattle:
                case EncounterType.BossBattle:
                    string enemiesDesc = string.Join("\n", encounter.Enemies.Select(e =>
                        $"{(e.IsBoss ? "??" : e.IsElite ? "?" : "??")} **{e.Name}**\n" +
                        $"  ?? HP: {e.CurrentHp}/{e.MaxHp} | ?? ATK: {e.Attack}"));

                    embedBuilder.WithDescription($"**?? 進入戰鬥遭遇！**\n\n{enemiesDesc}");

                    var teamDesc = string.Join("\n", run.Team.Select(s =>
                        $"{(s.IsAlive ? "??" : "??")} {GetClassEmoji(s.ClassName)} **{s.Name}** - " + 
                        $"HP: {s.CurrentHp}/{s.MaxHp} | NP: **{s.NpCharge}/100**"));
                    embedBuilder.AddField("?? 你的英靈隊伍", teamDesc, inline: false);
                    embedBuilder.WithFooter($"金幣儲蓄：{run.Gold}金 | 圖鑑數：{run.Team.Count} 位精英");
                    break;

                case EncounterType.Treasure:
                    embedBuilder.WithDescription("? **此層道路上安置了一個古老發光的聖杯寶箱！**");
                    break;

                case EncounterType.Shop:
                    embedBuilder.WithDescription($"?? **流浪商人營地**\n\n流浪商人向你展示草藥和秘技。\n你有 **{run.Gold}** 金金幣。");
                    break;

                case EncounterType.RestSite:
                    embedBuilder.WithDescription("?? **這層營火餘溫裊裊，是絕佳的魔泉休息地。**");
                    break;
            }

            return embedBuilder.Build();
        }

        private ComponentBuilder BuildFloorButtons(ulong channelId, HgwTowerRun run)
        {
            var cb = new ComponentBuilder();
            var encounter = run.CurrentEncounter;

            switch (encounter.Type)
            {
                case EncounterType.NormalBattle:
                case EncounterType.EliteBattle:
                case EncounterType.BossBattle:
                    for (int i = 0; i < run.Team.Count; i++)
                    {
                        var servant = run.Team[i];
                        if (servant.IsAlive)
                        {
                            cb.WithButton(
                                label: $"?? {servant.Name.Substring(0, Math.Min(5, servant.Name.Length))} 攻擊",
                                customId: $"hgwt_attack_{channelId}_{i}",
                                style: ButtonStyle.Danger,
                                row: i
                            );

                            cb.WithButton(
                                label: $"?? 寶具 ({servant.NpCharge}%)",
                                customId: $"hgwt_np_{channelId}_{i}",
                                style: servant.CanUseNp ? ButtonStyle.Success : ButtonStyle.Secondary,
                                disabled: !servant.CanUseNp,
                                row: i
                            );
                        }
                    }
                    break;

                case EncounterType.Treasure:
                    cb.WithButton("??? 開啟寶藏", $"hgwt_treasure_{channelId}", ButtonStyle.Success);
                    break;

                case EncounterType.Shop:
                    cb.WithButton("?? 購買治療魔藥 (30金)", $"hgwt_shop_heal_{channelId}", ButtonStyle.Success, disabled: run.Gold < 30);
                    cb.WithButton("?? 告別商人，前進下一層", $"hgwt_shop_leave_{channelId}", ButtonStyle.Secondary);
                    break;

                case EncounterType.RestSite:
                    cb.WithButton("?? 紮營恢復生命 (全隊 HP+50%)", $"hgwt_rest_{channelId}", ButtonStyle.Success);
                    cb.WithButton("?? 全隊特訓練習 (全隊 ATK+10)", $"hgwt_train_{channelId}", ButtonStyle.Primary);
                    break;
            }

            return cb;
        }

        // ═══════════════════════════════════════════════════════════
        //  輔助初始化
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

                Console.WriteLine($"[HolyGrailTower] 初始英靈庫 {_servantPool.Count} 成功！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HolyGrailTower] LoadPool 失敗: {ex.Message}");
            }
        }

        private async Task<(string npName, string faceUrl)> FetchServantDetailsAsync(int collectionNo)
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

                string npName = data?.NoblePhantasms?
                    .Where(np => !string.IsNullOrWhiteSpace(np.Name))
                    .OrderByDescending(np => np.Num)
                    .Select(np => np.Name)
                    .FirstOrDefault();

                var result = (npName, faceUrl);
                _servantCache[collectionNo] = result;
                return result;
            }
            catch
            {
                return (null, null);
            }
        }

        private int RollRarity()
        {
            var roll = _rng.Next(100);
            return roll switch
            {
                < 1 => 5,    // 1% SSR
                < 4 => 4,    // 3% SR
                < 16 => 3,   // 12% R
                < 40 => 2,   // 24% UC
                _ => 1       // 60% C
            };
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

        private static Color GetEncounterColor(EncounterType type) => type switch
        {
            EncounterType.BossBattle => new Color(139, 0, 0),
            EncounterType.EliteBattle => new Color(255, 140, 0),
            EncounterType.NormalBattle => new Color(220, 20, 60),
            EncounterType.Shop => new Color(0, 191, 255),
            EncounterType.Treasure => new Color(255, 215, 0),
            EncounterType.RestSite => new Color(34, 139, 34),
            _ => new Color(128, 128, 128)
        };
    }
}