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
        public int NpLevel { get; set; } = 1; // 寶具等級（抽到重複+1）

        [JsonPropertyName("timesUsed")]
        public int TimesUsed { get; set; } = 0;

        [JsonPropertyName("npName")]
        public string NpName { get; set; }

        [JsonPropertyName("faceUrl")]
        public string FaceUrl { get; set; }

        [JsonPropertyName("obtainedAt")]
        public DateTime ObtainedAt { get; set; } = DateTime.UtcNow;

        // 戰鬥屬性（基於稀有度和等級）
        public int GetMaxHp() => (600 + Rarity * 200) * Level;
        public int GetAttack() => (50 + Rarity * 20) * Level;
        public int GetDefense() => (30 + Rarity * 10) * Level;
    }

    /// <summary>單次爬塔 Run</summary>
    public class HgwTowerRun
    {
        public ulong ChannelId { get; set; }
        public ulong PlayerId { get; set; }
        public string PlayerName { get; set; }

        public int CurrentFloor { get; set; } = 1;
        public List<HgwTowerServantInstance> Team { get; set; } = new(); // 當前隊伍（最多3位）

        public int CurrentHp { get; set; } // 隊伍總血量
        public int MaxHp { get; set; }
        public int Gold { get; set; } = 50; // 本次 run 的金幣

        public List<HgwTowerRelic> Relics { get; set; } = new(); // 本次獲得的遺物
        public List<HgwTowerCard> Deck { get; set; } = new(); // 本次的卡組

        public HgwTowerEncounter CurrentEncounter { get; set; } // 當前遭遇
        public List<string> EventLog { get; set; } = new(); // 事件日誌

        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public bool IsFinished { get; set; } = false;
    }

    /// <summary>從者實例（本次 Run 中的）</summary>
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

        public int NpCharge { get; set; } = 0; // 0-100
        public string NpName { get; set; }
        public string FaceUrl { get; set; }

        // Run 內臨時增益
        public int BonusAtk { get; set; } = 0;
        public int BonusDef { get; set; } = 0;
        public int BonusHp { get; set; } = 0;

        public bool IsAlive => CurrentHp > 0;

        public static HgwTowerServantInstance FromServant(TowerServant servant)
        {
            return new HgwTowerServantInstance
            {
                CollectionNo = servant.CollectionNo,
                Name = servant.Name,
                ClassName = servant.ClassName,
                Rarity = servant.Rarity,
                Level = servant.Level,
                MaxHp = servant.GetMaxHp(),
                CurrentHp = servant.GetMaxHp(),
                Attack = servant.GetAttack(),
                Defense = servant.GetDefense(),
                NpName = servant.NpName,
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
        NormalBattle,   // 普通戰鬥
        EliteBattle,    // 精英戰鬥
        BossBattle,     // BOSS 戰
        Shop,           // 商店
        Treasure,       // 寶箱
        RestSite,       // 休息點
        Event           // 隨機事件
    }

    /// <summary>當前遭遇</summary>
    public class HgwTowerEncounter
    {
        public EncounterType Type { get; set; }
        public List<HgwTowerEnemy> Enemies { get; set; } = new();
        public int TurnCount { get; set; } = 1;
        public bool IsPlayerTurn { get; set; } = true;
        public int CurrentServantIndex { get; set; } = 0; // 當前行動的從者

        public HgwTowerEnemy GetCurrentEnemy() => Enemies.FirstOrDefault(e => e.IsAlive);
        public bool AllEnemiesDead() => Enemies.All(e => !e.IsAlive);
    }

    /// <summary>敵人</summary>
    public class HgwTowerEnemy
    {
        public string Name { get; set; }
        public string Type { get; set; } // "Skeleton", "Dragon", "Servant"
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public List<string> Skills { get; set; } = new(); // 特殊技能

        public bool IsElite { get; set; }
        public bool IsBoss { get; set; }

        public bool IsAlive => CurrentHp > 0;
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

    /// <summary>卡牌（技能卡）</summary>
    public class HgwTowerCard
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public CardType Type { get; set; }
        public int Cost { get; set; } = 1; // NP 消耗
        public int Damage { get; set; }
        public int Block { get; set; }
        public string Effect { get; set; }
    }

    public enum CardType
    {
        Attack,   // 攻擊卡
        Skill,    // 技能卡
        NP        // 寶具卡
    }

    /// <summary>樓層獎勵</summary>
    public class HgwFloorReward
    {
        public int Gold { get; set; }
        public int SummonTickets { get; set; }
        public int SaintQuartz { get; set; }
        public List<HgwTowerRelic> Relics { get; set; } = new();
        public int Experience { get; set; }
    }

    /// <summary>永久升級項目</summary>
    public static class PermanentUpgrades
    {
        public const string MAX_TEAM_SIZE = "max_team_size";      // 最大隊伍人數
        public const string STARTING_GOLD = "starting_gold";      // 起始金幣
        public const string STARTING_HP = "starting_hp";          // 起始血量加成
        public const string CARD_DRAW = "card_draw";              // 每回合抽牌數
        public const string NP_GAIN = "np_gain";                  // NP獲得量加成
        public const string SHOP_DISCOUNT = "shop_discount";      // 商店折扣
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
        Heal,           // 治療
        Relic,          // 遺物
        CardUpgrade,    // 卡牌升級
        RemoveCard      // 移除卡牌
    }
}
