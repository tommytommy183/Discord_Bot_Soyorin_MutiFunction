using System;
using System.Collections.Generic;
using System.Linq;

namespace MusicBot2.Models
{
    /// <summary>
    /// TRPG 遊戲狀態
    /// </summary>
    public class TRPGGameState
    {
        public ulong ChannelId { get; set; }
        public ulong GameMasterId { get; set; }
        public DateTime StartTime { get; set; }
        public List<TRPGMessage> GameHistory { get; set; } = new();
        public Dictionary<ulong, TRPGCharacter> Characters { get; set; } = new();
        public bool IsActive { get; set; }
        public bool WaitingForDiceRoll { get; set; }
        public string? PendingDiceContext { get; set; }
        public ulong? WaitingPlayerId { get; set; }

        /// <summary>
        /// 通關目標
        /// </summary>
        public string ObjectiveDescription { get; set; } = string.Empty;

        /// <summary>
        /// 獲取或創建角色（需指定職業）
        /// </summary>
        public TRPGCharacter GetOrCreateCharacter(ulong userId, string userName, TRPGClass characterClass = TRPGClass.None)
        {
            if (!Characters.ContainsKey(userId))
            {
                var stats = GetBaseStats(characterClass);
                var maxHp = 80 + stats.Constitution * 2; // 基礎80 + 體質加成
                var maxSanity = 70 + stats.Wisdom * 2;   // 基礎70 + 感知加成
                Characters[userId] = new TRPGCharacter
                {
                    UserId = userId,
                    UserName = userName,
                    CharacterClass = characterClass,
                    Stats = stats,
                    ClassAbilities = GetClassAbilities(characterClass),
                    CurrentHP = maxHp,
                    MaxHP = maxHp,
                    Hunger = 100,
                    MaxHunger = 100,
                    Sanity = maxSanity,
                    MaxSanity = maxSanity
                };
            }
            return Characters[userId];
        }

        private static TRPGStats GetBaseStats(TRPGClass cls) => cls switch
        {
            TRPGClass.Warrior => new TRPGStats { Strength = 16, Dexterity = 12, Constitution = 14, Intelligence = 8, Wisdom = 10, Charisma = 10 },
            TRPGClass.Rogue => new TRPGStats { Strength = 10, Dexterity = 16, Constitution = 10, Intelligence = 12, Wisdom = 12, Charisma = 14 },
            TRPGClass.Mage => new TRPGStats { Strength = 8, Dexterity = 10, Constitution = 10, Intelligence = 16, Wisdom = 14, Charisma = 12 },
            TRPGClass.Cleric => new TRPGStats { Strength = 12, Dexterity = 8, Constitution = 14, Intelligence = 10, Wisdom = 16, Charisma = 12 },
            TRPGClass.Ranger => new TRPGStats { Strength = 12, Dexterity = 14, Constitution = 12, Intelligence = 10, Wisdom = 14, Charisma = 10 },
            _ => new TRPGStats { Strength = 10, Dexterity = 10, Constitution = 10, Intelligence = 10, Wisdom = 10, Charisma = 10 }
        };

        private static List<string> GetClassAbilities(TRPGClass cls) => cls switch
        {
            TRPGClass.Warrior => new List<string> { "重擊", "格擋", "戰吼", "近戰武器精通" },
            TRPGClass.Rogue => new List<string> { "潛行", "開鎖", "背刺", "偵測陷阱" },
            TRPGClass.Mage => new List<string> { "火球術", "冰霜護盾", "魔法偵測", "傳送術" },
            TRPGClass.Cleric => new List<string> { "治療術", "神聖護盾", "驅散不死", "祝福" },
            TRPGClass.Ranger => new List<string> { "追蹤", "動物溝通", "精準射擊", "野外求生" },
            _ => new List<string>()
        };

        public static string GetClassName(TRPGClass cls) => cls switch
        {
            TRPGClass.Warrior => "戰士",
            TRPGClass.Rogue => "盜賊",
            TRPGClass.Mage => "法師",
            TRPGClass.Cleric => "牧師",
            TRPGClass.Ranger => "遊俠",
            _ => "無職業"
        };

        public static TRPGClass ParseClass(string input) => input.Trim().ToLower() switch
        {
            "戰士" or "warrior" or "1" => TRPGClass.Warrior,
            "盜賊" or "rogue" or "2" => TRPGClass.Rogue,
            "法師" or "mage" or "3" => TRPGClass.Mage,
            "牧師" or "cleric" or "4" => TRPGClass.Cleric,
            "遊俠" or "ranger" or "5" => TRPGClass.Ranger,
            _ => TRPGClass.None
        };

