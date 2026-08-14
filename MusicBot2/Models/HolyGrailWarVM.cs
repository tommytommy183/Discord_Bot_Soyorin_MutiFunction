using System.Text.Json.Serialization;

namespace MusicBot2.Models
{
    /// <summary>聖杯戰爭 - 玩家資料</summary>
    public class HgwPlayer
    {
        [JsonPropertyName("userId")]
        public ulong UserId { get; set; }

        [JsonPropertyName("userName")]
        public string UserName { get; set; }

        [JsonPropertyName("mana")]
        public int Mana { get; set; } = 100;

        [JsonPropertyName("commandSeals")]
        public int CommandSeals { get; set; } = 3;

        [JsonPropertyName("servants")]
        public List<HgwServant> Servants { get; set; } = new();

        [JsonPropertyName("activeServantId")]
        public int? ActiveServantId { get; set; }

        [JsonPropertyName("wins")]
        public int Wins { get; set; }

        [JsonPropertyName("losses")]
        public int Losses { get; set; }

        [JsonPropertyName("summonCount")]
        public int SummonCount { get; set; }

        [JsonPropertyName("lastDailyBonus")]
        public DateTime? LastDailyBonus { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>聖杯戰爭 - 從者實例（玩家擁有的）</summary>
    public class HgwServant
    {
        [JsonPropertyName("instanceId")]
        public int InstanceId { get; set; }

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
        public int Experience { get; set; }

        [JsonPropertyName("maxHp")]
        public int MaxHp { get; set; }

        [JsonPropertyName("currentHp")]
        public int CurrentHp { get; set; }

        [JsonPropertyName("attack")]
        public int Attack { get; set; }

        [JsonPropertyName("defense")]
        public int Defense { get; set; }

        [JsonPropertyName("critRate")]
        public int CritRate { get; set; } = 10;

        [JsonPropertyName("npCharge")]
        public int NpCharge { get; set; }

        [JsonPropertyName("npName")]
        public string NpName { get; set; }

        [JsonPropertyName("faceUrl")]
        public string FaceUrl { get; set; }

        [JsonPropertyName("fullImageUrl")]
        public string FullImageUrl { get; set; }

        [JsonPropertyName("obtainedAt")]
        public DateTime ObtainedAt { get; set; } = DateTime.UtcNow;

        public void InitializeStats()
        {
            var baseHp = Rarity switch
            {
                5 => 2000,
                4 => 1500,
                3 => 1200,
                2 => 1000,
                1 => 800,
                0 => 600,
                _ => 1000
            };
            var baseAtk = Rarity switch
            {
                5 => 200,
                4 => 150,
                3 => 120,
                2 => 100,
                1 => 80,
                0 => 60,
                _ => 100
            };

            MaxHp = baseHp + (Level - 1) * 50;
            CurrentHp = MaxHp;
            Attack = baseAtk + (Level - 1) * 5;
            Defense = 50 + (Level - 1) * 3;
        }

        public void Heal(int amount)
        {
            CurrentHp = Math.Min(CurrentHp + amount, MaxHp);
        }

        public void TakeDamage(int damage)
        {
            CurrentHp = Math.Max(0, CurrentHp - damage);
        }

        public bool IsAlive => CurrentHp > 0;

        public void AddNpCharge(int amount)
        {
            NpCharge = Math.Min(100, NpCharge + amount);
        }

        public bool CanUseNp => NpCharge >= 100;

        public void UseNp()
        {
            if (CanUseNp)
                NpCharge = 0;
        }
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

    /// <summary>戰鬥狀態</summary>
    public class HgwBattle
    {
        public ulong ChannelId { get; set; }
        public ulong Player1Id { get; set; }
        public ulong Player2Id { get; set; }
        public string Player1Name { get; set; }
        public string Player2Name { get; set; }
        public HgwServant Player1Servant { get; set; }
        public HgwServant Player2Servant { get; set; }
        public bool IsPlayer1Turn { get; set; } = true;
        public int TurnCount { get; set; } = 1;
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public List<string> BattleLog { get; set; } = new();
        public bool IsVsNpc { get; set; }

        public HgwServant GetCurrentAttacker() => IsPlayer1Turn ? Player1Servant : Player2Servant;
        public HgwServant GetCurrentDefender() => IsPlayer1Turn ? Player2Servant : Player1Servant;
        public ulong GetCurrentPlayerId() => IsPlayer1Turn ? Player1Id : Player2Id;
        public string GetCurrentPlayerName() => IsPlayer1Turn ? Player1Name : Player2Name;

        public void NextTurn()
        {
            IsPlayer1Turn = !IsPlayer1Turn;
            if (IsPlayer1Turn)
                TurnCount++;
        }
    }

    /// <summary>召喚結果</summary>
    public class SummonResult
    {
        public HgwServant Servant { get; set; }
        public bool IsNew { get; set; }
        public string Message { get; set; }
    }

    /// <summary>戰鬥行動類型</summary>
    public enum BattleAction
    {
        Attack,
        Skill,
        NoblePhantasm,
        Defend,
        UseCommandSeal
    }

    /// <summary>戰鬥結果</summary>
    public class HgwBattleResult
    {
        public bool IsFinished { get; set; }
        public ulong? WinnerId { get; set; }
        public string WinnerName { get; set; }
        public int Damage { get; set; }
        public bool IsCritical { get; set; }
        public bool UsedNoblePhantasm { get; set; }
        public string ActionDescription { get; set; }
    }
}
