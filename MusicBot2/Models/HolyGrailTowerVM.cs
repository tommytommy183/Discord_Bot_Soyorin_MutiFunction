using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MusicBot2.Models
{
    /// <summary>聖杯塔 - 玩家資料（永久）</summary>
    public class HolyGrailTowerPlayer
    {
        [JsonPropertyName("userId")]
        public ulong UserId { get; set; }

        [JsonPropertyName("userName")]
        public string UserName { get; set; }

        [JsonPropertyName("summonTickets")]
        public int SummonTickets { get; set; } = 10; // 召喚券

        [JsonPropertyName("saintQuartz")]
        public int SaintQuartz { get; set; } = 0; // 聖晶石（premium貨幣）

        [JsonPropertyName("ownedServants")]
        public List<TowerServant> OwnedServants { get; set; } = new(); // 擁有的從者圖鑑

        [JsonPropertyName("highestFloor")]
        public int HighestFloor { get; set; } = 0; // 最高到達層數

        [JsonPropertyName("totalRuns")]
        public int TotalRuns { get; set; } = 0; // 總挑戰次數

        [JsonPropertyName("totalKills")]
        public int TotalKills { get; set; } = 0; // 總擊殺數

        [JsonPropertyName("lastDailyReward")]
        public DateTime? LastDailyReward { get; set; }

        [JsonPropertyName("permanentUpgrades")]
        public Dictionary<string, int> PermanentUpgrades { get; set; } = new(); // 永久升級

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>從者卡片（圖鑑）</summary>
    public class TowerServant
    {
        [JsonPropertyName("collectionNo")]
        public int CollectionNo { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("className")]
        public string ClassName { get; set; }

        [JsonPropertyName("rarity")]
        public int Rarity { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; } = 1;

        [JsonPropertyName("experience")]
        public int Experience { get; set; } = 0;

        [JsonPropertyName("skillLevel")]
        public int SkillLevel { get; set; } = 1;

        [JsonPropertyName("npLevel")]
        public int NpLevel { get; set; } = 1;

        [JsonPropertyName("timesUsed")]
        public int TimesUsed { get; set; } = 0;

        [JsonPropertyName("npName")]
        public string NpName { get; set; }

        [JsonPropertyName("npRuby")]
        public string NpRuby { get; set; }

        [JsonPropertyName("npCard")]
        public string NpCard { get; set; } // "buster", "arts", "quick"

        [JsonPropertyName("npTargetType")]
        public string NpTargetType { get; set; } // "enemy", "enemyAll"

        [JsonPropertyName("npDmgMultiplier")]
        public int NpDmgMultiplier { get; set; } = 600;

        [JsonPropertyName("npEffect")]
        public string NpEffect { get; set; }

        [JsonPropertyName("cards")]
        public List<string> Cards { get; set; } = new(); // 5張指令卡, 譬如 ["buster", "buster", "arts", "quick", "quick"]

        [JsonPropertyName("faceUrl")]
        public string FaceUrl { get; set; }

        [JsonPropertyName("fullImageUrl")]
        public string FullImageUrl { get; set; }

        [JsonPropertyName("obtainedAt")]
        public DateTime ObtainedAt { get; set; } = DateTime.UtcNow;

        // 基於等級與稀有度計算戰鬥數值
        public int GetMaxHp() => (1200 + Rarity * 400) + Level * 100;
        public int GetAttack() => (150 + Rarity * 50) + Level * 15;
        public int GetDefense() => (50 + Rarity * 15) + Level * 5;
    }

    /// <summary>單次爬塔 Run 狀態</summary>
    public class HgwTowerRun
    {
        public ulong ChannelId { get; set; }
        public ulong PlayerId { get; set; }
        public string PlayerName { get; set; }

        public int CurrentFloor { get; set; } = 1;
        public List<HgwTowerServantInstance> Team { get; set; } = new(); // 當前出戰隊伍（至多3人）

        public int Gold { get; set; } = 50; // 本次冒險獲得的金幣
        public List<HgwTowerRelic> Relics { get; set; } = new(); // 獲得的遺物

        public HgwTowerEncounter CurrentEncounter { get; set; } // 當前遭遇事件
        public List<string> EventLog { get; set; } = new(); // 遭遇日誌

        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public bool IsFinished { get; set; } = false;
    }

    /// <summary>冒險中的從者實體</summary>
    public class HgwTowerServantInstance
    {
        public int CollectionNo { get; set; }
        public string Name { get; set; }
        public string ClassName { get; set; }
        public int Rarity { get; set; }
        public int Level { get; set; }

        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }

        public int NpCharge { get; set; } = 0; // NP 計量：0 - 100
        public string NpName { get; set; }
        public string NpRuby { get; set; }
        public string NpCard { get; set; }
        public string NpTargetType { get; set; }
        public int NpDmgMultiplier { get; set; } = 600;
        public string NpEffect { get; set; }

        public List<string> Cards { get; set; } = new(); // 5張指令卡
        public string FaceUrl { get; set; }

        // 本次冒險增益 (Buffs)
        public int BonusAtk { get; set; } = 0;
        public int BonusDef { get; set; } = 0;
        public int BonusHp { get; set; } = 0;

        public bool IsAlive => CurrentHp > 0;

        public static HgwTowerServantInstance FromServant(TowerServant servant)
        {
            int maxHp = servant.GetMaxHp();
            return new HgwTowerServantInstance
            {
                CollectionNo = servant.CollectionNo,
                Name = servant.Name,
                ClassName = servant.ClassName,
                Rarity = servant.Rarity,
                Level = servant.Level,
                MaxHp = maxHp,
                CurrentHp = maxHp,
                Attack = servant.GetAttack(),
                Defense = servant.GetDefense(),
                NpName = servant.NpName,
                NpRuby = servant.NpRuby,
                NpCard = string.IsNullOrWhiteSpace(servant.NpCard) ? "buster" : servant.NpCard.ToLower(),
                NpTargetType = string.IsNullOrWhiteSpace(servant.NpTargetType) ? "enemy" : servant.NpTargetType,
                NpDmgMultiplier = servant.NpDmgMultiplier <= 0 ? 600 : servant.NpDmgMultiplier,
                NpEffect = servant.NpEffect,
                Cards = servant.Cards != null && servant.Cards.Count == 5 ? servant.Cards : new List<string> { "buster", "buster", "arts", "quick", "quick" },
                FaceUrl = servant.FaceUrl
            };
        }

        public void AddNpCharge(int amount) => NpCharge = Math.Min(100, NpCharge + amount);
        public void UseNp() => NpCharge = 0;
        public bool CanUseNp => NpCharge >= 100;
    }

    /// <summary>遭遇類型</summary>
    public enum EncounterType
    {
        NormalBattle,
        EliteBattle,
        BossBattle,
        Shop,
        Treasure,
        RestSite,
        Event
    }

    /// <summary>當前遭遇</summary>
    public class HgwTowerEncounter
    {
        public EncounterType Type { get; set; }
        public List<HgwTowerEnemy> Enemies { get; set; } = new();
        public int TurnCount { get; set; } = 1;
        
        // FGO 指令卡戰鬥專屬狀態
        public int CritStars { get; set; } = 0; // 上回合累積的暴擊星

        public List<HgwCardPlay> HandCards { get; set; } = new(); // 當前抽牌手牌 (5張)
        public List<HgwCardPlay> SelectedCards { get; set; } = new(); // 玩家已選中欲出手的牌

        public List<string> BattleLog { get; set; } = new();

        public HgwTowerEnemy GetCurrentEnemy() => Enemies.Find(e => e.IsAlive);
        public bool AllEnemiesDead() => Enemies.TrueForAll(e => !e.IsAlive);
    }

    /// <summary>敵對NPC怪物數值</summary>
    public class HgwTowerEnemy
    {
        public string Name { get; set; }
        public string ClassName { get; set; } // FGO 英靈職階
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public bool IsElite { get; set; }
        public bool IsBoss { get; set; }
        public List<string> Skills { get; set; } = new();

        public bool IsAlive => CurrentHp > 0;
    }

    /// <summary>FGO 指令卡打出行為</summary>
    public class HgwCardPlay
    {
        public int ServantIndex { get; set; } // 隊友索引：0, 1, 2
        public string ServantName { get; set; }
        public string CardType { get; set; } // "buster", "arts", "quick", "np"
        public int CardIndex { get; set; } // 在手牌 (0~4) 中的 index，如果是寶具卡則為 -1
        public int CritChance { get; set; } = 0; // 暴擊機率 (0 - 100)
    }

    /// <summary>遺物（永久增益）</summary>
    public class HgwTowerRelic
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string IconEmoji { get; set; }
        public RelicRarity Rarity { get; set; }
    }

    public enum RelicRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }

    /// <summary>樓層獎勵</summary>
    public class HgwFloorReward
    {
        public int Gold { get; set; }
        public int SummonTickets { get; set; }
        public int SaintQuartz { get; set; }
        public List<HgwTowerRelic> Relics { get; set; } = new();
    }

    /// <summary>永久升級項目</summary>
    public static class PermanentUpgrades
    {
        public const string MAX_TEAM_SIZE = "max_team_size";      // 最大隊伍人數 (默認3)
        public const string STARTING_GOLD = "starting_gold";      // 起始金幣
        public const string NP_GAIN = "np_gain";                  // NP 額外獲得百分比
        public const string HP_BOOST = "hp_boost";                // 起行血量額外加成
    }

    /// <summary>商店物品</summary>
    public class HgwShopItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Price { get; set; }
        public ShopItemType Type { get; set; }
        public string IconEmoji { get; set; }
    }

    public enum ShopItemType
    {
        Heal,
        Relic,
        UpgradeServant
    }

    /// <summary>職階相剋關係</summary>
    public static class ClassAdvantage
    {
        private static readonly Dictionary<string, List<string>> _advantages = new()
        {
            ["saber"] = new() { "lancer", "berserker" },
            ["archer"] = new() { "saber", "berserker" },
            ["lancer"] = new() { "archer", "berserker" },
            ["rider"] = new() { "caster", "berserker" },
            ["caster"] = new() { "assassin", "berserker" },
            ["assassin"] = new() { "rider", "berserker" },
            ["berserker"] = new() { "saber", "archer", "lancer", "rider", "caster", "assassin" },
            ["ruler"] = new() { "all" },
            ["avenger"] = new() { "ruler" },
            ["mooncancer"] = new() { "avenger" },
            ["alterego"] = new() { "foreigner", "saber", "archer", "lancer" },
            ["foreigner"] = new() { "berserker" },
            ["pretender"] = new() { "alterego" },
            ["shielder"] = new() { }
        };

        public static double GetMultiplier(string attackerClass, string defenderClass)
        {
            var attacker = attackerClass?.ToLower() ?? "";
            var defender = defenderClass?.ToLower() ?? "";

            if (_advantages.TryGetValue(attacker, out var advantageList))
            {
                if (advantageList.Contains("all") || advantageList.Contains(defender))
                    return 1.5;
            }

            if (_advantages.TryGetValue(defender, out var defenderAdvantageList))
            {
                if (defenderAdvantageList.Contains("all") || defenderAdvantageList.Contains(attacker))
                    return 0.67;
            }

            return 1.0;
        }
    }
}