        /// <summary>
        /// 獲取所有角色的狀態摘要
        /// </summary>
        public string GetCharactersStatusSummary()
        {
            if (Characters.Count == 0)
                return "目前沒有角色";

            var lines = Characters.Values.Select(c => 
                $"- {c.UserName} [{GetClassName(c.CharacterClass)}]: {c.CurrentHP}/{c.MaxHP} HP | 飢餓:{c.Hunger}/{c.MaxHunger} | SAN:{c.Sanity}/{c.MaxSanity} | {c.Stats} | 技能: {string.Join(", ", c.ClassAbilities)}");
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// TRPG 職業列表
    /// </summary>
    public enum TRPGClass
    {
        None = 0,
        Warrior,    // 戰士
        Rogue,      // 盜賊
        Mage,       // 法師
        Cleric,     // 牧師
        Ranger      // 遊俠
    }

    /// <summary>
    /// TRPG 角色數值
    /// </summary>
    public class TRPGStats
    {
        public int Strength { get; set; }     // 力量
        public int Dexterity { get; set; }    // 敏捷
        public int Constitution { get; set; } // 體質
        public int Intelligence { get; set; } // 智力
        public int Wisdom { get; set; }       // 感知
        public int Charisma { get; set; }     // 魅力

        public override string ToString() =>
            $"力量:{Strength} 敏捷:{Dexterity} 體質:{Constitution} 智力:{Intelligence} 感知:{Wisdom} 魅力:{Charisma}";
    }

    /// <summary>
    /// TRPG 角色資訊
    /// </summary>
    public class TRPGCharacter
    {
        public ulong UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public TRPGClass CharacterClass { get; set; } = TRPGClass.None;
        public TRPGStats Stats { get; set; } = new();
        public List<string> ClassAbilities { get; set; } = new();
        public int CurrentHP { get; set; } = 100;
        public int MaxHP { get; set; } = 100;
        public int Hunger { get; set; } = 100;       // 飢餓值 0-100，0=餓死
        public int MaxHunger { get; set; } = 100;
        public int Sanity { get; set; } = 100;       // SAN值 0-100，0=瘋狂
        public int MaxSanity { get; set; } = 100;
        public DateTime LastActionTime { get; set; } = DateTime.UtcNow;

        public List<InventoryItem> Inventory { get; set; } = new List<InventoryItem>();

        /// <summary>
        /// 受到傷害
        /// </summary>
        public void TakeDamage(int damage)
        {
            CurrentHP = Math.Max(0, CurrentHP - damage);
        }

        /// <summary>
        /// 恢復生命值
        /// </summary>
        public void Heal(int amount)
        {
            CurrentHP = Math.Min(MaxHP, CurrentHP + amount);
        }

        /// <summary>
        /// 減少飢餓值
        /// </summary>
        public void ReduceHunger(int amount)
        {
            Hunger = Math.Max(0, Hunger - amount);
        }

        /// <summary>
        /// 恢復飢餓值
        /// </summary>
        public void RestoreHunger(int amount)
        {
            Hunger = Math.Min(MaxHunger, Hunger + amount);
        }

        /// <summary>
        /// 減少SAN值
        /// </summary>
        public void ReduceSanity(int amount)
        {
            Sanity = Math.Max(0, Sanity - amount);
        }

        /// <summary>
        /// 恢復SAN值
        /// </summary>
        public void RestoreSanity(int amount)
        {
            Sanity = Math.Min(MaxSanity, Sanity + amount);
        }

        /// <summary>
        /// 是否存活
        /// </summary>
        public bool IsAlive => CurrentHP > 0 && Hunger > 0;

        /// <summary>
        /// 是否瘋狂
        /// </summary>
        public bool IsInsane => Sanity <= 0;

        /// <summary>
        /// 獲取健康狀態圖示
        /// </summary>
        public string GetHealthStatus()
        {
            var percentage = (double)CurrentHP / MaxHP;
            return percentage switch
            {
                >= 0.8 => "??",
                >= 0.5 => "??",
                >= 0.3 => "??",
                > 0 => "??",
                _ => "??"
            };
        }

        /// <summary>
        /// 獲取健康狀態描述
        /// </summary>
        public string GetHealthDescription()
        {
            var percentage = (double)CurrentHP / MaxHP;
            return percentage switch
            {
                >= 0.8 => "狀態良好",
                >= 0.5 => "輕傷",
                >= 0.3 => "中傷",
                > 0 => "重傷",
                _ => "已死亡"
            };
        }

        /// <summary>
        /// 添加物品到背包
        /// </summary>
        public void AddItem(string itemName, string description)
        {
            Inventory.Add(new InventoryItem
            {
                Name = itemName,
                Description = description,
                AcquiredTime = DateTime.UtcNow
            });
        }

        /// <summary>
        /// 從背包移除物品
        /// </summary>
        public bool RemoveItem(string itemName)
        {
            var item = Inventory.FirstOrDefault(i => i.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                Inventory.Remove(item);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 檢查是否擁有物品
        /// </summary>
        public bool HasItem(string itemName)
        {
            return Inventory.Any(i => i.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 獲取背包內容摘要
        /// </summary>
        public string GetInventorySummary()
        {
            if (Inventory.Count == 0)
                return "背包是空的";

            return string.Join("\n", Inventory.Select((item, index) => 
                $"{index + 1}. {item.Name} - {item.Description}"));
        }
    }

    /// <summary>
    /// 背包物品
    /// </summary>
    public class InventoryItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime AcquiredTime { get; set; }
    }

    /// <summary>
    /// TRPG 訊息記錄
    /// </summary>
    public class TRPGMessage
    {
        public ulong UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public TRPGMessageType Type { get; set; }
        public int? DiceResult { get; set; }
    }

    /// <summary>
    /// TRPG 訊息類型
    /// </summary>
    public enum TRPGMessageType
    {
        PlayerAction,    // 玩家行動
        DiceRoll,       // 骰子結果
        GMNarration,    // GM 旁白
        SystemMessage   // 系統訊息
    }

    /// <summary>
    /// OpenRouter TRPG 請求
    /// </summary>
    public class TRPGRequest
    {
        public string UserMessage { get; set; } = string.Empty;
        public List<TRPGMessage> GameHistory { get; set; } = new();
        public int? LastDiceRoll { get; set; }
    }
}
