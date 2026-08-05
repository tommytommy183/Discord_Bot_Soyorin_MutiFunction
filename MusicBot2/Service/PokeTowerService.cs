using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Discord;
using MusicBot2.Models;
using StackExchange.Redis;

namespace MusicBot2.Service
{
    public enum TowerRunState
    {
        SelectingPath,
        InBattle,
        Shopping,
        SelectingEvent,
        SelectingMoveReward,
        SelectingMoveSlot,
        SelectingCatch,
        SelectingCatchSwap,
        Victory,
        Defeated,
        Resting,
        SelectingPowerUpgrade,
        SelectingRelic,
        InCasino,
        SelectingPassive,
        SelectingCursedRelic,
        InMiniGame2048,
        InMiniGameMine,
        InMiniGameQuiz,
    }

    public class TowerMove
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int Power { get; set; }
        public string Category { get; set; }
        public string Emoji { get; set; } = "⚡";
        public int MaxPP { get; set; } = 10;
        public int CurrentPP { get; set; } = 10;
        public int UpgradeCount { get; set; } = 0;
        // ── 技能效果 ──────────────────────────────────────────────
        /// <summary>附加狀態："burn"|"para"|"freeze"|"sleep"|"poison"|"flinch"</summary>
        public string EffectAilment { get; set; } = "";
        /// <summary>附加機率 0-100（0 = 必定觸發，但只在 StatTarget 時用）</summary>
        public int EffectChance { get; set; } = 0;
        /// <summary>吸血/後座力：正=吸傷害X%，負=自損傷害X%</summary>
        public int DrainPercent { get; set; } = 0;
        /// <summary>能力目標："foe_atk"|"foe_def"|"foe_spd"|"foe_spatk"|"foe_spdef"
        ///              或 "self_atk"|"self_def"|"self_spd"|"self_spatk"</summary>
        public string StatTarget { get; set; } = "";
        /// <summary>能力變化段數（-2 ~ +2）</summary>
        public int StatStageChange { get; set; } = 0;
        /// <summary>高暴擊率（命中率提升）</summary>
        public bool HighCrit { get; set; } = false;
        /// <summary>連打最少次數</summary>
        public int MinHits { get; set; } = 1;
        /// <summary>連打最多次數</summary>
        public int MaxHits { get; set; } = 1;
    }

    public class TowerPokemon
    {
        public int PokeId { get; set; }
        public string Name { get; set; }
        public string? CustomName { get; set; }
        public List<string> Types { get; set; } = new();
        public int MaxHP { get; set; }
        public int CurrentHP { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int SpecialAttack { get; set; }
        public int SpecialDefense { get; set; }
        public int Speed { get; set; }
        public List<TowerMove> Moves { get; set; } = new();
        public bool IsShiny { get; set; }
        public DateTime CaughtAt { get; set; }
        public string? ImageUrl { get; set; }
        public string? BackImageUrl { get; set; }
        // ── 戰鬥狀態（每場戰鬥開始重置）────────────────────────────
        public string BattleStatus { get; set; } = "";   // "burn","para","freeze","sleep","poison"
        public int SleepTurns { get; set; } = 0;
        public int AtkStage { get; set; } = 0;
        public int DefStage { get; set; } = 0;
        public int SpdStage { get; set; } = 0;
        public int SpAtkStage { get; set; } = 0;
        public int SpDefStage { get; set; } = 0;

        [JsonIgnore]
        public string DisplayName => !string.IsNullOrWhiteSpace(CustomName) ? CustomName : Name;
    }

    public class TowerEnemy
    {
        public string Name { get; set; }
        public int PokeId { get; set; }
        public List<string> Types { get; set; } = new();
        public int MaxHP { get; set; }
        public int CurrentHP { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int SpecialAttack { get; set; }
        public int SpecialDefense { get; set; }
        public int Speed { get; set; }
        public List<TowerMove> Moves { get; set; } = new();
        public int NextMoveIdx { get; set; }
        public bool IsBoss { get; set; }
        public int GoldReward { get; set; }
        // ── 戰鬥狀態（每場戰鬥開始重置）────────────────────────────
        public string BattleStatus { get; set; } = "";
        public int SleepTurns { get; set; } = 0;
        public int AtkStage { get; set; } = 0;
        public int DefStage { get; set; } = 0;
        public int SpdStage { get; set; } = 0;
        public int SpAtkStage { get; set; } = 0;
        public int SpDefStage { get; set; } = 0;
        public bool Flinched { get; set; } = false;

        [JsonIgnore]
        public string FrontGifUrl => PokeId > 0
            ? $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/showdown/{PokeId}.gif"
            : null;
        [JsonIgnore]
        public string FrontFallbackUrl => PokeId > 0
            ? $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/official-artwork/{PokeId}.png"
            : null;
    }

    public class TowerRun
    {
        public ulong PlayerId { get; set; }
        public string PlayerName { get; set; }
        public ulong ChannelId { get; set; }
        public int CurrentFloor { get; set; } = 0;
        public int MaxFloor { get; set; } = 20;
        public TowerPokemon ActivePokemon { get; set; }
        public List<TowerPokemon> Party { get; set; } = new();
        public TowerEnemy CurrentEnemy { get; set; }
        public TowerRunState State { get; set; } = TowerRunState.SelectingPath;
        public List<string> RunLog { get; set; } = new();
        public string CurrentBattleLog { get; set; } = "";
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public int TotalDamageDealt { get; set; }
        public int Gold { get; set; } = 10;
        public List<TowerMove> PendingMoveRewards { get; set; } = new();
        public TowerMove PendingSelectedMove { get; set; }
        public TowerEnemy PendingCatch { get; set; }
        public ulong EnemyImgMsgId { get; set; }
        public ulong PlayerImgMsgId { get; set; }
        // 球庫：key = "normal"/"super"/"ultra"/"master"，value = 數量
        public Dictionary<string, int> Balls { get; set; } = new() { ["normal"] = 10 };
        public int PendingEventIdx { get; set; } = -1;
        public HashSet<int> UsedEventIndices { get; set; } = new();
        public int MathCorrectChoice { get; set; } = -1;
        public string MathProblemText { get; set; } = "";
        public List<string> MathChoiceLabels { get; set; } = new();
        public int Level { get; set; } = 1;
        public int Exp { get; set; } = 0;
        public int ExpToNext => Level * 60;
        public List<string> CurrentPaths { get; set; } = new();
        public bool RestMoveRewardPending { get; set; } = false;
        public bool ShopMoveRewardPending { get; set; } = false;
        public bool EventMoveRewardPending { get; set; } = false;
        // "battle" | "rest" | "shop"
        public string PowerUpgradeReturn { get; set; } = "";
        // 商店各商品已購買次數（每買一次該商品+10💰）
        public Dictionary<string, int> ShopBuyCounts { get; set; } = new();
        public List<string> RelicIds { get; set; } = new();
        public HashSet<string> SeenRelicIds { get; set; } = new();
        public List<string> PendingRelicChoices { get; set; } = new();
        public bool ShieldActive { get; set; } = false;
        public int PhoenixUseCount { get; set; } = 0;
        public int AvengeStacks { get; set; } = 0;
        public bool WillUsed { get; set; } = false;
        public float ChainBonus { get; set; } = 0f;
        // Casino
        public int CasinoRound { get; set; } = 0;
        public int CasinoProfit { get; set; } = 0;
        public int CasinoBet { get; set; } = 0;   // 本局下注金額（0=未下注）
        // Mini-game: 2048 (16 ints, row-major; 0=empty)
        public List<int> MiniGame2048Board { get; set; } = new();
        public int MiniGame2048MovesLeft { get; set; } = 0;
        public int MiniGame2048Reward { get; set; } = 0;
        // Mini-game: Minesweeper (9 ints: -1=mine, 0-8=nearby mines)
        public List<int> MiniGameMineBoard { get; set; } = new();
        public List<bool> MiniGameMineRevealed { get; set; } = new();
        public int MiniGameMineSafeLeft { get; set; } = 0;   // 還需要安全踩幾步
        public int MiniGameMineReward { get; set; } = 0;
        // Mini-game: Quiz (champion / word)
        public string MiniGameQuizQuestion { get; set; } = "";
        public List<string> MiniGameQuizChoices { get; set; } = new();  // display labels
        public int MiniGameQuizAnswerIdx { get; set; } = 0;
        public int MiniGameQuizReward { get; set; } = 0;
        // Passive
        public string PassiveId { get; set; } = "";
        // Cursed Relics
        public List<string> CursedRelicIds { get; set; } = new();
        public List<string> PendingCursedRelicChoices { get; set; } = new();
    }

    public class PokeTowerService
    {
        private readonly IDatabase _redisDb;
        private readonly bool _useRedis;
        private readonly Dictionary<ulong, TowerRun> _activeRuns = new();
        private const string REDIS_PREFIX = "tower:run:";
        private const string SHINY_KEY_PREFIX = "tower:shiny:";
        // in-memory fallback for shiny reward (also written to Redis for persistence)
        internal static readonly HashSet<ulong> PendingShinyUserIds = new();
        private static readonly Random _rng = new();
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
        private static readonly Dictionary<int, List<TowerMove>> _movesApiCache = new();
        private static readonly Dictionary<string, string> _typeEnToZh = new(StringComparer.OrdinalIgnoreCase)
        {
            ["normal"]="一般", ["fire"]="火", ["water"]="水", ["electric"]="電",
            ["grass"]="草", ["ice"]="冰", ["fighting"]="格鬥", ["poison"]="毒",
            ["ground"]="地面", ["flying"]="飛行", ["psychic"]="超能力", ["bug"]="蟲",
            ["rock"]="岩石", ["ghost"]="幽靈", ["dragon"]="龍", ["dark"]="惡",
            ["steel"]="鋼", ["fairy"]="妖精"
        };

        #region 靜態資料表（球種 / 技能池 / 敵人池 / 屬性相剋）
        // ── 球種設定 ───────────────────────────────────────────
        private static readonly Dictionary<string, (string DisplayName, string Emoji, float Rate)> _balls = new()
        {
            ["normal"] = ("普通球", "⚽", 0.30f),
            ["super"]  = ("超級球", "🔵", 0.55f),
            ["ultra"]  = ("高級球", "🟡", 0.75f),
            ["master"] = ("大師球", "🟣", 1.00f),
        };

        private static readonly Dictionary<string, string> _typeEmoji = new(StringComparer.OrdinalIgnoreCase)
        {
            ["一般"] = "⬜", ["火"] = "🔥", ["水"] = "💧", ["電"] = "⚡",
            ["草"] = "🌿", ["冰"] = "❄️", ["格鬥"] = "🥊", ["毒"] = "☠️",
            ["地面"] = "🏔️", ["飛行"] = "🌪️", ["超能力"] = "🔮", ["蟲"] = "🐛",
            ["岩石"] = "🪨", ["幽靈"] = "👻", ["龍"] = "🐉", ["惡"] = "🌑",
            ["鋼"] = "⚙️", ["妖精"] = "🌸",
            // English fallbacks (in case enemy types are English)
            ["Normal"] = "⬜", ["Fire"] = "🔥", ["Water"] = "💧", ["Electric"] = "⚡",
            ["Grass"] = "🌿", ["Ice"] = "❄️", ["Fighting"] = "🥊", ["Poison"] = "☠️",
            ["Ground"] = "🏔️", ["Flying"] = "🌪️", ["Psychic"] = "🔮", ["Bug"] = "🐛",
            ["Rock"] = "🪨", ["Ghost"] = "👻", ["Dragon"] = "🐉", ["Dark"] = "🌑",
            ["Steel"] = "⚙️", ["Fairy"] = "🌸"
        };

        // ── 中文技能池 ──────────────────────────────────────────
        private static readonly List<TowerMove> _movePool = new()
        {
            // 一般
            new() { Name="身體猛撞",   Type="一般",   Power=85,  Category="Physical", Emoji="💪", MaxPP=10 },
            new() { Name="破壞死光",   Type="一般",   Power=120, Category="Special",  Emoji="💥", MaxPP=5  },
            new() { Name="高速移動",   Type="一般",   Power=40,  Category="Physical", Emoji="💨", MaxPP=15 },
            new() { Name="劈斬",       Type="一般",   Power=70,  Category="Physical", Emoji="🗡️", MaxPP=10 },
            new() { Name="三重攻擊",   Type="一般",   Power=80,  Category="Special",  Emoji="🔺", MaxPP=10 },
            // 火
            new() { Name="火焰放射",   Type="火",     Power=90,  Category="Special",  Emoji="🔥", MaxPP=10 },
            new() { Name="大字爆",     Type="火",     Power=110, Category="Special",  Emoji="🔥", MaxPP=5  },
            new() { Name="火焰輪",     Type="火",     Power=60,  Category="Physical", Emoji="🔥", MaxPP=15 },
            new() { Name="熱風",       Type="火",     Power=95,  Category="Special",  Emoji="🌡️", MaxPP=10 },
            new() { Name="火焰齒",     Type="火",     Power=65,  Category="Physical", Emoji="🦷", MaxPP=15 },
            // 水
            new() { Name="水槍",       Type="水",     Power=40,  Category="Special",  Emoji="💧", MaxPP=15 },
            new() { Name="水流衝擊",   Type="水",     Power=110, Category="Special",  Emoji="🌊", MaxPP=5  },
            new() { Name="衝浪",       Type="水",     Power=90,  Category="Special",  Emoji="🏄", MaxPP=10 },
            new() { Name="水尾",       Type="水",     Power=90,  Category="Physical", Emoji="🐟", MaxPP=10 },
            new() { Name="沸水",       Type="水",     Power=80,  Category="Special",  Emoji="♨️", MaxPP=10 },
            // 電
            new() { Name="十萬伏特",   Type="電",     Power=90,  Category="Special",  Emoji="⚡", MaxPP=10 },
            new() { Name="打雷",       Type="電",     Power=110, Category="Special",  Emoji="🌩️", MaxPP=5  },
            new() { Name="雷電拳",     Type="電",     Power=75,  Category="Physical", Emoji="⚡", MaxPP=15 },
            new() { Name="野蠻電力",   Type="電",     Power=90,  Category="Physical", Emoji="⚡", MaxPP=10 },
            new() { Name="伏特替換",   Type="電",     Power=70,  Category="Special",  Emoji="🔌", MaxPP=10 },
            // 草
            new() { Name="剃刀葉",     Type="草",     Power=55,  Category="Physical", Emoji="🍃", MaxPP=15 },
            new() { Name="太陽光線",   Type="草",     Power=120, Category="Special",  Emoji="☀️", MaxPP=5  },
            new() { Name="葉片風暴",   Type="草",     Power=130, Category="Special",  Emoji="🌿", MaxPP=5  },
            new() { Name="種子炸彈",   Type="草",     Power=80,  Category="Physical", Emoji="💣", MaxPP=10 },
            new() { Name="能量球",     Type="草",     Power=90,  Category="Special",  Emoji="🟢", MaxPP=10 },
            // 冰
            new() { Name="冰凍光線",   Type="冰",     Power=90,  Category="Special",  Emoji="❄️", MaxPP=10 },
            new() { Name="暴風雪",     Type="冰",     Power=110, Category="Special",  Emoji="🌨️", MaxPP=5  },
            new() { Name="冰拳",       Type="冰",     Power=75,  Category="Physical", Emoji="❄️", MaxPP=15 },
            new() { Name="冰柱墜落",   Type="冰",     Power=85,  Category="Physical", Emoji="🧊", MaxPP=10 },
            new() { Name="冰凍乾燥",   Type="冰",     Power=70,  Category="Special",  Emoji="🥶", MaxPP=10 },
            // 格鬥
            new() { Name="近身格鬥",   Type="格鬥",   Power=120, Category="Physical", Emoji="🥊", MaxPP=5  },
            new() { Name="破壞磚塊",   Type="格鬥",   Power=75,  Category="Physical", Emoji="🧱", MaxPP=15 },
            new() { Name="波導彈",     Type="格鬥",   Power=80,  Category="Special",  Emoji="🔵", MaxPP=10 },
            new() { Name="超強力",     Type="格鬥",   Power=120, Category="Physical", Emoji="💪", MaxPP=5  },
            new() { Name="剪刀十字",   Type="格鬥",   Power=100, Category="Physical", Emoji="✂️", MaxPP=10 },
            // 超能力
            new() { Name="精神力",     Type="超能力", Power=90,  Category="Special",  Emoji="🔮", MaxPP=10 },
            new() { Name="念力射線",   Type="超能力", Power=65,  Category="Special",  Emoji="🌀", MaxPP=15 },
            new() { Name="精神切割",   Type="超能力", Power=70,  Category="Physical", Emoji="🔮", MaxPP=10 },
            new() { Name="禪宗頭槌",   Type="超能力", Power=80,  Category="Physical", Emoji="💫", MaxPP=10 },
            // 龍
            new() { Name="龍爪",       Type="龍",     Power=80,  Category="Physical", Emoji="🐉", MaxPP=15 },
            new() { Name="逆鱗",       Type="龍",     Power=120, Category="Physical", Emoji="😡", MaxPP=5  },
            new() { Name="龍波動彈",   Type="龍",     Power=85,  Category="Special",  Emoji="🐉", MaxPP=10 },
            new() { Name="龍之隕石",   Type="龍",     Power=130, Category="Special",  Emoji="☄️", MaxPP=5  },
            // 惡
            new() { Name="咬碎",       Type="惡",     Power=80,  Category="Physical", Emoji="🌑", MaxPP=15 },
            new() { Name="惡波",       Type="惡",     Power=80,  Category="Special",  Emoji="🌑", MaxPP=10 },
            new() { Name="夜斬",       Type="惡",     Power=70,  Category="Physical", Emoji="🌙", MaxPP=15 },
            new() { Name="奇襲",       Type="惡",     Power=70,  Category="Physical", Emoji="👊", MaxPP=10 },
            // 幽靈
            new() { Name="影子球",     Type="幽靈",   Power=80,  Category="Special",  Emoji="👻", MaxPP=10 },
            new() { Name="影爪",       Type="幽靈",   Power=70,  Category="Physical", Emoji="👻", MaxPP=15 },
            new() { Name="幻影突擊",   Type="幽靈",   Power=90,  Category="Physical", Emoji="👻", MaxPP=10 },
            new() { Name="惡詛",       Type="幽靈",   Power=65,  Category="Special",  Emoji="🔱", MaxPP=10 },
            // 岩石
            new() { Name="岩石滑落",   Type="岩石",   Power=75,  Category="Physical", Emoji="🪨", MaxPP=10 },
            new() { Name="尖石攻擊",   Type="岩石",   Power=100, Category="Physical", Emoji="🪨", MaxPP=5  },
            new() { Name="寶石能量炮", Type="岩石",   Power=80,  Category="Special",  Emoji="💎", MaxPP=10 },
            // 地面
            new() { Name="地震",       Type="地面",   Power=100, Category="Physical", Emoji="🌍", MaxPP=10 },
            new() { Name="大地之力",   Type="地面",   Power=90,  Category="Special",  Emoji="🌏", MaxPP=10 },
            new() { Name="挖洞",       Type="地面",   Power=80,  Category="Physical", Emoji="⛏️", MaxPP=10 },
            // 飛行
            new() { Name="劈空斬",     Type="飛行",   Power=75,  Category="Special",  Emoji="🌬️", MaxPP=15 },
            new() { Name="勇鳥急衝",   Type="飛行",   Power=120, Category="Physical", Emoji="🦅", MaxPP=5  },
            new() { Name="颶風",       Type="飛行",   Power=110, Category="Special",  Emoji="🌀", MaxPP=5  },
            new() { Name="空中斬",     Type="飛行",   Power=60,  Category="Physical", Emoji="✈️", MaxPP=15 },
            // 蟲
            new() { Name="X剪刀",      Type="蟲",     Power=80,  Category="Physical", Emoji="✂️", MaxPP=15 },
            new() { Name="蟲鳴",       Type="蟲",     Power=90,  Category="Special",  Emoji="🐝", MaxPP=10 },
            new() { Name="U形回轉",    Type="蟲",     Power=70,  Category="Physical", Emoji="🔄", MaxPP=10 },
            // 毒
            new() { Name="毒菌炸彈",   Type="毒",     Power=90,  Category="Special",  Emoji="☠️", MaxPP=10 },
            new() { Name="毒刺",       Type="毒",     Power=80,  Category="Physical", Emoji="💉", MaxPP=10 },
            new() { Name="骯臟射擊",   Type="毒",     Power=120, Category="Physical", Emoji="🗑️", MaxPP=5  },
            // 鋼
            new() { Name="鐵頭",       Type="鋼",     Power=80,  Category="Physical", Emoji="⚙️", MaxPP=15 },
            new() { Name="閃光炮",     Type="鋼",     Power=80,  Category="Special",  Emoji="💡", MaxPP=10 },
            new() { Name="隕石衝",     Type="鋼",     Power=90,  Category="Physical", Emoji="🌠", MaxPP=10 },
            new() { Name="鋼翼",       Type="鋼",     Power=70,  Category="Physical", Emoji="🪽",  MaxPP=15 },
            new() { Name="磁鐵炸彈",   Type="鋼",     Power=60,  Category="Physical", Emoji="🧲", MaxPP=20 },
            new() { Name="子彈拳",     Type="鋼",     Power=40,  Category="Physical", Emoji="🔩", MaxPP=20 },
            // 妖精
            new() { Name="月亮之力",   Type="妖精",   Power=95,  Category="Special",  Emoji="🌸", MaxPP=10 },
            new() { Name="粗野播弄",   Type="妖精",   Power=90,  Category="Physical", Emoji="🎀", MaxPP=10 },
            new() { Name="耀眼魅力",   Type="妖精",   Power=80,  Category="Special",  Emoji="✨", MaxPP=10 },
            new() { Name="夢幻接觸",   Type="妖精",   Power=90,  Category="Physical", Emoji="🌷", MaxPP=10 },
            new() { Name="少女之吻",   Type="妖精",   Power=40,  Category="Special",  Emoji="💋", MaxPP=20 },
            new() { Name="迷惑射線",   Type="妖精",   Power=80,  Category="Special",  Emoji="🌈", MaxPP=10 },
            // ── 補充：每屬性再擴充 ──────────────────────────────────
            // 一般
            new() { Name="快速攻擊",   Type="一般",   Power=40,  Category="Physical", Emoji="💨", MaxPP=20 },
            new() { Name="歸還",       Type="一般",   Power=102, Category="Physical", Emoji="🤝", MaxPP=15 },
            new() { Name="鬼火擊",     Type="一般",   Power=70,  Category="Physical", Emoji="🔥", MaxPP=15 },
            // 火
            new() { Name="熾焰決戰",   Type="火",     Power=130, Category="Special",  Emoji="🌋", MaxPP=5  },
            new() { Name="旭日一擊",   Type="火",     Power=120, Category="Physical", Emoji="☀️", MaxPP=5  },
            new() { Name="火炎旋渦",   Type="火",     Power=35,  Category="Special",  Emoji="🌀", MaxPP=15 },
            // 水
            new() { Name="泡沫光線",   Type="水",     Power=65,  Category="Special",  Emoji="🫧", MaxPP=15 },
            new() { Name="潛水",       Type="水",     Power=80,  Category="Physical", Emoji="🤿", MaxPP=10 },
            new() { Name="急流",       Type="水",     Power=40,  Category="Physical", Emoji="💦", MaxPP=20 },
            // 電
            new() { Name="放電",       Type="電",     Power=80,  Category="Special",  Emoji="🌩️", MaxPP=15 },
            new() { Name="電磁炮",     Type="電",     Power=120, Category="Special",  Emoji="🔫", MaxPP=5  },
            new() { Name="電網",       Type="電",     Power=55,  Category="Special",  Emoji="🕸️", MaxPP=15 },
            // 草
            new() { Name="奇異植物",   Type="草",     Power=75,  Category="Special",  Emoji="🌱", MaxPP=10 },
            new() { Name="花瓣舞",     Type="草",     Power=120, Category="Special",  Emoji="🌺", MaxPP=10 },
            new() { Name="草結",       Type="草",     Power=80,  Category="Special",  Emoji="🌿", MaxPP=20 },
            // 冰
            new() { Name="霧化",       Type="冰",     Power=55,  Category="Special",  Emoji="🌫️", MaxPP=15 },
            new() { Name="極寒之地",   Type="冰",     Power=70,  Category="Special",  Emoji="🥶", MaxPP=10 },
            new() { Name="冰封世界",   Type="冰",     Power=95,  Category="Special",  Emoji="🌨️", MaxPP=10 },
            // 格鬥
            new() { Name="腦力衝擊",   Type="格鬥",   Power=120, Category="Special",  Emoji="🧠", MaxPP=5  },
            new() { Name="真空波",     Type="格鬥",   Power=40,  Category="Special",  Emoji="🌬️", MaxPP=20 },
            new() { Name="旋風踢",     Type="格鬥",   Power=85,  Category="Physical", Emoji="🦵", MaxPP=10 },
            // 超能力
            new() { Name="未來預知",   Type="超能力", Power=120, Category="Special",  Emoji="🔭", MaxPP=10 },
            new() { Name="意念頭槌",   Type="超能力", Power=80,  Category="Special",  Emoji="🧿", MaxPP=10 },
            new() { Name="夢幻之吻",   Type="超能力", Power=100, Category="Special",  Emoji="💭", MaxPP=10 },
            // 龍
            new() { Name="龍尾",       Type="龍",     Power=60,  Category="Physical", Emoji="🐲", MaxPP=10 },
            new() { Name="神秘龍脈",   Type="龍",     Power=130, Category="Special",  Emoji="🌌", MaxPP=5  },
            new() { Name="龍之怒",     Type="龍",     Power=100, Category="Special",  Emoji="🔴", MaxPP=10 },
            // 惡
            new() { Name="奸詐拳",     Type="惡",     Power=95,  Category="Physical", Emoji="🤛", MaxPP=15 },
            new() { Name="追打",       Type="惡",     Power=40,  Category="Physical", Emoji="👣", MaxPP=20 },
            new() { Name="橫掃千軍",   Type="惡",     Power=70,  Category="Physical", Emoji="⚔️", MaxPP=15 },
            // 幽靈
            new() { Name="幽冥爆破",   Type="幽靈",   Power=110, Category="Physical", Emoji="💣", MaxPP=5  },
            new() { Name="惡魔之吻",   Type="幽靈",   Power=65,  Category="Special",  Emoji="😈", MaxPP=15 },
            new() { Name="影子偷襲",   Type="幽靈",   Power=40,  Category="Physical", Emoji="🌑", MaxPP=20 },
            // 岩石
            new() { Name="古代力量",   Type="岩石",   Power=60,  Category="Special",  Emoji="⛰️", MaxPP=5  },
            new() { Name="猛岩炮彈",   Type="岩石",   Power=100, Category="Physical", Emoji="🪨", MaxPP=5  },
            new() { Name="礫石衝",     Type="岩石",   Power=50,  Category="Physical", Emoji="💢", MaxPP=20 },
            // 地面
            new() { Name="土撥球",     Type="地面",   Power=55,  Category="Special",  Emoji="🟤", MaxPP=15 },
            new() { Name="礫石擊",     Type="地面",   Power=60,  Category="Physical", Emoji="🌏", MaxPP=20 },
            new() { Name="流沙地獄",   Type="地面",   Power=35,  Category="Physical", Emoji="⌛", MaxPP=15 },
            // 飛行
            new() { Name="翅膀攻擊",   Type="飛行",   Power=60,  Category="Physical", Emoji="🪶", MaxPP=20 },
            new() { Name="燕返",       Type="飛行",   Power=60,  Category="Physical", Emoji="🐦", MaxPP=20 },
            new() { Name="空氣切割",   Type="飛行",   Power=75,  Category="Special",  Emoji="✈️", MaxPP=15 },
            // 蟲
            new() { Name="蟲爆",       Type="蟲",     Power=90,  Category="Special",  Emoji="🐛", MaxPP=10 },
            new() { Name="信號束",     Type="蟲",     Power=75,  Category="Special",  Emoji="📡", MaxPP=15 },
            new() { Name="蟲咬",       Type="蟲",     Power=60,  Category="Physical", Emoji="🐝", MaxPP=20 },
            // 毒
            new() { Name="毒素衝擊",   Type="毒",     Power=120, Category="Physical", Emoji="💀", MaxPP=5  },
            new() { Name="酸液",       Type="毒",     Power=40,  Category="Special",  Emoji="🧪", MaxPP=20 },
            new() { Name="污泥炸彈",   Type="毒",     Power=90,  Category="Special",  Emoji="💚", MaxPP=10 },
        };

        private static readonly List<RelicDef> _relics = new()
        {
            // Immediate stat boosts
            new("relic_atk_up",    "純純的數值",  "💪", "全隊攻擊力+20%"),
            new("relic_def_up",    "硬啦",  "🛡️", "全隊防禦力+20%"),
            new("relic_hp_up",     "坦克引擎",  "💎", "全隊最大HP+25%"),
            new("relic_move_pow",  "全ap跟你爆搂",  "👁️", "全部技能威力+20"),
            new("relic_move_pp",   "魔力水晶",  "💧", "全部技能MaxPP+5並回滿"),
            new("relic_all_stats", "X項之力",  "🎺", "全隊所有能力+15%"),
            new("relic_gold",      "我就愛錢",  "💰", "立即獲得80金幣"),
            new("relic_exp",       "爆考研究所",  "📚", "立即獲得大量EXP（Level×120）"),
            // Passive combat
            new("relic_lifesteal", "嗜血者",  "🧛", "攻擊回復傷害的20%HP"),
            new("relic_thorns",    "你是甲我反甲",  "🌵", "受傷時反彈傷害的25%（最多25）"),
            new("relic_crit",      "賭你不敢",    "💥", "攻擊15%機率造成雙倍傷害"),
            new("relic_poison",    "毒牙",      "☠️", "每次攻擊額外造成15固定傷害"),
            new("relic_no_pp",     "永動機",    "⚙️", "使用技能25%機率不消耗PP"),
            new("relic_enrage",    "老子跟你爆搂",  "😤", "HP低於30%時傷害×1.6"),
            new("relic_regen",     "再生果實",  "🍎", "每回合回復MaxHP×3%（上限20）"),
            new("relic_boss_dmg",  "專打強者",  "🪞", "對Boss造成的傷害+50%"),
            new("relic_fullhp",    "滿血的我，是最強的",  "👑", "HP全滿時傷害+30%"),
            new("relic_amplify",   "發瘋啦","🔍", "所有攻擊傷害+30%"),
            new("relic_blood",     "血祭刃",    "🩸", "每回合自損MaxHP×3%但傷害×1.3"),
            new("relic_avenge",    "復仇碎片",  "💔", "受傷累積3次後下次攻擊造成雙倍傷害"),
            new("relic_kill_pp",   "奪命符文",  "⚡", "擊倒敵人後所有技能回復3PP"),
            // One-time / battle-start
            new("relic_shield",    "護盾符",    "🔮", "每場戰鬥免疫第一次受到的攻擊"),
            new("relic_phoenix",   "不死鳥羽",  "🪶", "本次冒險一次致命攻擊後以1HP存活"),
            new("relic_last_stand","最後防線",  "🏴", "HP低於20%時受到的傷害減少50%"),
            // Utility
            new("relic_hunter",    "獵人徽章",  "🎯", "捕獲率+30%"),
            new("relic_hourglass", "時間沙漏",  "⏳", "進入每一層時回復MaxHP×5%"),
            new("relic_berserk",   "背水一戰",  "🌊", "HP低於50%時每回合所有技能回復2PP"),
            new("relic_no_def",    "混沌之眼",  "🌀", "攻擊有20%機率完全無視防禦"),
            new("relic_will",      "意志結晶",  "✨", "每場戰鬥若全技能PP歸零則自動回復一次"),
            new("relic_chain",     "連鎖爆發",  "⛓️", "每擊倒一個敵人累積+5%傷害加成"),
            new("relic_time_warp",    "時光扭曲",   "⏰", "每場戰鬥開始時全部PP回復3點"),
            new("relic_executioner",  "劊子手",     "🪓", "敵人HP低於25%時傷害×2"),
            new("relic_mirror_coat",  "鏡面反射",   "🪞", "每場戰鬥有一次完全反射傷害機會"),
            new("relic_parasite",     "寄生種子",   "🌱", "每擊倒一隻敵人永久+5最大HP"),
            new("relic_feast",        "盛宴",       "🍖", "每場戰鬥勝利後回復50HP"),
            new("relic_double_edge",  "捨身衝撞",   "💨", "攻擊傷害+40%但每次攻擊自損傷害的15%"),
            new("relic_lucky_charm",  "幸運符",     "🍀", "所有隨機判定（暴擊/閃避/特效）機率+15%"),
            new("relic_exp_boost",    "學習加速器", "🎓", "每場戰鬥獲得的EXP×1.5"),
            new("relic_gold_mine",    "金礦脈",     "⛏️", "每場戰鬥勝利額外獲得20💰"),
            new("relic_berserker_r",  "狂暴之心",   "❤️‍🔥", "HP低於50%時傷害+40%（疊加enrage）"),
            new("relic_swift",        "迅捷之羽",   "🪽",  "速度+30%（疊加被動）"),
            new("relic_scholar",      "學者之冠",   "🎩", "每升一級額外獲得所有技能+5PP"),
            new("relic_comeback",     "逆轉勝負",   "🔄", "HP低於10%時下一次攻擊傷害×3"),
            new("relic_shared_pain",  "共苦盟約",   "🤝", "受到傷害時對敵人反彈30%傷害（疊加thorns）"),
        };

        private static readonly List<PassiveDef> _passives = new()
        {
            new("passive_firstblood",  "先手必勝",   "⚡", "永遠先手攻擊，不論速度"),
            new("passive_ironwall",    "鐵壁",       "🛡️", "所有受到的傷害減少 20%"),
            new("passive_berserker",   "狂戰士",     "🪓", "攻擊+30% 但防禦-20%"),
            new("passive_vampire",     "吸血鬼",     "🧛", "每次攻擊吸取傷害 30% 為HP"),
            new("passive_catchmaster", "捕獲大師",   "🎯", "初始獲得高級球×3，捕獲率+40%，但無法在商店購買球"),
            new("passive_richboy",     "金錢萬能",   "💵", "初始獲得 80 金幣，商店所有價格打7折"),
            new("passive_genius",      "天才",       "📚", "獲得的 EXP 翻倍"),
            new("passive_techgeek",    "技術宅",     "🔧", "技能強化上限提升至 8 次（原5次）"),
            new("passive_undying",     "不死身",     "🪶", "不死鳥羽效果可觸發兩次"),
            new("passive_tanker",      "坦克",       "🦛", "全隊最大 HP+40%，但速度-20%"),
            new("passive_chaosmaster", "渾沌大師",   "🌀", "所有負面狀態施加成功機率+30%（燒/麻/凍/眠/毒/降能力）"),
            new("passive_gambler",     "賭徒靈魂",   "🎰", "所有隨機傷害和獎勵浮動範圍×2（更高或更低）"),
            new("passive_packrat",     "囤貨王",     "🎒", "初始背包上限4隻（原3隻）"),
            new("passive_strategist",  "謀士",       "🔮", "可預測敵人未來兩回合的行動"),
        };

        private static readonly List<CursedRelicDef> _cursedRelics = new()
        {
            new("curse_half_pp",     "詛咒之語",   "💀", "全部技能 MaxPP 減半（最低1）"),
            new("curse_gold_tax",    "貪婪詛咒",   "🪙", "每層結束扣除 10💰（扣到0為止）"),
            new("curse_slow",        "重力詛咒",   "🔩", "全隊速度 -40%"),
            new("curse_weak_atk",    "腐蝕之力",   "⚗️", "全隊攻擊力 -25%"),
            new("curse_bleed",       "流血詛咒",   "🩸", "每回合強制扣 MaxHP×5%（疊加）"),
            new("curse_fragile",     "玻璃心",     "💔", "全隊防禦力 -30%"),
            new("curse_blind",       "蒙眼詛咒",   "👁️‍🗨️", "無法看到敵人下一招（預告消失）"),
            new("curse_expensive",   "奸商詛咒",   "🏪", "商店所有價格×1.5"),
            new("curse_exp_drain",   "知識吸取",   "📖", "獲得 EXP 減少 50%"),
            new("curse_no_catch",    "鐵籠詛咒",   "🔒", "無法捕獲任何 Pokemon（球全部失效）"),
            new("curse_hp_cap",      "生命封印",   "❤️‍🔥", "全隊最大 HP -20%"),
            new("curse_move_random", "混亂咒語",   "🌀", "每回合 20% 機率隨機使用技能"),
            new("curse_forget",      "遺忘詛咒",   "🧠", "每過一層隨機忘掉一個技能，換成隨機技能"),
            new("curse_weaken",      "虛弱加身",   "⚠️", "已強化過的技能威力減半"),
            new("curse_gold_drain",  "黃金枷鎖",   "🔗", "擊倒敵人時不給金幣，改扣現有金幣10%"),
            new("curse_mirror",      "角色互換",   "🔀", "每奇數回合玩家隨機使用技能（無法控制）"),
            new("curse_fragile2",    "紙糊護甲",   "📄", "每次受傷後防禦永久-3（下限1）"),
            new("curse_hungry",      "飢餓詛咒",   "🍽️", "每回合技能PP額外消耗1點"),
            new("curse_unlucky",     "厄運纏身",   "🎭", "所有暴擊/捕獲等機率減少30%"),
            new("curse_decay",       "腐敗詛咒",   "🦠", "神器效果減弱50%（攻擊類加成減半）"),
            new("curse_paranoia",    "妄想症",     "👻", "無法使用商店（商店路徑強制跳過）"),
            new("curse_silence",     "沉默詛咒",   "🔇", "威力最高的技能PP上限變為1"),
        };

        // pending starts keyed by channelId (before passive is chosen)
        private static readonly Dictionary<ulong, (ulong PlayerId, string PlayerName, PokeGamePokemon Src)> _pendingStarts = new();

        // ── 中文敵人池 (Name, Types, StatTotal, PokeApiId) ────────
        private static readonly List<(string Name, string[] Types, int StatTotal, int PokeId)> _enemyPool = new()
        {
            // 低階 (floor 1-3)  StatTotal < 430
            ("比雕",     new[]{"一般","飛行"}, 349, 22),
            ("隆隆石",   new[]{"岩石","地面"}, 390, 75),
            ("鬼斯通",   new[]{"幽靈","毒"},   405, 93),
            ("卡咪龜",   new[]{"水"},           405, 8),
            ("火恐龍",   new[]{"火"},           405, 5),
            ("妙蛙草",   new[]{"草","毒"},      405, 2),
            ("電擊獸",   new[]{"電"},           490, 26),
            ("皮卡丘",   new[]{"電"},           320, 25),
            ("瞌睡貘",   new[]{"超能力"},       303, 96),
            ("小磁怪",   new[]{"電"},           325, 81),
            ("小火馬",   new[]{"火"},           410, 77),
            ("可達鴨",   new[]{"水"},           320, 54),
            ("六尾",     new[]{"火"},           299, 37),
            ("大岩蛇",   new[]{"岩石","地面"},  385, 95),
            ("獨角蟲",   new[]{"蟲","毒"},      395, 15),
            ("喇叭芽",   new[]{"草","毒"},      300, 69),
            ("海星星",   new[]{"水"},           340, 120),
            ("喵喵",     new[]{"一般"},         290, 52),
            ("謎擬Q",    new[]{"超能力"},       320, 122),
            ("小拳石",   new[]{"岩石"},         300, 74),
            ("哈達",     new[]{"一般"},         253, 100),
            ("菊草葉",   new[]{"草"},           318, 152),
            ("火球鼠",   new[]{"火"},           309, 155),
            ("波克比",   new[]{"水"},           314, 158),
            ("幸福蛋",   new[]{"一般"},         430, 113),
            ("人偶",     new[]{"超能力"},       328, 202),
            ("毛毛蟲",   new[]{"蟲"},           195, 10),
            ("鐵甲蛹",   new[]{"蟲"},           205, 11),
            ("鴿子",     new[]{"一般","飛行"},  251, 16),
            // 中階 (floor 4-6)  430 <= StatTotal < 545
            ("暴鯉龍",   new[]{"水","飛行"},    540, 130),
            ("拉普拉斯", new[]{"水","冰"},      535, 131),
            ("雷電獸",   new[]{"電"},           525, 135),
            ("寶石海星", new[]{"水","超能力"},  520, 121),
            ("飛天螳螂", new[]{"蟲","飛行"},    500, 123),
            ("鴨嘴火獸", new[]{"火"},           495, 126),
            ("椰蛋樹",   new[]{"草","超能力"},  530, 103),
            ("刺殼菊兒", new[]{"水","冰"},      525, 91),
            ("嗡嗡蝙",   new[]{"地面","岩石"},  485, 105),
            ("多刺球",   new[]{"蟲"},           395, 14),
            ("班夏",     new[]{"幽靈"},         435, 292),
            ("電龍",     new[]{"電"},           520, 466),
            ("鋼鳥",     new[]{"鋼","飛行"},    510, 227),
            ("冰蝎王",   new[]{"冰"},           510, 473),
            ("苦栗寶",   new[]{"草"},           405, 470),
            ("格鬥鼬",   new[]{"格鬥"},         484, 286),
            ("妙蛙花",   new[]{"草","毒"},      525, 3),
            ("水箭龜",   new[]{"水"},           530, 9),
            ("水君",     new[]{"水"},           530, 245),
            ("雷公",     new[]{"電"},           530, 243),
            ("炎帝",     new[]{"火"},           530, 244),
            ("索羅亞克", new[]{"惡"},           510, 571),
            ("鐵臂膀",   new[]{"龍","格鬥"},    490, 783),
            ("蒼翠鳥",   new[]{"水","飛行"},    500, 130),
            ("伊布",     new[]{"一般"},         525, 133),
            ("電氣龍",   new[]{"電"},           490, 135),
            ("土地雲(戰) ",new[]{"地面","飛行"},580, 645),
            ("沙奈朵",   new[]{"超能力","妖精"},518, 282),
            ("颶風雲",   new[]{"電","飛行"},    580, 641),
            ("鐵甲弄蝶", new[]{"蟲","鋼"},      575, 205),
            ("泥偶巨人", new[]{"地面"},         580, 260),
            // 高階 (floor 7-9)  545 <= StatTotal < 590
            ("怪力",     new[]{"格鬥"},         505, 68),
            ("耿鬼",     new[]{"幽靈","毒"},    500, 94),
            ("胡地",     new[]{"超能力"},       500, 65),
            ("風速狗",   new[]{"火"},           555, 59),
            ("尼多王",   new[]{"毒","地面"},    505, 34),
            ("哈克龍",   new[]{"龍"},           420, 148),
            ("化石翼龍", new[]{"岩石","飛行"},  515, 142),
            ("袋獸",     new[]{"一般"},         490, 115),
            ("水箭龜",   new[]{"水"},           530, 9),
            ("噴火龍",   new[]{"火","飛行"},    534, 6),
            ("黑暗鴉",   new[]{"飛行","一般"},  555, 227),
            ("沙漠蜥蜴", new[]{"地面","龍"},    579, 330),
            ("鋼甲蛹",   new[]{"鋼","超能力"},  540, 376),
            ("格鬥公雞", new[]{"格鬥"},         530, 256),
            ("水晶雯",   new[]{"水"},           535, 350),
            ("鐵甲弄蝶", new[]{"蟲","鋼"},      575, 205),
            ("泥偶巨人", new[]{"地面"},         580, 260),
            // Boss (floor 10, 20)  StatTotal >= 590
            // ── 擬龍 / 偽傳說 ──
            ("快龍",       new[]{"龍","飛行"},      600, 149),
            ("班基拉斯",   new[]{"岩石","惡"},      600, 248),
            ("烈咬陸鯊",   new[]{"龍","地面"},      600, 445),
            ("暴飛龍",     new[]{"龍","飛行"},      600, 373),
            ("鋼鐵蛇",     new[]{"鋼","超能力"},    600, 376),
            ("藤藤蛇",     new[]{"龍"},             600, 706),
            ("骸骨龍",     new[]{"龍","幽靈"},      600, 887),
            ("鬥鬥蝦",     new[]{"龍","格鬥"},      600, 784),
            // ── 傳說 ──
            ("超夢",       new[]{"超能力"},         680, 150),
            ("夢幻",       new[]{"超能力"},         600, 151),
            ("路基亞",     new[]{"超能力","飛行"},  680, 249),
            ("鳳王",       new[]{"火","飛行"},      680, 250),
            ("固拉多",     new[]{"地面"},           670, 383),
            ("蓋歐卡",     new[]{"水"},             670, 382),
            ("烈空坐",     new[]{"龍","飛行"},      680, 384),
            ("帝牙盧卡",   new[]{"鋼","龍"},        680, 483),
            ("帕路奇亞",   new[]{"水","龍"},        680, 484),
            ("騎拉帝納",   new[]{"幽靈","龍"},      680, 487),
            ("雷希拉姆",   new[]{"龍","火"},        680, 643),
            ("捷克羅姆",   new[]{"龍","電"},        680, 644),
            ("酋雷姆",     new[]{"龍","冰"},        660, 646),
            ("哲爾尼亞斯", new[]{"妖精"},           680, 716),
            ("伊裂卡爾",   new[]{"惡","飛行"},      680, 717),
            ("索爾迦雷歐", new[]{"超能力","鋼"},    680, 791),
            ("露奈雅拉",   new[]{"超能力","幽靈"},  680, 792),
            ("藏瑪然特",   new[]{"妖精","鋼"},      720, 888),
            ("蒼響",       new[]{"格鬥","鋼"},      720, 889),
            ("依布",       new[]{"毒","龍"},        690, 890),
            ("天冠馬",     new[]{"超能力","幽靈"},  680, 898),
            ("亢龍",       new[]{"格鬥","龍"},      670, 1007),
            ("騰飛龍",     new[]{"電","龍"},        670, 1008),
            // ── Mega 進化 ──
            ("水箭龜Mega", new[]{"水"},             630, 9),
            ("噴火龍X",    new[]{"火","龍"},        634, 6),
            ("噴火龍Y",    new[]{"火","飛行"},      634, 6),
            ("超夢X",      new[]{"超能力","格鬥"},  780, 150),
            ("烈咬陸鯊Mega",new[]{"龍","地面"},     700, 445),
            ("班基拉斯Mega",new[]{"岩石","惡"},     700, 248),
        };

        // ── 神獸池 ────────────────────────────────────────────
        private static readonly (string Name, int PokeId, string[] Types)[] _legendaryPool = new[]
        {
            ("急凍鳥", 144, new[]{"冰","飛行"}),
            ("閃電鳥", 145, new[]{"電","飛行"}),
            ("火焰鳥", 146, new[]{"火","飛行"}),
            ("超夢",   150, new[]{"超能力"}),
            ("夢幻",   151, new[]{"超能力"}),
            ("雷公",   243, new[]{"電"}),
            ("炎帝",   244, new[]{"火"}),
            ("水君",   245, new[]{"水"}),
            ("路基亞", 249, new[]{"超能力","飛行"}),
            ("鳳王",   250, new[]{"火","飛行"}),
            ("拉帝雅斯", 380, new[]{"龍","超能力"}),
            ("拉帝歐斯", 381, new[]{"龍","超能力"}),
            ("蓋歐卡", 382, new[]{"水"}),
            ("固拉多", 383, new[]{"地面"}),
            ("烈空坐", 384, new[]{"龍","飛行"}),
            ("帝牙盧卡", 483, new[]{"鋼","龍"}),
            ("帕路奇亞", 484, new[]{"水","龍"}),
            ("騎拉帝納", 487, new[]{"幽靈","龍"}),
            ("克雷色利亞", 488, new[]{"超能力"}),
            ("席多藍恩", 638, new[]{"鋼","格鬥"}),
            ("焰輝鳳凰", 641, new[]{"飛行"}),
            ("土地雲",  645, new[]{"地面","飛行"}),
        };

        // ── 屬性剋制表 ─────────────────────────────────────────
        private static readonly Dictionary<string, Dictionary<string, float>> _typeChart = BuildTypeChart();

        private static Dictionary<string, Dictionary<string, float>> BuildTypeChart()
        {
            // Chinese type names
            var zh = new[] { "一般","火","水","電","草","冰","格鬥","毒","地面","飛行","超能力","蟲","岩石","幽靈","龍","惡","鋼","妖精" };
            var en = new[] { "Normal","Fire","Water","Electric","Grass","Ice","Fighting","Poison","Ground","Flying","Psychic","Bug","Rock","Ghost","Dragon","Dark","Steel","Fairy" };
            var all = zh.Concat(en).ToArray();

            var c = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in all)
            {
                c[a] = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in all) c[a][d] = 1.0f;
            }

            // Add both ZH and EN mappings for the same relationship
            void Map(int ai, int di, float v)
            {
                c[zh[ai]][zh[di]] = v; c[zh[ai]][en[di]] = v;
                c[en[ai]][zh[di]] = v; c[en[ai]][en[di]] = v;
            }

            // Fire
            Map(1,4,2);Map(1,5,2);Map(1,11,2);Map(1,16,2);
            Map(1,1,.5f);Map(1,2,.5f);Map(1,12,.5f);Map(1,14,.5f);
            // Water
            Map(2,1,2);Map(2,8,2);Map(2,12,2);
            Map(2,2,.5f);Map(2,4,.5f);Map(2,14,.5f);
            // Electric
            Map(3,2,2);Map(3,9,2);
            Map(3,3,.5f);Map(3,4,.5f);Map(3,14,.5f);
            c["電"]["地面"]=0;c["電"]["Ground"]=0;c["Electric"]["地面"]=0;c["Electric"]["Ground"]=0;
            // Grass
            Map(4,2,2);Map(4,8,2);Map(4,12,2);
            Map(4,1,.5f);Map(4,4,.5f);Map(4,7,.5f);Map(4,9,.5f);Map(4,11,.5f);Map(4,14,.5f);Map(4,16,.5f);
            // Ice
            Map(5,4,2);Map(5,8,2);Map(5,9,2);Map(5,14,2);
            Map(5,1,.5f);Map(5,2,.5f);Map(5,5,.5f);Map(5,16,.5f);
            // Fighting
            Map(6,0,2);Map(6,5,2);Map(6,12,2);Map(6,15,2);Map(6,16,2);
            Map(6,7,.5f);Map(6,9,.5f);Map(6,10,.5f);Map(6,11,.5f);Map(6,17,.5f);
            c["格鬥"]["幽靈"]=0;c["格鬥"]["Ghost"]=0;c["Fighting"]["幽靈"]=0;c["Fighting"]["Ghost"]=0;
            // Poison
            Map(7,4,2);Map(7,17,2);
            Map(7,7,.5f);Map(7,8,.5f);Map(7,12,.5f);Map(7,13,.5f);
            c["毒"]["鋼"]=0;c["毒"]["Steel"]=0;c["Poison"]["鋼"]=0;c["Poison"]["Steel"]=0;
            // Ground
            Map(8,1,2);Map(8,3,2);Map(8,7,2);Map(8,12,2);Map(8,16,2);
            Map(8,4,.5f);Map(8,11,.5f);
            c["地面"]["飛行"]=0;c["地面"]["Flying"]=0;c["Ground"]["飛行"]=0;c["Ground"]["Flying"]=0;
            // Flying
            Map(9,4,2);Map(9,6,2);Map(9,11,2);
            Map(9,3,.5f);Map(9,12,.5f);Map(9,16,.5f);
            // Psychic
            Map(10,6,2);Map(10,7,2);
            Map(10,10,.5f);Map(10,16,.5f);
            c["超能力"]["惡"]=0;c["超能力"]["Dark"]=0;c["Psychic"]["惡"]=0;c["Psychic"]["Dark"]=0;
            // Bug
            Map(11,4,2);Map(11,10,2);Map(11,15,2);
            Map(11,1,.5f);Map(11,6,.5f);Map(11,9,.5f);Map(11,13,.5f);Map(11,16,.5f);Map(11,17,.5f);
            // Rock
            Map(12,1,2);Map(12,5,2);Map(12,9,2);Map(12,11,2);
            Map(12,6,.5f);Map(12,8,.5f);Map(12,16,.5f);
            // Ghost
            Map(13,10,2);Map(13,13,2);
            Map(13,15,.5f);
            c["幽靈"]["一般"]=0;c["幽靈"]["Normal"]=0;c["Ghost"]["一般"]=0;c["Ghost"]["Normal"]=0;
            // Dragon
            Map(14,14,2);
            Map(14,16,.5f);
            c["龍"]["妖精"]=0;c["龍"]["Fairy"]=0;c["Dragon"]["妖精"]=0;c["Dragon"]["Fairy"]=0;
            // Dark
            Map(15,10,2);Map(15,13,2);
            Map(15,6,.5f);Map(15,15,.5f);Map(15,17,.5f);
            // Steel
            Map(16,5,2);Map(16,12,2);Map(16,17,2);
            Map(16,1,.5f);Map(16,2,.5f);Map(16,3,.5f);Map(16,16,.5f);
            // Fairy
            Map(17,6,2);Map(17,14,2);Map(17,15,2);
            Map(17,1,.5f);Map(17,7,.5f);Map(17,16,.5f);
            // Normal immune to Ghost
            c["一般"]["幽靈"]=0;c["一般"]["Ghost"]=0;c["Normal"]["幽靈"]=0;c["Normal"]["Ghost"]=0;

            return c;
        }

        #endregion

        #region 事件池（含小遊戲特殊事件）
        // ── 事件池（帶選項） ──────────────────────────────────────
        private record RelicDef(string Id, string Name, string Emoji, string Desc);
        private record PassiveDef(string Id, string Name, string Emoji, string Desc);
        private record CursedRelicDef(string Id, string Name, string Emoji, string Desc);
        private record EventChoice(string Label, string Emoji, Func<TowerRun, Task<string>> Apply);
        private record EventDef(string Title, string Emoji, string Desc, List<EventChoice> Choices);
        // 同步 choice 的語法糖
        private static EventChoice C(string label, string emoji, Func<TowerRun, string> f)
            => new(label, emoji, run => Task.FromResult(f(run)));

        private static readonly List<EventDef> _events = new()
        {
            new("神秘寶箱女", "🎁",
                "前方寶箱有一位白色雙馬尾的精靈困在其中，該做什麼呢?",
                new() {
                    C("🔓 解救精靈少女", "🔓", run => {
                        if (_rng.Next(4) == 0) {
                            int dmg = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.12));
                            run.ActivePokemon.CurrentHP = Math.Max(1, run.ActivePokemon.CurrentHP - dmg);
                            return $"💥 是陷阱！**{run.ActivePokemon.DisplayName}** 也被吃了，受到 **{dmg}** 傷害後逃出來了";
                        }
                        int g = _rng.Next(25, 65); run.Gold += g;
                        return $"白髮少女很開心，送了你 **{g} 金幣**！";
                    }),
                    C("🚶 當作沒看到", "🚶", run => "背後傳來好黑喔好可怕喔的聲音"),
                }),

            new("神秘藥丸", "💊",
                "地上有一頻不明藥丸，上面的標籤已經模糊不清，依稀寫著APTX48...。要吃嗎？",
                new() {
                    C("💊 直接吃下", "💊", run => {
                        if (_rng.Next(5) == 0) {
                            int dmg = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.20));
                            run.ActivePokemon.CurrentHP = Math.Max(1, run.ActivePokemon.CurrentHP - dmg);
                            return $"😨 是毒藥！**{run.ActivePokemon.DisplayName}** 損失 **{dmg}** HP！（只有先發吃到）";
                        }
                        foreach (var p in run.Party) p.CurrentHP = Math.Min(p.MaxHP, p.CurrentHP + Math.Max(1, p.MaxHP / 2));
                        return "💚 **全隊**恢復約 50% HP，而你的身材雖然縮小了，頭腦還是原來的名偵探！";
                    }),
                    C("👃 先聞一聞", "👃", run => {
                        foreach (var p in run.Party) p.CurrentHP = Math.Min(p.MaxHP, p.CurrentHP + Math.Max(1, p.MaxHP / 4));
                        return "🌿 小心地吃了一點，**全隊**恢復約 25% HP。";
                    }),
                    C("🚫 不碰它", "🚫", run => "謹慎地繞過，繼續前進，珍愛生命，遠離梯歐歪立。"),
                }),

            new("鼎王麻辣鍋", "⛲",
                "發現了超大鍋的鼎王麻辣鍋，你看著你的寶可夢……",
                new() {
                    C("🏊 整隻泡進去", "🏊", run => {
                        foreach (var p in run.Party) { p.CurrentHP = p.MaxHP; foreach (var m in p.Moves) m.CurrentPP = m.MaxPP; }
                        return $"✨ 冰水一壺冰水一壺 豆腐鴨血豆腐鴨血就飽啦，**全隊** HP 和 PP **完全恢復**！";
                    }),
                    C("💧 餵他吃一份豆腐鴨血", "💧", run => {
                        foreach (var p in run.Party) foreach (var m in p.Moves) m.CurrentPP = m.MaxPP;
                        return $"🔋 吃一份豆腐鴨血，**全隊**所有技能 **PP 完全恢復**！";
                    }),
                    C("💰 外帶打包帶走", "💰", run => {
                        int g = _rng.Next(20, 45); run.Gold += g;
                        return $"💰 外帶鼎王賣給了商人，獲得 **{g} 金幣**！";
                    }),
                }),

            new("外星手錶", "📀",
                "地上有一個散發著綠色光芒的外星手錶，螢幕還微微發亮。要觸碰看看嗎？",
                new() {
                    C("📀 摸摸看", "📀", run => {
                        var pool = PickMovesStatic(run.ActivePokemon.Types).OrderBy(_ => _rng.Next()).ToList();
                        run.PendingMoveRewards = pool.Take(3).ToList();
                        run.EventMoveRewardPending = true;
                        return "📀 手錶黏到你手上了，完全拿不掉！請從以下技能中選一個讓你的寶可夢學習：";
                    }),
                    C("💰 賣給其他人", "💰", run => {
                        int g = _rng.Next(15, 30); run.Gold += g;
                        return $"💰 把手錶賣了，然後把錢拿去買相撲卡跟奶昔，剩下 **{g} 金幣**！";
                    }),
                    C("🚶 不需要", "🚶", run => "留下了手錶，外星章魚將會統治世界。"),
                }),

            new("待業上", "😈",
                "你看到路邊有一個流浪漢，嘴巴不停說著我依然是世一上，他看到妳後向你撲了過來！",
                new() {
                    C("⚔️ 正面對抗", "⚔️", run => {
                        if (_rng.Next(2) == 0) {
                            return $"💪 成功擊退了bin哥！他化身bin大小姐跑去跟zeus哭，金幣完整保留（共 {run.Gold}💰）。";
                        }
                        int dmg = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.15));
                        run.ActivePokemon.CurrentHP = Math.Max(1, run.ActivePokemon.CurrentHP - dmg);
                        return $"😓 對抗失敗，損失 **{dmg}** HP，但保住了金幣，bin哥抱著滿滿的信心回blg找文波單挑。";
                    }),
                    C("💰 乖乖交出世一上的稱號", "💰", run => {
                        int lost = Math.Min(run.Gold, _rng.Next(10, 25));
                        run.Gold -= lost;
                        return $"😞 你根本不是世一上，bin直接搶走你的 **{lost} 金幣**。（剩餘 {run.Gold}💰）";
                    }),
                    C("🏃 高歌離席", "🏃", run => {
                        if (_rng.Next(3) == 0) {
                            int lost = Math.Min(run.Gold, _rng.Next(5, 15));
                            run.Gold -= lost;
                            return $"🫣 沒跑掉！bin哥最後追上你了，他搶了 **{lost} 金幣**去做眼皮手術。";
                        }
                        return "💨 成功逃跑！後方傳來 我是世一上我是世一上我是世一上的聲音";
                    }),
                }),

            new("遇到一個穿著綠色班服的神祕腳踏車大盜", "😈",
                "他偷偷繞到你的背後，想偷你的腳踏車但發現你沒有，所以偷了你的錢！",
                new() {
                    C("💰 哭阿，下次一定在朝會狠狠的教訓你", "💰", run => {
                        int lost = Math.Min(run.Gold, _rng.Next(10, 25));
                        run.Gold -= lost;
                        return $"😞 失去了 **{lost} 金幣**。（剩餘 {run.Gold}💰）";
                    })
                }),

            new("精靈的祝福", "🌟",
                "傳說中的精靈現身，散發著溫暖的光芒，似乎願意賜予祝福……",
                new() {
                    C("🙏 接受完整祝福", "🙏", run => {
                        foreach (var p in run.Party) { p.CurrentHP = p.MaxHP; foreach (var m in p.Moves) m.CurrentPP = m.MaxPP; }
                        return "🌟 **全隊** HP 和 PP **完全恢復**！";
                    }),
                    C("💰 求賜財富", "💰", run => {
                        int g = _rng.Next(30, 70); run.Gold += g;
                        int expGain = 20; run.Exp += expGain;
                        return $"💰 精靈賜予了 **{g} 金幣** 和 **{expGain} EXP**！";
                    }),
                    C("🎾 求賜精靈球", "🎾", run => {
                        run.Balls["super"] = run.Balls.GetValueOrDefault("super") + 2;
                        return "🔵 精靈賜予了 **超級球×2**！";
                    }),
                }),

            new("迷失樹林", "🌲",
                "四周全是樹，完全迷失了方向。要怎麼辦？",
                new() {
                    C("🧭 仔細找路", "🧭", run => {
                        if (_rng.Next(2) == 0) {
                            int g = _rng.Next(10, 30); run.Gold += g;
                            int expGain = 15; run.Exp += expGain;
                            return $"🍄 在林中找到了珍稀藥草，換了 **{g} 金幣** 和 **{expGain} EXP**！";
                        }
                        return "😵‍💫 耗費了許多時間，什麼也沒找到。";
                    }),
                    C("🏃 靠直覺衝", "🏃", run => {
                        if (_rng.Next(3) == 0) {
                            int dmg = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.10));
                            run.ActivePokemon.CurrentHP = Math.Max(1, run.ActivePokemon.CurrentHP - dmg);
                            return $"🌵 被樹枝劃傷，損失 **{dmg}** HP。";
                        }
                        return "😤 靠著直覺走出了樹林！";
                    }),
                    C("😴 先休息", "😴", run => {
                        int h = Math.Max(1, run.ActivePokemon.MaxHP / 5);
                        foreach (var p in run.Party) { p.CurrentHP = Math.Min(p.MaxHP, p.CurrentHP + Math.Max(1, p.MaxHP / 5)); foreach (var m in p.Moves) m.CurrentPP = m.MaxPP; }
                        return $"💤 休息了一會兒，**全隊**恢復約 20% HP 和 **全部 PP**，再出發！";
                    }),
                }),

            new("現代最強", "🧙",
                "一位帶著眼罩的白髮揍速師，似乎想傳授些什麼。",
                new() {
                    C("📚 請求對練", "📚", run => {
                        int g = _rng.Next(25, 55); run.Gold += g;
                        int expGain = 35; run.Exp += expGain;
                        return $"🧠 你說想跟他實際戰鬥，結果不到10秒他就被腰斬了，從現代最強的半身口袋中獲益，收到 **{g} 金幣** 和 **{expGain} EXP**。";
                    }),
                    C("💊 學習反轉術士", "💊", run => {
                        int h = Math.Max(1, run.ActivePokemon.MaxHP / 2);
                        foreach (var p in run.Party) { p.CurrentHP = Math.Min(p.MaxHP, p.CurrentHP + Math.Max(1, p.MaxHP / 2)); foreach (var m in p.Moves) m.CurrentPP = m.MaxPP; }
                        return $"🌿 現代最強飄在空中，喊著甚麼對不起天內...你聽不懂，但**全隊**睡著了，恢復約 50% HP + PP 全回！";
                    }),
                    C("📀 學習領域展開", "📀", run => {
                        var pool = PickMovesStatic(run.ActivePokemon.Types).OrderBy(_ => _rng.Next()).ToList();
                        run.PendingMoveRewards = pool.Take(3).ToList();
                        run.EventMoveRewardPending = true;
                        return $"📀 現代最強開出領域，**{run.ActivePokemon.DisplayName}** 大腦直接當機！醒過來後感覺學到了新東西，請選一個技能：";
                    }),
                }),

            new("廢棄的球工廠", "🏭",
                "發現了一間廢棄的精靈球工廠，倉庫裡還有些存貨……",
                new() {
                    C("⚽ 拿普通球", "⚽", run => {
                        int n = _rng.Next(3, 7);
                        run.Balls["normal"] = run.Balls.GetValueOrDefault("normal") + n;
                        return $"⚽ 找到了 **普通球×{n}**！";
                    }),
                    C("🔍 深入搜索", "🔍", run => {
                        if (_rng.Next(3) == 0) {
                            run.Balls["ultra"] = run.Balls.GetValueOrDefault("ultra") + 1;
                            return "🟡 在深處找到了稀有的 **高級球×1**！";
                        }
                        int n = _rng.Next(2, 5);
                        run.Balls["super"] = run.Balls.GetValueOrDefault("super") + n;
                        return $"🔵 找到了 **超級球×{n}**！";
                    }),
                    C("💰 全部變現", "💰", run => {
                        int g = _rng.Next(15, 35); run.Gold += g;
                        return $"💰 把找到的球賣掉，獲得 **{g} 金幣**！";
                    }),
                }),

            new("神秘外星人", "<:kc1:1511607011253551194>",
                "遇到一位妹妹頭綠色觸角皮膚黑黑的外星人，她似乎有些東西想交換。",
                new() {
                    C("💰 以金換物", "💰", run => {
                        if (run.Gold < 15) return "💸 金幣不足，外星人失望地離開了。";
                        run.Gold -= 15;
                        run.Balls["super"] = run.Balls.GetValueOrDefault("super") + 2;
                        return "🔵 花了 **15 金幣** 換到 **超級球×2**！";
                    }),
                    C("🎁 直接交換", "🎁", run => {
                        if (_rng.Next(2) == 0) {
                            run.Balls["ultra"] = run.Balls.GetValueOrDefault("ultra") + 1;
                            return "🟡 運氣好！獲得了 **高級球×1**！";
                        }
                        run.Balls["normal"] = run.Balls.GetValueOrDefault("normal") + 3;
                        return "⚽ 交換到了 **普通球×3**。";
                    }),
                    C("🚶 不感興趣", "🚶", run => "外星人抖了抖觸角便離開了。"),
                }),

            new("神秘長脖男", "<:541105947435859978:1526117158831001742>",
                "前方出現一顆頭飄在空中，定睛一看才發現是一個人，他似乎說了些甚麼。",
                new() {
                    C("📀 這個增幅裝置不能虧，鬼轉ap", "📀", run => {
                        var pool = PickMovesStatic(run.ActivePokemon.Types).OrderBy(_ => _rng.Next()).ToList();
                        run.PendingMoveRewards = pool.Take(3).ToList();
                        run.EventMoveRewardPending = true;
                        return "📀 長脖男塞給你一個增幅裝置，你的寶可夢感受到了能量！請選一個新技能：";
                    }),
                    C("🎁 這邊獎勵一把食魂者ap特朗德", "🎁", run => {
                        int lost = Math.Min(run.Gold, _rng.Next(10, 25));
                        run.Gold -= lost;
                        return $"😞 輸了，失去了 **{lost} 金幣**。（剩餘 {run.Gold}💰）";
                    }),
                    C("🔍 一起研究扶他知識，撰寫論文", "🔍", run => {
                        if (_rng.Next(4) == 0) {
                            int dmg = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.12));
                            run.ActivePokemon.CurrentHP = Math.Max(1, run.ActivePokemon.CurrentHP - dmg);
                            return $"💥 是陷阱！ 教授不給過，**{run.ActivePokemon.DisplayName}** 覺得很羞恥，受到 **{dmg}** 心靈傷害";
                        }
                        int g = _rng.Next(15, 40); run.Gold += g;
                        return $"論文火了，後續甚至登上各大新聞版面，賺到了 **{g} 金幣**！";
                    }),
                }),

            new("神秘鳥巢頭", "<:540922644267270154:1526117156641706044>",
                "有一個看起來很邋遢的留著鳥巢頭的大叔，他一看到你就開始哭著對你說:",
                new() {
                    C("📀 真的好想玩活俠傳阿哈阿哈2ㄏ2ㄏ，甚麼時候才要更新阿", "📀", run => {
                        if (_rng.Next(10) == 0) {
                            int dmg = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.12));
                            run.ActivePokemon.CurrentHP = Math.Max(1, run.ActivePokemon.CurrentHP - dmg);
                            return $"💥 你跟他說反正$也用不到不如給我，結果**{run.ActivePokemon.DisplayName}** 就被痛打一頓，受到 **{dmg}** 傷害後快速跑走了";
                        }
                        int g = _rng.Next(25, 65); run.Gold += g;
                        return $"你跟他說反正$也用不到不如給我，他覺得滿有道理的，送了你 **{g} 金幣**！";
                    }),
                    C("😩 怎麼會有人一直花錢買pokemon的遊戲啊，10年來從沒換過诶", "😩", run => {
                        run.Balls["super"] = run.Balls.GetValueOrDefault("super") + 2;
                        return $"🔵 他一邊哭一邊還是買下了最新版的pokemon，你獲得了他的**超級球×{2}**！";
                    }),
                    C("😴 想不想來一局緊張又刺激的指令卡戰鬥阿? 2026最好玩的遊戲喔", "😴", run => {
                        foreach (var p in run.Party) { p.CurrentHP = Math.Min(p.MaxHP, p.CurrentHP + Math.Max(1, p.MaxHP / 5)); foreach (var m in p.Moves) m.CurrentPP = m.MaxPP; }
                        return "💤 你陪他玩了一下，但真的太無聊你不小心睡著了，**全隊**成功回血，恢復約 20% HP 和 **全部 PP**！";
                    }),
                }),

            new("神秘拉弓男", "<:404439235290988544:1526117155127562320>",
                "你看到了一位在路邊練習拉弓的人，他看到妳後射掉你頭上的蘋果，",
                new() {
                    C("🪪 可以借我看一下你的健保卡嗎?", "🪪", run => {
                        int dmg = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.15));
                        run.ActivePokemon.CurrentHP = Math.Max(1, run.ActivePokemon.CurrentHP - dmg);
                        return $"😨 他直接笑你長得像外星人，你後來還被做成Discord貼圖！**{run.ActivePokemon.DisplayName}** 受到嚴重心靈傷害 **{dmg}** 點。";
                    }),
                    C("🎮 這把認真局ok吧?", "🎮", run => {
                        if (_rng.Next(2) == 0) {
                            int g = _rng.Next(25, 55); run.Gold += g;
                            return $"⚔️ 他一邊說著要認真一邊拿出雙刀流柔伊，但在一番努力下你贏了，獲得 **{g} 金幣**！";
                        }
                        int lost = Math.Min(run.Gold, _rng.Next(15, 35));
                        run.Gold -= lost;
                        return $"😔 完全沒辦法，輸了一把，扣了 **{lost} 金幣**。（剩餘 {run.Gold}💰）";
                    }),
                    C("🩺 還敢趁我不在偷偷用索拉卡贏遊戲喔?", "🩺", run => {
                        run.Balls["super"] = run.Balls.GetValueOrDefault("super") + 2;
                        return "🔵 你趁他不在的時候趕快用索拉卡贏了一把，獲得 **超級球×2**！";
                    }),
                }),

            new("神秘綠帽男", "<:325482625127153664:1526117110667804836>",
                "你走一走路不小心踢到東西，低頭才發現原來是一個戴著帽子的人，他跟你介紹了他的貓芝麻",
                new() {
                    C("💕 他問妳是不是純愛黨?", "💕", run => {
                        if (_rng.Next(2) == 0) {
                            int g = _rng.Next(40, 80); run.Gold += g;
                            return $"🥰 你說是！他非常開心，大方地給了你 **{g} 金幣**！";
                        }
                        var victim = run.Party.Count > 1
                            ? run.Party[_rng.Next(run.Party.Count)]
                            : run.ActivePokemon;
                        victim.CurrentHP = 0;
                        return $"😱 你說不是！他暴怒，直接把 **{victim.DisplayName}** 打倒了！";
                    }),
                    C("⚔️ 他說這把他會秀給你看", "⚔️", run => {
                        run.Gold += 19;
                        return "🏆 他完成了傳說 **1/9/2 牙膏**，感動地分給你 **19.2 金幣**（取整數19）！";
                    }),
                    C("💬 跟他講一講話…", "💬", run => "🫥 你跟他講一講話發現人不見了，又不見了，後來喊了他好幾下也沒有回應，無事發生。"),
                }),

            new("神秘揪打遊戲女", "🎮",
                "你在準備上樓前，收到一則 Discord 私訊：",
                new() {
                    C("🔦 R.E.P.O -1 要不要++", "🔦", run => {
                        if (_rng.Next(2) == 0) {
                            run.Balls["ultra"] = run.Balls.GetValueOrDefault("ultra") + 1;
                            return "👑 你打贏了其他人，當上了 loser 之王！獲得 **高級球×1**！";
                        }
                        int lost = Math.Min(run.Gold, _rng.Next(15, 35));
                        run.Gold -= lost;
                        return $"💀 你們揪團打了好幾場 R.E.P.O，從來沒有通關過，失去了 **{lost} 金幣**。（剩餘 {run.Gold}💰）";
                    }),
                    C("🏔️ PEAK ++", "🏔️", run => {
                        int lost = Math.Min(run.Gold, _rng.Next(20, 45));
                        run.Gold -= lost;
                        return $"😈 她化身遊戲大盜，偷了其他人的遊戲來玩，還順便偷走了你的 **{lost} 金幣**。（剩餘 {run.Gold}💰）";
                    }),
                    C("🎯 APEX -1 ++", "🎯", run => {
                        int g = _rng.Next(20, 45); run.Gold += g;
                        run.Balls["super"] = run.Balls.GetValueOrDefault("super") + 1;
                        return $"🦅 她不演了，直接秀一手頂獵實力帶你上分，獲得 **{g} 金幣** 和 **超級球×1**！";
                    }),
                }),

            new("神秘程式男", "<:415032840925741056:1526117153307103282>",
                "在你準備登上下一層時，你看到了一個人在路邊推銷其他人使用自己的soyo機器人，你靠近看了看，",
                new() {
                    new("💬 上前搭話", "💬", async run => {
                        if (_aiService == null) return "（Soyo 好像去充電了，沒有反應……）";
                        string prompt = "你是一個名叫Soyo的可愛Discord機器人助手，個性活潑親切，說話帶有一點點傲嬌。有人在神秘塔的路上遇到了正在推銷你的程式男，並走上來搭話。請用繁體中文，以Soyo的口吻，隨機說一句有趣的自我介紹或推銷詞，不超過60字。";
                        string reply = await _aiService.GenerateSimpleTextAsync(prompt);
                        int expGain = 30;
                        run.Exp += expGain;
                        return $"🤖 Soyo 機器人：「{reply.Trim()}」\n✨ 聽了 Soyo 的自我介紹，獲得 **{expGain} EXP**！";
                    }),
                    new("🎮 試用看看", "🎮", async run => {
                        if (_aiService == null) return "（系統錯誤：找不到 Soyo……）";
                        string prompt = "你是Soyo，一個可愛的Discord機器人。有人想試用你，請用繁體中文給出一個簡短的寶可夢爬塔小技巧或鼓勵的話，不超過50字，口氣要俏皮可愛。";
                        string tip = await _aiService.GenerateSimpleTextAsync(prompt);
                        int expGain = 50;
                        run.Exp += expGain;
                        return $"💡 Soyo 給的爬塔建議：「{tip.Trim()}」\n✨ 試用了 Soyo 機器人，獲得 **{expGain} EXP**！";
                    }),
                    C("🚶 裝沒看見，繼續趕路", "🚶", run => "你假裝沒看到，快步離開，背後傳來「欸欸欸你要不要試試看～」的聲音……"),
                }),

            new("藍髮媚魔", "<a:95333c6fabb3e5d23e6325817ce09986:1293572566715203594>",
                "一位藍髮媚魔出現在你面前，想找你組樂隊……",
                new() {
                    C("🎵 同意組樂隊，而且要組一輩子", "🎵", run => {
                        int expGain = 40; run.Exp += expGain;
                        return $"🎸 說說而已，唱完一首歌後樂團就解散了。但這段美好回憶值得 **{expGain} EXP**！";
                    }),
                    C("😔 同意但根本不覺得開心過", "😔", run => {
                        int dmg = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.20));
                        run.ActivePokemon.CurrentHP = Math.Max(1, run.ActivePokemon.CurrentHP - dmg);
                        run.Balls["super"] = run.Balls.GetValueOrDefault("super") + 1;
                        int expGain = 25; run.Exp += expGain;
                        return $"💔 樂團解散後得了人格分裂症，**{run.ActivePokemon.DisplayName}** 受到 **{dmg}** 心靈傷害，但獲得了 **超級球×1** 和 **{expGain} EXP**。";
                    }),
                    C("🚫 不同意（但還是加入了）", "🚫", run => {
                        var pool = PickMovesStatic(new List<string> { "重力", "地面", "岩石", "一般" });
                        var gravityMove = pool.FirstOrDefault(m => m.Name == "地震" || m.Name == "岩石封鎖" || m.Name == "岩石滑落")
                            ?? _movePool.Where(m => m.Type == "地面" || m.Type == "岩石").OrderBy(_ => _rng.Next()).FirstOrDefault()
                            ?? pool[0];
                        int slot = run.ActivePokemon.Moves.Select((m,i)=>(m,i)).OrderBy(x=>x.m.Power).First().i;
                        string old = run.ActivePokemon.Moves[slot].Name;
                        run.ActivePokemon.Moves[slot] = new TowerMove { Name=gravityMove.Name, Type=gravityMove.Type, Power=gravityMove.Power, Category=gravityMove.Category, Emoji=gravityMove.Emoji, MaxPP=gravityMove.MaxPP, CurrentPP=gravityMove.MaxPP };
                        int expGain = 35; run.Exp += expGain;
                        return $"🎸 嘴巴說不同意但還是加入了，成為重力樂團女！學會了 {gravityMove.Emoji}**{gravityMove.Name}**，取代 **{old}**，+**{expGain} EXP**！";
                    }),
                }),

            new("感情修羅場", "💢",
                "你看到一個女的在公園跪著跟另外一位女學生說話，你決定…",
                new() {
                    C("📢 衝上去喊676767", "📢", run => {
                        int dmg = 67;
                        run.ActivePokemon.CurrentHP = Math.Max(1, run.ActivePokemon.CurrentHP - dmg);
                        int expGain = 7; run.Exp += expGain; run.Gold += 67;
                        return $"😵 被痛扁了 67 滴！**{run.ActivePokemon.DisplayName}** 受到 **{dmg}** 傷害，但獲得了 **67 金幣** 和 **{expGain} EXP（取整）**！";
                    }),
                    C("🕊️ 勸架", "🕊️", run => {
                        run.Balls["master"] = run.Balls.GetValueOrDefault("master") + 2;
                        int expGain = 60; run.Exp += expGain;
                        return $"😢 她哭著說只要能重新組樂隊，她什麼都願意做。你好好勸說了他們一番，收到了 **大師球×2** 和 **{expGain} EXP**！";
                    }),
                }),

            new("神秘數學題", "🔢",
                "一個神秘人攔住你，說要考你數學，答對有獎，答錯受罰。",
                new() {
                    C("答案 A", "🅰️", run => run.MathCorrectChoice == 0
                        ? $"✅ 答對了！{MathReward(run)}" : $"❌ 答錯了！{MathPunish(run)}"),
                    C("答案 B", "🅱️", run => run.MathCorrectChoice == 1
                        ? $"✅ 答對了！{MathReward(run)}" : $"❌ 答錯了！{MathPunish(run)}"),
                    C("答案 C", "🇨", run => run.MathCorrectChoice == 2
                        ? $"✅ 答對了！{MathReward(run)}" : $"❌ 答錯了！{MathPunish(run)}"),
                }),

            new("傳說神獸現身！", "✨",
                "前方出現了一道耀眼的光芒——一隻傳說中的神獸出現了！你要怎麼做？",
                new() {
                    new EventChoice("⚔️ 正面對打", "⚔️", async run => {
                        var leg = _legendaryPool[_rng.Next(_legendaryPool.Length)];
                        float scale = 1.0f + (run.CurrentFloor - 1) * 0.07f;
                        int b = Math.Max(40, (int)(600 * scale / 7));
                        var enemy = new TowerEnemy {
                            Name = $"✨ {leg.Name}", PokeId = leg.PokeId,
                            Types = leg.Types.ToList(),
                            MaxHP = (int)(b * 1.6), CurrentHP = (int)(b * 1.6),
                            Attack = b, Defense = (int)(b * 0.9),
                            SpecialAttack = b, SpecialDefense = (int)(b * 0.9),
                            Speed = b, IsBoss = false, GoldReward = 0,
                            Moves = PickMovesStatic(leg.Types.ToList()),
                        };
                        bool win = _rng.Next(100) < 40;
                        int expGain = run.Level * 50;
                        run.Exp += expGain;
                        if (win) {
                            run.PendingCatch = enemy;
                            return $"⚡ 一番苦戰之後，**{leg.Name}** 精疲力竭！趁現在收服牠吧！（+{expGain} EXP）";
                        }
                        int dmg = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.35));
                        run.ActivePokemon.CurrentHP = Math.Max(1, run.ActivePokemon.CurrentHP - dmg);
                        return $"💥 **{leg.Name}** 太強了！**{run.ActivePokemon.DisplayName}** 受到 **{dmg}** 傷害後敗退，但還是獲得了 +{expGain} EXP。";
                    }),
                    new EventChoice("🕸️ 丟普通網子捕捉", "🕸️", async run => {
                        var leg = _legendaryPool[_rng.Next(_legendaryPool.Length)];
                        float scale = 1.0f + (run.CurrentFloor - 1) * 0.07f;
                        int b = Math.Max(40, (int)(600 * scale / 7));
                        var enemy = new TowerEnemy {
                            Name = $"✨ {leg.Name}", PokeId = leg.PokeId,
                            Types = leg.Types.ToList(),
                            MaxHP = (int)(b * 1.6), CurrentHP = (int)(b * 1.6),
                            Attack = b, Defense = (int)(b * 0.9),
                            SpecialAttack = b, SpecialDefense = (int)(b * 0.9),
                            Speed = b, IsBoss = false, GoldReward = 0,
                            Moves = PickMovesStatic(leg.Types.ToList()),
                        };
                        bool caught = _rng.Next(10) == 0;
                        if (caught) {
                            run.PendingCatch = enemy;
                            return $"😱 哀呀我被抓到了哀呀，我堂堂神獸被一個普通網子抓了！？**{leg.Name}** 懵掉了，趁現在帶走牠！";
                        }
                        int dmg = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.25));
                        run.ActivePokemon.CurrentHP = Math.Max(1, run.ActivePokemon.CurrentHP - dmg);
                        int expGain = run.Level * 20;
                        run.Exp += expGain;
                        return $"幹，當林北 **{leg.Name}** 是水君嗎？ **{leg.Name}** 一怒之下痛扁 **{run.ActivePokemon.DisplayName}**  **{dmg}** 點傷害，不過還是獲得了 {expGain} EXP。";
                    }),
                }),

            new("神秘快遞", "📦",
                "前方有一個包裝完好的神秘包裹，上面沒有寄件人，也沒有收件人……",
                new() {
                    C("📦 打開來看看", "📦", run => {
                        if (_rng.Next(10) < 7) {
                            int g = _rng.Next(40, 81); run.Gold += g;
                            return $"🎉 裡面是一大堆金幣！獲得 **{g}💰**！";
                        }
                        foreach (var p in run.Party) p.CurrentHP = p.MaxHP;
                        return "💚 裡面裝著神奇藥水，**全隊 HP 完全回滿**！";
                    }),
                    C("📮 退回去", "📮", run => {
                        run.Gold += 30;
                        return "📮 你把包裹退回去，郵局給了你 **30💰** 的手續費。";
                    }),
                }),

            new("時光機", "⏰",
                "角落有一台生鏽的時光機，還在嗡嗡作響，似乎還能用……",
                new() {
                    C("⏪ 回到過去", "⏪", run => {
                        foreach (var p in run.Party) { p.CurrentHP = p.MaxHP; foreach (var m in p.Moves) m.CurrentPP = m.MaxPP; }
                        run.Gold += 30;
                        return "✨ 回到了一個沒有詛咒的時刻，**全隊 HP、PP 完全回復**，並額外獲得 **30💰**！";
                    }),
                    new EventChoice("⏩ 跳到未來", "⏩", async run => {
                        var available = _relics.Where(r => !run.RelicIds.Contains(r.Id)).ToList();
                        if (available.Count == 0) { run.Gold += 80; return "🔮 未來中你已擁有所有神器！獲得 **80💰** 作為補償。"; }
                        var picked = available[_rng.Next(available.Count)];
                        run.RelicIds.Add(picked.Id);
                        ApplyRelicOnPickup(run, picked.Id);
                        run.RunLog.Add($"🏺 時光機帶來神器【{picked.Name}】！");
                        return $"🔮 從未來帶回了神器 **{picked.Emoji}{picked.Name}**！{picked.Desc}";
                    }),
                }),

            new("神秘修行者", "🧘",
                "山洞中有一位閉目養神的修行者，感覺到你靠近後緩緩睜開眼睛。",
                new() {
                    C("📖 接受指導", "📖", run => {
                        foreach (var m in run.ActivePokemon.Moves) m.Power += 10;
                        int expGain = 50; run.Exp += expGain;
                        return $"🔥 修行者的智慧灌入腦海，**{run.ActivePokemon.DisplayName}** 的所有技能威力 **+10**，並獲得 **{expGain} EXP**！";
                    }),
                    C("🌙 學習秘術", "🌙", run => {
                        foreach (var p in run.Party) foreach (var m in p.Moves) { m.MaxPP += 3; m.CurrentPP = m.MaxPP; }
                        return "🌙 全隊所有技能 **MaxPP+3** 並完全回滿！";
                    }),
                }),

            new("廢棄寶箱", "🎁",
                "路邊有一個布滿灰塵的大寶箱，上面的鎖看起來很老舊……",
                new() {
                    C("💥 用力砸開", "💥", run => {
                        if (_rng.Next(2) == 0) {
                            run.Balls["super"] = run.Balls.GetValueOrDefault("super") + 2;
                            run.Gold += 30;
                            return "💥 箱子被砸開了！裡面有 **超級球×2** 和 **30💰**！";
                        }
                        foreach (var p in run.Party) p.CurrentHP = p.MaxHP;
                        run.Balls["ultra"] = run.Balls.GetValueOrDefault("ultra") + 1;
                        return "✨ 箱子裡的聖光讓**全隊 HP 全回**，還有一顆 **高級球**！";
                    }),
                    new EventChoice("🔑 用鑰匙開（消耗20💰）", "🔑", async run => {
                        if (run.Gold < 20) return "💸 沒有足夠的金幣打開寶箱……（需要20💰）";
                        run.Gold -= 20;
                        run.Balls["ultra"] = run.Balls.GetValueOrDefault("ultra") + 2;
                        run.Gold += 60;
                        return "🔑 寶箱緩緩打開，裡面整整齊齊！獲得 **高級球×2** 和 **60💰**！";
                    }),
                }),

            new("Pokemon 救援隊", "🚁",
                "天空中突然飛來一架直升機，上面有一支 Pokemon 救援隊正在執行任務！",
                new() {
                    C("🆘 請求支援", "🆘", run => {
                        foreach (var p in run.Party) { p.CurrentHP = p.MaxHP; foreach (var m in p.Moves) m.CurrentPP = m.MaxPP; }
                        return "🚁 救援隊立刻展開行動，**全隊 HP 和 PP 完全回復**！";
                    }),
                    C("🎒 分享物資", "🎒", run => {
                        run.Balls["normal"] = run.Balls.GetValueOrDefault("normal") + 3;
                        run.Balls["super"] = run.Balls.GetValueOrDefault("super") + 1;
                        run.Gold += 30;
                        return "🎒 救援隊分享了物資：**普通球×3**、**超級球×1** 和 **30💰**！";
                    }),
                }),

            new("傳說中的廚師", "👨‍🍳",
                "傳說中的廚師長正在野外開伙，香氣飄散了整個迷宮……",
                new() {
                    C("🍽️ 吃一頓大餐", "🍽️", run => {
                        foreach (var p in run.Party) p.CurrentHP = p.MaxHP;
                        foreach (var p in run.Party) { p.Attack = (int)(p.Attack * 1.10f); p.SpecialAttack = (int)(p.SpecialAttack * 1.10f); }
                        return "🍖 美食的力量！**全隊 HP 全回**，並且全隊攻擊力 **永久+10%**！";
                    }),
                    C("🥡 外帶走", "🥡", run => {
                        int g = _rng.Next(50, 81); run.Gold += g;
                        return $"🥡 廚師說外帶更貴，你賣給了路過的商人，獲得 **{g}💰**！";
                    }),
                }),

            new("幸運骰子", "🎲",
                "地上有一顆發光的骰子，感覺只要擲一下就會有事發生……",
                new() {
                    new EventChoice("🎲 擲骰子", "🎲", async run => {
                        int roll = _rng.Next(1, 7);
                        switch (roll) {
                            case 1: {
                                int lost = Math.Min(run.Gold, 20); run.Gold -= lost;
                                return $"⚀ 骰到 **1**！倒楣，扣了 **{lost}💰**。（剩餘 {run.Gold}💰）";
                            }
                            case 2: return "⚁ 骰到 **2**，無事發生，也算是一種幸運。";
                            case 3: { run.Gold += 30; return "⚂ 骰到 **3**！獲得 **30💰**！"; }
                            case 4: { run.Gold += 50; return "⚃ 骰到 **4**！獲得 **50💰**！"; }
                            case 5: {
                                foreach (var p in run.Party) p.CurrentHP = p.MaxHP;
                                return "⚄ 骰到 **5**！超級幸運，**全隊 HP 全回**！";
                            }
                            default: {
                                var avail = _relics.Where(r => !run.RelicIds.Contains(r.Id)).ToList();
                                if (avail.Count == 0) { run.Gold += 80; return "⚅ 骰到 **6**！但神器已集齊，獲得 **80💰**！"; }
                                var picked = avail[_rng.Next(avail.Count)];
                                run.RelicIds.Add(picked.Id);
                                ApplyRelicOnPickup(run, picked.Id);
                                run.RunLog.Add($"🏺 骰子帶來神器【{picked.Name}】！");
                                return $"⚅ 骰到 **6**！天降神器 **{picked.Emoji}{picked.Name}**！{picked.Desc}";
                            }
                        }
                    }),
                    C("🚫 不賭了", "🚫", run => {
                        run.Gold += 20;
                        return "🚫 你果斷拒絕，骰子自動消失，卻留下了 **20💰**！";
                    }),
                }),

            new("古代遺跡", "🏛️",
                "眼前出現了神秘的古代遺跡，壁畫上有各種奇異的符號……",
                new() {
                    C("🔍 破解謎題", "🔍", run => {
                        foreach (var m in run.ActivePokemon.Moves) m.Power += 15;
                        var pool = PickMovesStatic(run.ActivePokemon.Types);
                        var nm = pool.FirstOrDefault(m => run.ActivePokemon.Moves.All(e => e.Name != m.Name)) ?? pool[0];
                        int slot = run.ActivePokemon.Moves.Select((m,i)=>(m,i)).OrderBy(x=>x.m.Power).First().i;
                        string old = run.ActivePokemon.Moves[slot].Name;
                        run.ActivePokemon.Moves[slot] = new TowerMove { Name=nm.Name, Type=nm.Type, Power=nm.Power, Category=nm.Category, Emoji=nm.Emoji, MaxPP=nm.MaxPP, CurrentPP=nm.MaxPP };
                        return $"📜 謎題解開！所有技能威力 **+15**，並學會新技能 {nm.Emoji}**{nm.Name}**，取代了 **{old}**！";
                    }),
                    new EventChoice("🏺 帶走神器", "🏺", async run => {
                        var available = _relics.Where(r => !run.RelicIds.Contains(r.Id)).ToList();
                        if (available.Count == 0) { run.Gold += 80; return "🏛️ 遺跡中的神器你都已擁有，獲得 **80💰** 作為補償。"; }
                        var picked = available[_rng.Next(available.Count)];
                        run.RelicIds.Add(picked.Id);
                        ApplyRelicOnPickup(run, picked.Id);
                        run.RunLog.Add($"🏺 遺跡神器【{picked.Name}】！");
                        return $"🏺 從遺跡中取出神器 **{picked.Emoji}{picked.Name}**！{picked.Desc}";
                    }),
                    C("↩️ 原路返回", "↩️", run => {
                        foreach (var p in run.Party) p.CurrentHP = Math.Min(p.MaxHP, p.CurrentHP + Math.Max(1, p.MaxHP / 2));
                        return "↩️ 明智地撤退，在外面休息了一下，**全隊回復 50% HP**！";
                    }),
                }),

            // ── 小遊戲特殊事件 ─────────────────────────────────────

            new("204..1532?", "🎮",
                "走廊角落有台散落零件的老舊機器，螢幕閃著 1532 的字樣……「挑戰成功送好禮！」",
                new() {
                    new("🎮 接受挑戰（15步內到32）", "🎮", async run => {
                        int reward = 30 + run.CurrentFloor * 2;
                        Setup2048(run, reward);
                        run.State = TowerRunState.InMiniGame2048;
                        return $"🎮 機器啟動！**15步**內讓方塊到達 **32**，獲得 **{reward}💰**！";
                    })
                }),

            new("非洲小孩訓練場", "💣",
                "前方是一片黑暗的訓練場，地上貼著標語「沒有網路也能玩踩地雷」",
                new() {
                    new("💣 接受踩地雷挑戰（連踩5步安全）", "💣", async run => {
                        int reward = 40 + run.CurrentFloor * 2;
                        SetupMinesweeper(run, reward, mineCount: 3);
                        run.State = TowerRunState.InMiniGameMine;
                        return $"💣 3×3 地圖，藏了 **3 顆地雷**！連踩 **5 步**不爆炸，獲得 **{reward}💰**！";
                    })
                }),

            new("一具手指放在嘴巴前的木乃伊", "⚔️",
                "他今年還在打，這一冠，為了自己，他向你問到：「阿一古 西巴西八 這是哪個英雄的能力描述牙 猜對有獎私密打！」",
                new() {
                    new("⚔️ 接受挑戰！", "⚔️", async run => {
                        if (_champService?.allChampionData?.data == null || _champService.allChampionData.data.Count < 4)
                            return "😕 英雄資料庫暫時不可用，改天再來！";
                        var champs = _champService.allChampionData.data.Values.ToList();
                        var picks = champs.OrderBy(_ => _rng.Next()).Take(4).ToList();
                        int answerIdx = _rng.Next(4);
                        var answer = picks[answerIdx];
                        string blurb = string.IsNullOrWhiteSpace(answer.blurb)
                            ? $"一位神秘英雄，代號 {answer.id}。"
                            : answer.blurb.Replace("\n", " ").Length > 120
                                ? answer.blurb.Replace("\n", " ")[..120] + "…"
                                : answer.blurb.Replace("\n", " ");
                        int reward = 50 + run.CurrentFloor;
                        run.MiniGameQuizQuestion = $"⚔️ **英雄描述：**\n_{blurb}_\n猜猜這是誰呀？（猜對 +{reward}💰）";
                        run.MiniGameQuizChoices = picks.Select(p => p.name).ToList();
                        run.MiniGameQuizAnswerIdx = answerIdx;
                        run.MiniGameQuizReward = reward;
                        run.State = TowerRunState.InMiniGameQuiz;
                        return "⚔️ 好！讓我看看你對英雄聯盟了解多少……";
                    }),
                }),

            new("wordle", "📚",
                "Your group is on a 1 day streak! 🔥 Here are yesterday's results:",
                new() {
                    new("📚 接受挑戰！", "📚", async run => {
                        try {
                            var words = WordGuessingService.LoadWords().Where(w => w.word.Length >= 4 && w.word.Length <= 6).ToList();
                            if (words.Count < 4) return "📚 單字庫太少了，找不到題目。";
                            var picks = words.OrderBy(_ => _rng.Next()).Take(4).ToList();
                            int answerIdx = _rng.Next(4);
                            var answer = picks[answerIdx];
                            string hint = answer.word[0].ToString().ToUpper() + new string('_', answer.word.Length - 1);
                            int reward = 30 + run.CurrentFloor;
                            run.MiniGameQuizQuestion = $"📖 **提示：** `{hint}` （{answer.word.Length} 個字母）\n意思：**{answer.translate}**\n\n哪個是正確的單字？（猜對 +{reward}💰）";
                            run.MiniGameQuizChoices = picks.Select(p => p.word.ToUpper()).ToList();
                            run.MiniGameQuizAnswerIdx = answerIdx;
                            run.MiniGameQuizReward = reward;
                            run.State = TowerRunState.InMiniGameQuiz;
                            return "📚 單字測驗開始！";
                        } catch { return "📚 載入單字庫失敗，改天再試。"; }
                    }),
                }),
        };

        #endregion

        private static OpenRouterService _aiService;
        private static GetChampService _champService;

        #region 技能效果初始化
        // ── 把效果資料打入 _movePool（不動現有 entry 的其他欄位）────────
        private static bool _moveEffectsLoaded = false;
        private static void InitMoveEffects()
        {
            if (_moveEffectsLoaded) return;
            _moveEffectsLoaded = true;

            void E(string name, string ailment = "", int chance = 0, int drain = 0,
                   string stat = "", int statChg = 0, bool hc = false, int min = 1, int max = 1)
            {
                var m = _movePool.FirstOrDefault(x => x.Name == name);
                if (m == null) return;
                if (ailment != "")  { m.EffectAilment = ailment; m.EffectChance = chance; }
                if (drain != 0)       m.DrainPercent = drain;
                if (stat != "")     { m.StatTarget = stat; m.StatStageChange = statChg;
                                      if (m.EffectChance == 0 && chance > 0) m.EffectChance = chance; }
                if (hc)               m.HighCrit = true;
                if (max > 1)        { m.MinHits = min; m.MaxHits = max; }
            }

            // ── 火 ──────────────────────────────────────────────────
            E("火焰放射", ailment:"burn",  chance:10);
            E("大字爆",   ailment:"burn",  chance:10);
            E("火焰輪",   ailment:"burn",  chance:10);
            E("熱風",     ailment:"burn",  chance:10);
            E("火焰齒",   ailment:"burn",  chance:10);
            E("熾焰決戰", stat:"self_spatk", statChg:-2);
            E("旭日一擊", ailment:"burn",  chance:10);
            E("火炎旋渦", ailment:"burn",  chance:10);
            E("沸水",     ailment:"burn",  chance:30);          // Scald 特色：30%燒傷

            // ── 水 ──────────────────────────────────────────────────
            E("泡沫光線", stat:"foe_spd", statChg:-1, chance:10);

            // ── 電 ──────────────────────────────────────────────────
            E("十萬伏特", ailment:"para", chance:10);
            E("打雷",     ailment:"para", chance:30);
            E("雷電拳",   ailment:"para", chance:10);
            E("野蠻電力", drain:-25);                            // 後座力 25%
            E("放電",     ailment:"para", chance:30);
            E("電磁炮",   ailment:"para", chance:50);
            E("伏特替換", ailment:"para", chance:10);

            // ── 草 ──────────────────────────────────────────────────
            E("剃刀葉",   hc:true);
            E("奇異植物", drain:50);                             // 吸血 50%
            E("葉片風暴", stat:"self_spatk", statChg:-2);
            E("花瓣舞",   stat:"self_spatk", statChg:-1);        // 簡化：用完降特攻

            // ── 冰 ──────────────────────────────────────────────────
            E("冰凍光線", ailment:"freeze", chance:10);
            E("暴風雪",   ailment:"freeze", chance:10);
            E("冰拳",     ailment:"freeze", chance:10);
            E("冰柱墜落", ailment:"flinch", chance:30);
            E("冰凍乾燥", ailment:"freeze", chance:10);
            E("霧化",     stat:"foe_spd",   statChg:-1);
            E("極寒之地", ailment:"freeze", chance:10);
            E("冰封世界", ailment:"freeze", chance:10);

            // ── 格鬥 ──────────────────────────────────────────────
            E("近身格鬥", stat:"self_def", statChg:-1);
            E("超強力",   stat:"self_atk", statChg:-1);
            E("剪刀十字", hc:true);
            E("腦力衝擊", stat:"foe_spdef", statChg:-1, chance:10);
            E("旋風踢",   ailment:"flinch", chance:30);

            // ── 超能力 ──────────────────────────────────────────────
            E("精神力",   stat:"foe_spdef", statChg:-1, chance:10);
            E("念力射線", stat:"foe_spatk", statChg:-1, chance:10);
            E("夢幻之吻", drain:50);

            // ── 龍 ──────────────────────────────────────────────────
            E("龍之隕石", stat:"self_spatk", statChg:-2);
            E("神秘龍脈", stat:"self_spatk", statChg:-2);
            E("逆鱗",     stat:"self_def",   statChg:-1);        // 簡化：用完降防
            E("龍爪",     hc:true);
            E("龍之怒",   hc:true);

            // ── 惡 ──────────────────────────────────────────────────
            E("咬碎",     stat:"foe_def",   statChg:-1, chance:20);
            E("橫掃千軍", hc:true);
            E("夜斬",     hc:true);
            E("奸詐拳",   stat:"foe_atk",   statChg:-1, chance:10);

            // ── 幽靈 ──────────────────────────────────────────────
            E("影子球",   stat:"foe_spdef", statChg:-1, chance:20);
            E("影爪",     ailment:"flinch", chance:10);
            E("幻影突擊", ailment:"flinch", chance:20);

            // ── 岩石 ──────────────────────────────────────────────
            E("古代力量", stat:"self_atk",  statChg:1, chance:10); // 10%全升（簡化為攻擊）
            E("猛岩炮彈", hc:true);
            E("礫石衝",   min:2, max:5);                          // 2-5連打

            // ── 地面 ──────────────────────────────────────────────
            E("大地之力", stat:"foe_spdef", statChg:-1, chance:10);
            E("土撥球",   stat:"foe_spd",   statChg:-1);

            // ── 飛行 ──────────────────────────────────────────────
            E("勇鳥急衝", drain:-33);                             // 後座力 33%
            E("颶風",     ailment:"para",   chance:30);           // 簡化：30%麻痺
            E("空氣切割", ailment:"flinch", chance:30);

            // ── 蟲 ──────────────────────────────────────────────
            E("蟲鳴",     stat:"foe_spdef", statChg:-1, chance:10);
            E("信號束",   ailment:"para",   chance:10);
            E("蟲咬",     ailment:"flinch", chance:10);

            // ── 毒 ──────────────────────────────────────────────
            E("毒菌炸彈", ailment:"poison", chance:30);
            E("毒刺",     ailment:"poison", chance:30);
            E("骯臟射擊", ailment:"poison", chance:30);
            E("污泥炸彈", ailment:"poison", chance:30);
            E("毒素衝擊", ailment:"poison", chance:30);
            E("酸液",     stat:"foe_spdef", statChg:-2);          // Acid Spray：必降特防2段

            // ── 鋼 ──────────────────────────────────────────────
            E("鐵頭",     ailment:"flinch", chance:30);
            E("子彈拳",   ailment:"flinch", chance:10);

            // ── 一般 ──────────────────────────────────────────────
            E("身體猛撞", ailment:"para",   chance:30);
            E("劈斬",     hc:true);

            // ── 妖精 ──────────────────────────────────────────────
            E("月亮之力", stat:"foe_spatk", statChg:-1, chance:30);
            E("粗野播弄", stat:"foe_atk",   statChg:-1, chance:10);
            E("夢幻接觸", stat:"foe_atk",   statChg:-1, chance:10);
        }
        #endregion

        #region 建構子 / 初始化
        // ── Constructor ────────────────────────────────────────
        public PokeTowerService(string redisConnectionString = null, OpenRouterService aiService = null, GetChampService champService = null)
        {
            InitMoveEffects();
            if (!string.IsNullOrWhiteSpace(redisConnectionString))
            {
                try
                {
                    var redis = ConnectionMultiplexer.Connect(
                        redisConnectionString + ",ConnectTimeout=10000,abortConnect=false,ConnectRetry=3");
                    _redisDb = redis.GetDatabase();
                    _useRedis = true;
                }
                catch (Exception ex) { Console.WriteLine($"[Tower] Redis 連線失敗: {ex.Message}"); }
            }
            _aiService = aiService;
            _champService = champService;
            _ = LoadRunsAsync();
        }

        #endregion

        #region 核心爬塔流程（開始 / 路徑選擇 / 被動）

        public bool HasActiveRun(ulong channelId) => _activeRuns.ContainsKey(channelId);
        public TowerRun GetRun(ulong channelId) => _activeRuns.TryGetValue(channelId, out var r) ? r : null;

        /// <summary>顯示 Pokemon 選擇畫面（最多顯示10隻）</summary>
        public (Embed embed, ComponentBuilder component) ShowPokemonSelection(
            ulong channelId, ulong playerId, string playerName, List<PokeGamePokemon> pokemons)
        {
            if (_activeRuns.TryGetValue(channelId, out var existing))
                return (new EmbedBuilder()
                    .WithTitle("❌ 此頻道已有爬塔進行中")
                    .WithDescription($"**{existing.PlayerName}** 正在第 {existing.CurrentFloor} 層（共 {existing.MaxFloor} 層）。")
                    .WithColor(Color.Red).Build(), new ComponentBuilder());

            if (pokemons == null || pokemons.Count == 0)
                return (new EmbedBuilder()
                    .WithTitle("😅 你還沒有寶可夢")
                    .WithDescription("先用 `/抓pokemon` 抓一隻再來挑戰爬塔！")
                    .WithColor(Color.Orange).Build(), new ComponentBuilder());

            var showList = pokemons.Take(10).ToList();
            var embed = new EmbedBuilder()
                .WithTitle("🏔️ 寶可夢爬塔 — 選擇你的先發")
                .WithDescription(
                    "選一隻帶入爬塔。\n" +
                    "**HP 與技能 PP 全程保留**，打倒的敵人可以捕獲，背包最多 **3 隻**。\n\n" +
                    "共 **20 層**，第 20 層是 Boss，加油！")
                .WithColor(new Color(70, 130, 180))
                .WithFooter($"{playerName} 的爬塔申請");

            for (int i = 0; i < showList.Count; i++)
            {
                var p = showList[i];
                var shiny = p.isShiny ? " ✨" : "";
                embed.AddField(
                    $"{i + 1}. {p.CustomName ?? p.Name}{shiny} {TypeBadge(p.Types)}",
                    $"HP {p.HP} | ATK {p.Attack} | DEF {p.Defense} | SPD {p.Speed}",
                    inline: true);
            }

            var cb = new ComponentBuilder();
            for (int i = 0; i < showList.Count; i++)
            {
                var p = showList[i];
                cb.WithButton(p.CustomName ?? p.Name,
                    $"tower_select_{channelId}_{playerId}_{i}",
                    ButtonStyle.Primary, row: i / 5);
            }
            return (embed.Build(), cb);
        }

        /// <summary>開始爬塔</summary>
        public async Task<(Embed embed, ComponentBuilder component)> StartRunAsync(
            ulong channelId, ulong playerId, string playerName, PokeGamePokemon src, string passiveId = "")
        {
            var pokemon = ConvertPokemon(src);
            pokemon.Moves = await FetchMovesFromApiAsync(src.Id, src.Types?.ToList() ?? new());

            var run = new TowerRun
            {
                PlayerId = playerId,
                PlayerName = playerName,
                ChannelId = channelId,
                ActivePokemon = pokemon,
                State = TowerRunState.SelectingPath,
                PassiveId = passiveId,
            };
            run.Party.Add(pokemon);
            run.RunLog.Add($"🏔️ {playerName} 帶著 {pokemon.DisplayName} 踏入爬塔！");

            // Apply immediate passive effects
            if (!string.IsNullOrEmpty(passiveId))
            {
                switch (passiveId)
                {
                    case "passive_catchmaster":
                        run.Balls["ultra"] = run.Balls.GetValueOrDefault("ultra") + 3;
                        break;
                    case "passive_richboy":
                        run.Gold += 80;
                        break;
                    case "passive_tanker":
                        foreach (var p in run.Party) { p.MaxHP = (int)(p.MaxHP * 1.4f); p.Speed = (int)(p.Speed * 0.8f); p.CurrentHP = p.MaxHP; }
                        break;
                    case "passive_chaosmaster":
                        // 效果純戰鬥時觸發，開局無需改數值
                        break;
                    case "passive_berserker":
                        foreach (var p in run.Party) { p.Attack = (int)(p.Attack * 1.3f); p.SpecialAttack = (int)(p.SpecialAttack * 1.3f); p.Defense = (int)(p.Defense * 0.8f); p.SpecialDefense = (int)(p.SpecialDefense * 0.8f); }
                        break;
                }
                var pv = _passives.FirstOrDefault(x => x.Id == passiveId);
                if (pv != null) run.RunLog.Add($"🌟 被動技能【{pv.Emoji}{pv.Name}】已啟動！");
            }

            _activeRuns[channelId] = run;
            await SaveAsync(run);
            return BuildPathEmbed(run);
        }

        /// <summary>顯示被動技能選擇畫面（爬塔前呼叫）</summary>
        public (Embed embed, ComponentBuilder component) ShowPassiveSelectionAsync(
            ulong channelId, ulong playerId, string playerName, PokeGamePokemon src)
        {
            // Check for existing active run
            if (_activeRuns.TryGetValue(channelId, out var existing))
                return (new EmbedBuilder()
                    .WithTitle("❌ 此頻道已有爬塔進行中")
                    .WithDescription($"**{existing.PlayerName}** 正在第 {existing.CurrentFloor} 層（共 {existing.MaxFloor} 層）。")
                    .WithColor(Color.Red).Build(), new ComponentBuilder());

            _pendingStarts[channelId] = (playerId, playerName, src);
            return BuildPassiveSelectionEmbed(channelId);
        }

        /// <summary>玩家選擇被動後正式開始爬塔</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandlePassiveChoiceAsync(ulong channelId, int idx)
        {
            if (!_pendingStarts.TryGetValue(channelId, out var pending))
                return ErrEmbed("找不到待開始的爬塔，請重新使用 /pokemon爬塔。");

            if (idx < 0 || idx >= _passives.Count)
                return ErrEmbed("無效的被動選擇");

            var chosen = _passives[idx];
            _pendingStarts.Remove(channelId);
            return await StartRunAsync(channelId, pending.PlayerId, pending.PlayerName, pending.Src, chosen.Id);
        }

        private (Embed embed, ComponentBuilder component) BuildPassiveSelectionEmbed(ulong channelId)
        {
            var desc = new StringBuilder();
            desc.AppendLine("踏入爬塔前，選擇一個**被動技能**加持你的旅程！");
            desc.AppendLine();
            for (int i = 0; i < _passives.Count; i++)
            {
                var pv = _passives[i];
                desc.AppendLine($"**{i + 1}. {pv.Emoji} {pv.Name}**");
                desc.AppendLine($"　　{pv.Desc}");
                desc.AppendLine();
            }

            var embed = new EmbedBuilder()
                .WithTitle("🌟 選擇被動技能")
                .WithDescription(desc.ToString())
                .WithColor(new Color(255, 180, 0))
                .WithFooter("選擇後不可更改，請仔細考慮！")
                .Build();

            var cb = new ComponentBuilder();
            for (int i = 0; i < _passives.Count; i++)
            {
                var pv = _passives[i];
                cb.WithButton($"{pv.Emoji}{pv.Name}", $"tower_passive_{channelId}_{i}", ButtonStyle.Primary, row: i / 5);
            }
            return (embed, cb);
        }

        /// <summary>選擇路徑</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandlePathChoiceAsync(
            ulong channelId, string choice)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            run.CurrentFloor++;
            run.CurrentPaths = null;
            bool isBoss = run.CurrentFloor % 10 == 0;

            // curse_gold_tax: deduct 10 gold on floor entry
            if (run.CursedRelicIds.Contains("curse_gold_tax") && run.Gold > 0)
                run.Gold = Math.Max(0, run.Gold - 10);

            // relic_hourglass: heal on floor entry
            if (HasRelic(run, "relic_hourglass"))
            {
                int hourglass = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.05f));
                run.ActivePokemon.CurrentHP = Math.Min(run.ActivePokemon.MaxHP, run.ActivePokemon.CurrentHP + hourglass);
            }

            // curse_forget: randomly replace a move on floor entry
            if (run.CursedRelicIds.Contains("curse_forget") && run.CurrentFloor > 1)
            {
                var cpoke = run.ActivePokemon;
                int slot = _rng.Next(cpoke.Moves.Count);
                var newMovePool = PickMovesStatic(cpoke.Types);
                var newMove = newMovePool.FirstOrDefault(m => cpoke.Moves.All(e => e.Name != m.Name)) ?? newMovePool[0];
                string oldName = cpoke.Moves[slot].Name;
                cpoke.Moves[slot] = new TowerMove { Name=newMove.Name, Type=newMove.Type, Power=newMove.Power, Category=newMove.Category, Emoji=newMove.Emoji, MaxPP=newMove.MaxPP, CurrentPP=newMove.MaxPP };
                run.RunLog.Add($"💀 遺忘詛咒：{cpoke.DisplayName} 忘掉了【{oldName}】，學會了【{newMove.Name}】！");
            }

            if (choice == "battle" || isBoss)
            {
                run.CurrentEnemy = GenEnemy(run.CurrentFloor, isBoss);
                run.CurrentBattleLog = "";
                run.State = TowerRunState.InBattle;
                run.RunLog.Add($"⚔️ 第{run.CurrentFloor}層：遭遇 {run.CurrentEnemy.Name}！");
                // Reset per-battle relic flags
                run.ShieldActive = HasRelic(run, "relic_shield");
                run.WillUsed = false;
                run.AvengeStacks = 0;
                // 重置戰鬥狀態（每場戰鬥清空）
                foreach (var pk in run.Party)
                {
                    pk.BattleStatus = ""; pk.SleepTurns = 0;
                    pk.AtkStage = pk.DefStage = pk.SpdStage = pk.SpAtkStage = pk.SpDefStage = 0;
                }
                run.CurrentEnemy.BattleStatus = ""; run.CurrentEnemy.SleepTurns = 0;
                run.CurrentEnemy.AtkStage = run.CurrentEnemy.DefStage = run.CurrentEnemy.SpdStage =
                run.CurrentEnemy.SpAtkStage = run.CurrentEnemy.SpDefStage = 0;
                run.CurrentEnemy.Flinched = false;
                // relic_time_warp: restore 3 PP to all moves at battle start
                if (HasRelic(run, "relic_time_warp"))
                    foreach (var mv in run.ActivePokemon.Moves) mv.CurrentPP = Math.Min(mv.MaxPP, mv.CurrentPP + 3);
                await SaveAsync(run);
                return BuildBattleEmbed(run);
            }
            if (choice == "rest")
            {
                int hp = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.35));
                foreach (var pk in run.Party)
                {
                    int pkHeal = Math.Max(1, (int)(pk.MaxHP * 0.35));
                    pk.CurrentHP = Math.Min(pk.MaxHP, pk.CurrentHP + pkHeal);
                    foreach (var m in pk.Moves) m.CurrentPP = m.MaxPP;
                }
                // sync ActivePokemon in case it diverged from Party after Redis load
                run.ActivePokemon.CurrentHP = Math.Min(run.ActivePokemon.MaxHP,
                    run.ActivePokemon.CurrentHP + Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.35)));
                foreach (var m in run.ActivePokemon.Moves) m.CurrentPP = m.MaxPP;
                run.RunLog.Add($"🏕️ 第{run.CurrentFloor}層：全隊休息恢復 35%HP + PP全回復");
                run.State = TowerRunState.Resting;
                await SaveAsync(run);
                return BuildRestEmbed(run, hp);
            }
            if (choice == "shop")
            {
                run.State = TowerRunState.Shopping;
                await SaveAsync(run);
                return BuildShopEmbed(run);
            }
            if (choice == "event")
            {
                var available = Enumerable.Range(0, _events.Count)
                    .Where(i => !run.UsedEventIndices.Contains(i)).ToList();
                if (available.Count == 0) { run.UsedEventIndices.Clear(); available = Enumerable.Range(0, _events.Count).ToList(); }
                run.PendingEventIdx = available[_rng.Next(available.Count)];
                // If math event, generate the problem
                if (_events[run.PendingEventIdx].Title == "神秘數學題")
                    GenerateMathEvent(run);
                run.State = TowerRunState.SelectingEvent;
                await SaveAsync(run);
                return BuildEventEmbed(run);
            }
            if (choice == "casino")
            {
                run.State = TowerRunState.InCasino;
                run.CasinoRound = 0;
                run.CasinoProfit = 0;
                await SaveAsync(run);
                return BuildCasinoEmbed(run);
            }
            if (choice == "cursed_relic")
            {
                var available = _cursedRelics.Where(r => !run.CursedRelicIds.Contains(r.Id)).ToList();
                if (available.Count == 0) available = _cursedRelics.ToList();
                run.PendingCursedRelicChoices = available.OrderBy(_ => _rng.Next()).Take(3).Select(r => r.Id).ToList();
                run.State = TowerRunState.SelectingCursedRelic;
                await SaveAsync(run);
                return BuildCursedRelicEmbed(run);
            }
            return ErrEmbed("未知的路徑選擇");
        }

        #endregion

        #region 戰鬥系統

        /// <summary>戰鬥選技能</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleMoveAsync(
            ulong channelId, int moveIdx)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");
            if (run.State != TowerRunState.InBattle)
                return ErrEmbed("目前不在戰鬥中");

            var poke = run.ActivePokemon;
            var enemy = run.CurrentEnemy;

            // curse_bleed: lose 5% MaxHP at start of turn
            if (run.CursedRelicIds.Contains("curse_bleed"))
                poke.CurrentHP = Math.Max(1, poke.CurrentHP - Math.Max(1, (int)(poke.MaxHP * 0.05f)));

            // Per-turn relic effects (start of turn)
            if (HasRelic(run, "relic_regen"))
            {
                int regen = Math.Min(20, Math.Max(1, (int)(poke.MaxHP * 0.03f)));
                poke.CurrentHP = Math.Min(poke.MaxHP, poke.CurrentHP + regen);
            }
            if (HasRelic(run, "relic_blood"))
            {
                int bleed = Math.Max(1, (int)(poke.MaxHP * 0.03f));
                poke.CurrentHP = Math.Max(1, poke.CurrentHP - bleed);
            }
            if (HasRelic(run, "relic_berserk") && poke.CurrentHP * 2 < poke.MaxHP)
            {
                foreach (var mv in poke.Moves) mv.CurrentPP = Math.Min(mv.MaxPP, mv.CurrentPP + 2);
            }
            // relic_will: if all moves at 0 PP and not used yet this battle
            if (HasRelic(run, "relic_will") && !run.WillUsed && poke.Moves.All(m => m.CurrentPP <= 0))
            {
                foreach (var mv in poke.Moves) mv.CurrentPP = mv.MaxPP;
                run.WillUsed = true;
            }

            // 掙扎機制：所有PP歸零時
            TowerMove playerMove;
            if (moveIdx >= 0 && moveIdx < poke.Moves.Count && poke.Moves[moveIdx].CurrentPP > 0)
            {
                playerMove = poke.Moves[moveIdx];
                if (!(HasRelic(run, "relic_no_pp") && _rng.Next(100) < 25))
                    playerMove.CurrentPP--;
            }
            else
            {
                playerMove = new TowerMove { Name="掙扎", Type="一般", Power=50, Category="Physical", Emoji="😤", MaxPP=1, CurrentPP=1 };
            }
            // curse_move_random: 20% chance player uses a random move
            if (run.CursedRelicIds.Contains("curse_move_random") && _rng.Next(100) < 20)
            {
                int ri = _rng.Next(poke.Moves.Count);
                playerMove = poke.Moves[ri];
                if (!(HasRelic(run, "relic_no_pp") && _rng.Next(100) < 25) && playerMove.CurrentPP > 0)
                    playerMove.CurrentPP--;
            }
            // curse_mirror: 50% chance player uses a random move
            if (run.CursedRelicIds.Contains("curse_mirror") && _rng.Next(2) == 0)
            {
                int randomSlot = _rng.Next(poke.Moves.Count);
                playerMove = poke.Moves[randomSlot];
            }

            var enemyMove = enemy.Moves[enemy.NextMoveIdx % enemy.Moves.Count];

            bool playerFirst = poke.Speed >= enemy.Speed; // 後面的速度計算會覆蓋此值
            // 計算目前第幾回合（現有 rounds 數 + 1）
            int roundNum = run.CurrentBattleLog.Split(new[] { "════════" }, StringSplitOptions.RemoveEmptyEntries)
                               .Count(r => r.Trim().Length > 0) + 1;
            var sb = new StringBuilder();
            sb.AppendLine($"【回合 {roundNum}】");

            // ── 局部函式：玩家攻擊一次 ───────────────────────────────
            void DoPlayerAttack()
            {
                // 狀態判斷
                if (poke.BattleStatus == "para" && _rng.Next(100) < 25)
                    { sb.AppendLine($"⚡ {poke.DisplayName} 麻痺無法行動！"); return; }
                if (poke.BattleStatus == "freeze")
                {
                    if (_rng.Next(100) < 20) { sb.AppendLine($"❄️ {poke.DisplayName} 解凍了！"); poke.BattleStatus = ""; }
                    else { sb.AppendLine($"❄️ {poke.DisplayName} 被凍住無法行動！"); return; }
                }
                if (poke.BattleStatus == "sleep")
                {
                    if (poke.SleepTurns <= 0) { sb.AppendLine($"💤 {poke.DisplayName} 醒來了！"); poke.BattleStatus = ""; }
                    else { poke.SleepTurns--; sb.AppendLine($"💤 {poke.DisplayName} 在睡覺，無法行動！"); return; }
                }

                // 傷害計算（套用 stat stage）
                int effAtk   = (int)(poke.Attack        * StatMult(poke.AtkStage  + (poke.BattleStatus=="burn" ? -1 : 0)));
                int effSpAtk = (int)(poke.SpecialAttack  * StatMult(poke.SpAtkStage));
                int effDef   = (int)(enemy.Defense       * StatMult(enemy.DefStage));
                int effSpDef = (int)(enemy.SpecialDefense* StatMult(enemy.SpDefStage));

                int hits = playerMove.MaxHits > 1 ? _rng.Next(playerMove.MinHits, playerMove.MaxHits + 1) : 1;
                int d = 0;
                for (int h = 0; h < hits; h++)
                    d += CalcDamage(playerMove, effAtk, effSpAtk, effDef, effSpDef, enemy.Types);
                if (hits > 1) sb.AppendLine($"  (連打 {hits} 次！)");

                // Relic damage modifiers
                int critThreshold = 15 + (HasRelic(run, "relic_lucky_charm") ? 15 : 0) - (run.CursedRelicIds.Contains("curse_unlucky") ? 15 : 0);
                if (HasRelic(run, "relic_crit") && _rng.Next(100) < Math.Max(1, critThreshold)) d = d * 2;
                int noDefThreshold = 20 + (HasRelic(run, "relic_lucky_charm") ? 15 : 0);
                if (HasRelic(run, "relic_no_def") && _rng.Next(100) < noDefThreshold)
                    d = Math.Max(d, (int)(playerMove.Power * (playerMove.Category == "Physical" ? effAtk : effSpAtk) / 5.0f));
                if (HasRelic(run, "relic_boss_dmg") && enemy.IsBoss) d = (int)(d * 1.5f);
                if (HasRelic(run, "relic_fullhp") && poke.CurrentHP == poke.MaxHP) d = (int)(d * 1.3f);
                if (HasRelic(run, "relic_amplify")) d = (int)(d * 1.3f);
                if (HasRelic(run, "relic_blood")) d = (int)(d * 1.3f);
                if (HasRelic(run, "relic_enrage") && poke.CurrentHP * 100 / Math.Max(1, poke.MaxHP) < 30) d = (int)(d * 1.6f);
                if (HasRelic(run, "relic_berserker_r") && poke.CurrentHP * 2 < poke.MaxHP) d = (int)(d * 1.4f);
                if (HasRelic(run, "relic_double_edge")) d = (int)(d * 1.4f);
                if (HasRelic(run, "relic_executioner") && enemy.CurrentHP * 4 < enemy.MaxHP) d *= 2;
                if (HasRelic(run, "relic_comeback") && poke.CurrentHP * 10 < poke.MaxHP) d *= 3;
                if (run.CursedRelicIds.Contains("curse_weaken") && playerMove.UpgradeCount > 0) d /= 2;
                if (HasRelic(run, "relic_chain") && run.ChainBonus > 0) d = (int)(d * (1f + run.ChainBonus));
                bool avengeProc = HasRelic(run, "relic_avenge") && run.AvengeStacks >= 3;
                if (avengeProc) { d = d * 2; run.AvengeStacks = 0; }

                enemy.CurrentHP = Math.Max(0, enemy.CurrentHP - d);
                if (HasRelic(run, "relic_double_edge")) poke.CurrentHP = Math.Max(1, poke.CurrentHP - Math.Max(1, (int)(d * 0.15f)));
                run.TotalDamageDealt += d;
                AppendHit(sb, poke.DisplayName, enemy.Name, playerMove, d, enemy.Types, true);

                // Post-attack relics
                if (HasRelic(run, "relic_lifesteal")) { int ls = Math.Max(1, (int)(d * 0.20f)); poke.CurrentHP = Math.Min(poke.MaxHP, poke.CurrentHP + ls); }
                if (HasPassive(run, "passive_vampire") && !HasRelic(run, "relic_lifesteal")) { int ls = Math.Max(1, (int)(d * 0.30f)); poke.CurrentHP = Math.Min(poke.MaxHP, poke.CurrentHP + ls); }
                if (HasRelic(run, "relic_poison") && enemy.CurrentHP > 0) enemy.CurrentHP = Math.Max(0, enemy.CurrentHP - 15);

                // 技能效果（狀態/stat/吸血）
                if (enemy.CurrentHP > 0 || playerMove.DrainPercent > 0)
                    ApplyMoveEffect(playerMove, true, poke, enemy, d, sb, run);
            }

            // ── 局部函式：敵人攻擊一次 ───────────────────────────────
            void DoEnemyAttack()
            {
                // 畏縮 / 狀態判斷
                if (enemy.Flinched) { enemy.Flinched = false; sb.AppendLine($"  😨 {enemy.Name} 因畏縮跳過攻擊！"); return; }
                if (enemy.BattleStatus == "para" && _rng.Next(100) < 25)
                    { sb.AppendLine($"⚡ {enemy.Name} 麻痺無法行動！"); return; }
                if (enemy.BattleStatus == "freeze")
                {
                    if (_rng.Next(100) < 20) { sb.AppendLine($"❄️ {enemy.Name} 解凍了！"); enemy.BattleStatus = ""; }
                    else { sb.AppendLine($"❄️ {enemy.Name} 被凍住無法行動！"); return; }
                }
                if (enemy.BattleStatus == "sleep")
                {
                    if (enemy.SleepTurns <= 0) { sb.AppendLine($"💤 {enemy.Name} 醒來了！"); enemy.BattleStatus = ""; }
                    else { enemy.SleepTurns--; sb.AppendLine($"💤 {enemy.Name} 在睡覺，無法行動！"); return; }
                }

                // 傷害計算（套用 stat stage）
                int effEAtk   = (int)(enemy.Attack        * StatMult(enemy.AtkStage  + (enemy.BattleStatus=="burn" ? -1 : 0)));
                int effESpAtk = (int)(enemy.SpecialAttack  * StatMult(enemy.SpAtkStage));
                int effPDef   = (int)(poke.Defense         * StatMult(poke.DefStage));
                int effPSpDef = (int)(poke.SpecialDefense  * StatMult(poke.SpDefStage));

                int ed = CalcDamage(enemyMove, effEAtk, effESpAtk, effPDef, effPSpDef, poke.Types);

                // Shield check
                if (HasRelic(run, "relic_shield") && run.ShieldActive) { ed = 0; run.ShieldActive = false; }
                // Last stand
                if (HasRelic(run, "relic_last_stand") && poke.CurrentHP * 100 / Math.Max(1, poke.MaxHP) < 20) ed = ed / 2;
                // passive_ironwall: -20% damage taken
                if (HasPassive(run, "passive_ironwall")) ed = (int)(ed * 0.8f);

                poke.CurrentHP = Math.Max(0, poke.CurrentHP - ed);
                AppendHit(sb, enemy.Name, poke.DisplayName, enemyMove, ed, poke.Types, false);

                // Thorns / shared_pain
                if (HasRelic(run, "relic_thorns") && ed > 0) enemy.CurrentHP = Math.Max(0, enemy.CurrentHP - Math.Min(25, (int)(ed * 0.25f)));
                if (HasRelic(run, "relic_shared_pain") && ed > 0) enemy.CurrentHP = Math.Max(0, enemy.CurrentHP - Math.Max(1, (int)(ed * 0.3f)));
                // curse_fragile2
                if (run.CursedRelicIds.Contains("curse_fragile2") && ed > 0) poke.Defense = Math.Max(1, poke.Defense - 3);
                // curse_hungry
                if (run.CursedRelicIds.Contains("curse_hungry"))
                    foreach (var mv in poke.Moves) mv.CurrentPP = Math.Max(0, mv.CurrentPP - 1);
                // Avenge
                if (HasRelic(run, "relic_avenge") && ed > 0) run.AvengeStacks++;

                // 敵方技能效果（可對玩家造成狀態）
                if (poke.CurrentHP > 0 || enemyMove.DrainPercent > 0)
                    ApplyMoveEffect(enemyMove, false, poke, enemy, ed, sb, run);
            }

            // ── 出手順序 ───────────────────────────────────────────
            // 麻痺降速（速度減半算入先後手）
            int pSpeed = poke.BattleStatus == "para" ? poke.Speed / 2 : (int)(poke.Speed * StatMult(poke.SpdStage));
            int eSpeed = enemy.BattleStatus == "para" ? enemy.Speed / 2 : (int)(enemy.Speed * StatMult(enemy.SpdStage));
            if (HasRelic(run, "relic_swift")) pSpeed = (int)(pSpeed * 1.3f);
            if (HasPassive(run, "passive_firstblood")) playerFirst = true;
            else playerFirst = pSpeed >= eSpeed;

            if (playerFirst)
            {
                DoPlayerAttack();
                if (enemy.CurrentHP > 0) DoEnemyAttack();
            }
            else
            {
                DoEnemyAttack();
                if (poke.CurrentHP > 0) DoPlayerAttack();
            }

            // ── 回合末狀態傷害（燒傷/毒）────────────────────────────
            ApplyEndOfTurnStatus(poke, enemy, sb);

            enemy.NextMoveIdx = (enemy.NextMoveIdx + 1) % enemy.Moves.Count;

            // 累積戰鬥紀錄，只保留最新 3 回合
            const string RSEP = "════════";
            var newRound = sb.ToString().Trim();
            var existing = run.CurrentBattleLog;
            var rounds = existing.Split(new[] { RSEP }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
            rounds.Add(newRound);
            if (rounds.Count > 3) rounds = rounds.TakeLast(3).ToList();
            run.CurrentBattleLog = string.Join($"\n{RSEP}\n", rounds);

            // Check end
            if (enemy.CurrentHP <= 0)
            {
                // curse_gold_drain: drain gold instead of gaining
                if (run.CursedRelicIds.Contains("curse_gold_drain"))
                    run.Gold = Math.Max(0, run.Gold - Math.Max(1, (int)(run.Gold * 0.1f)));
                else
                    run.Gold += enemy.GoldReward;
                // relic_gold_mine: extra gold on kill
                if (HasRelic(run, "relic_gold_mine")) run.Gold += 20;

                // relic_kill_pp
                if (HasRelic(run, "relic_kill_pp"))
                    foreach (var mv in poke.Moves) mv.CurrentPP = Math.Min(mv.MaxPP, mv.CurrentPP + 3);
                // relic_chain
                if (HasRelic(run, "relic_chain")) run.ChainBonus += 0.05f;
                // relic_feast: restore 50 HP
                if (HasRelic(run, "relic_feast")) poke.CurrentHP = Math.Min(poke.MaxHP, poke.CurrentHP + 50);
                // relic_parasite: +5 max HP
                if (HasRelic(run, "relic_parasite")) { poke.MaxHP += 5; poke.CurrentHP += 5; }

                // 回復 10% HP
                int heal = Math.Max(1, poke.MaxHP / 10);
                poke.CurrentHP = Math.Min(poke.MaxHP, poke.CurrentHP + heal);

                // 獲得 EXP
                int expGain = 15 + run.CurrentFloor * 8;
                if (HasPassive(run, "passive_genius")) expGain *= 2;
                if (HasRelic(run, "relic_exp_boost")) expGain = (int)(expGain * 1.5f);
                if (run.CursedRelicIds.Contains("curse_exp_drain")) expGain = (int)(expGain * 0.5f);
                run.Exp += expGain;
                var levelUpMsg = new StringBuilder();
                while (run.Exp >= run.ExpToNext)
                {
                    run.Exp -= run.ExpToNext;
                    run.Level++;
                    foreach (var member in run.Party)
                    {
                        member.Attack          = (int)(member.Attack          * 1.10f);
                        member.Defense         = (int)(member.Defense         * 1.10f);
                        member.SpecialAttack   = (int)(member.SpecialAttack   * 1.10f);
                        member.SpecialDefense  = (int)(member.SpecialDefense  * 1.10f);
                        member.Speed           = (int)(member.Speed           * 1.10f);
                        int hpBoost = Math.Max(1, (int)(member.MaxHP * 0.10f));
                        member.MaxHP     += hpBoost;
                        member.CurrentHP += hpBoost;
                    }
                    // relic_scholar: +5 PP to all moves on level up
                    if (HasRelic(run, "relic_scholar"))
                        foreach (var mv in poke.Moves) { mv.MaxPP += 5; mv.CurrentPP = Math.Min(mv.MaxPP, mv.CurrentPP + 5); }
                    levelUpMsg.Append($" ⬆️ **Lv.{run.Level}！全隊能力上升！**");
                }

                run.CurrentBattleLog += $"\n❤️ {poke.DisplayName} 恢復 {heal} HP | ✨ 獲得 {expGain} EXP{levelUpMsg}";
                run.RunLog.Add($"✅ 第{run.CurrentFloor}層：擊倒 {enemy.Name}，獲得 {enemy.GoldReward}💰，+{expGain} EXP");

                if (run.CurrentFloor >= run.MaxFloor)
                {
                    run.State = TowerRunState.Victory;
                    await RemoveAsync(channelId);
                    // Grant shiny reward for the player's next catch
                    PendingShinyUserIds.Add(run.PlayerId);
                    if (_useRedis)
                        _ = _redisDb.StringSetAsync($"{SHINY_KEY_PREFIX}{run.PlayerId}", "1", TimeSpan.FromDays(30));
                    return BuildVictoryEmbed(run);
                }

                // Every 5 floors: relic selection
                if (run.CurrentFloor % 5 == 0)
                {
                    var available = _relics.Where(r => !run.SeenRelicIds.Contains(r.Id)).ToList();
                    if (available.Count == 0) { run.SeenRelicIds.Clear(); available = _relics.ToList(); }
                    run.PendingRelicChoices = available.OrderBy(_ => _rng.Next()).Take(3).Select(r => r.Id).ToList();
                    run.PendingRelicChoices.ForEach(id => run.SeenRelicIds.Add(id));
                    run.State = TowerRunState.SelectingRelic;
                    await SaveAsync(run);
                    return BuildRelicEmbed(run);
                }

                // 提供技能獎勵（先讓玩家選強化）
                run.PendingMoveRewards = _movePool.OrderBy(_ => _rng.Next()).Take(3).ToList();
                // 可以捕獲
                run.PendingCatch = enemy;
                run.PowerUpgradeReturn = "battle";
                run.State = TowerRunState.SelectingPowerUpgrade;
                await SaveAsync(run);
                return BuildPowerUpgradeEmbed(run);
            }

            int phoenixLimit = HasPassive(run, "passive_undying") ? 2 : 1;
            if (poke.CurrentHP <= 0 && HasRelic(run, "relic_phoenix") && run.PhoenixUseCount < phoenixLimit)
            {
                poke.CurrentHP = 1;
                run.PhoenixUseCount++;
                run.CurrentBattleLog += $"\n🪶 **不死鳥羽** 發動！以1HP存活！（{run.PhoenixUseCount}/{phoenixLimit}）";
            }

            if (poke.CurrentHP <= 0)
            {
                // Check if can swap to another party member
                var alive = run.Party.FirstOrDefault(p => p.PokeId != poke.PokeId && p.CurrentHP > 0);
                if (alive != null)
                {
                    run.ActivePokemon = alive;
                    run.CurrentBattleLog += $"\n💀 **{poke.DisplayName}** 倒下了！換上 **{alive.DisplayName}**！\n";
                    await SaveAsync(run);
                    return BuildBattleEmbed(run);
                }
                run.State = TowerRunState.Defeated;
                await RemoveAsync(channelId);
                return BuildDefeatEmbed(run);
            }

            await SaveAsync(run);
            return BuildBattleEmbed(run);
        }

        #endregion

        #region 技能獎勵 / 學習

        /// <summary>選擇技能獎勵（0-2=選技能, 3=跳過）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleMoveRewardAsync(
            ulong channelId, int idx)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            if (idx == 3 || idx >= run.PendingMoveRewards.Count)
            {
                // Skip → go back based on origin
                run.PendingMoveRewards.Clear();
                run.PendingSelectedMove = null;
                if (run.ShopMoveRewardPending)
                {
                    run.ShopMoveRewardPending = false;
                    run.State = TowerRunState.Shopping;
                    await SaveAsync(run);
                    return BuildShopEmbed(run, "📀 跳過技能選擇。");
                }
                if (run.EventMoveRewardPending)
                {
                    run.EventMoveRewardPending = false;
                    run.State = TowerRunState.SelectingPath;
                    await SaveAsync(run);
                    return BuildPathEmbed(run, "📀 跳過技能選擇。");
                }
                if (run.RestMoveRewardPending)
                {
                    run.RestMoveRewardPending = false;
                    run.State = TowerRunState.SelectingPath;
                    await SaveAsync(run);
                    return BuildPathEmbed(run);
                }
                await SaveAsync(run);
                return CheckCatch(run);
            }

            // Store selected move, ask which slot to replace
            run.PendingSelectedMove = run.PendingMoveRewards[idx];
            run.State = TowerRunState.SelectingMoveSlot;
            await SaveAsync(run);
            return BuildMoveSlotEmbed(run);
        }

        /// <summary>選擇要替換的技能槽（0-3=槽位, 4=取消）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleMoveSlotAsync(
            ulong channelId, int slot)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            if (slot == 4 || run.PendingSelectedMove == null)
            {
                // Cancel → back to move reward selection
                run.PendingSelectedMove = null;
                run.State = TowerRunState.SelectingMoveReward;
                await SaveAsync(run);
                return BuildMoveRewardEmbed(run);
            }

            string learnedMoveName = "";
            if (slot < run.ActivePokemon.Moves.Count)
            {
                string old = run.ActivePokemon.Moves[slot].Name;
                var src = run.PendingSelectedMove;
                var nm = new TowerMove { Name=src.Name, Type=src.Type, Power=src.Power,
                    Category=src.Category, Emoji=src.Emoji, MaxPP=src.MaxPP, CurrentPP=src.MaxPP };
                run.ActivePokemon.Moves[slot] = nm;
                learnedMoveName = nm.Name;
                run.RunLog.Add($"📀 換掉【{old}】，學會了【{nm.Name}】");
            }

            run.PendingSelectedMove = null;
            run.PendingMoveRewards.Clear();
            if (run.ShopMoveRewardPending)
            {
                run.ShopMoveRewardPending = false;
                run.State = TowerRunState.Shopping;
                await SaveAsync(run);
                return BuildShopEmbed(run, $"📀 學會了【{learnedMoveName}】！");
            }
            if (run.EventMoveRewardPending)
            {
                run.EventMoveRewardPending = false;
                run.State = TowerRunState.SelectingPath;
                await SaveAsync(run);
                return BuildPathEmbed(run, $"📀 學會了【{learnedMoveName}】！");
            }
            if (run.RestMoveRewardPending)
            {
                run.RestMoveRewardPending = false;
                run.State = TowerRunState.SelectingPath;
                await SaveAsync(run);
                return BuildPathEmbed(run);
            }
            await SaveAsync(run);
            return CheckCatch(run);
        }

        private (Embed embed, ComponentBuilder component) CheckCatch(TowerRun run)
        {
            if (run.PendingCatch != null)
            {
                run.State = TowerRunState.SelectingCatch;
                return BuildCatchEmbed(run);
            }
            run.State = TowerRunState.SelectingPath;
            run.CurrentEnemy = null;
            return BuildPathEmbed(run);
        }

        #endregion

        #region 捕獲系統

        /// <summary>投球捕獲（ballKey = "normal"/"super"/"ultra"/"master"/"pass"）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleCatchAsync(
            ulong channelId, string ballKey)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            if (ballKey == "pass")
            {
                string name = run.PendingCatch?.Name ?? "敵人";
                run.PendingCatch = null;
                run.State = TowerRunState.SelectingPath;
                run.CurrentEnemy = null;
                await SaveAsync(run);
                return BuildPathEmbed(run, $"放走了 {name}。");
            }

            if (!_balls.TryGetValue(ballKey, out var ballInfo))
                return ErrEmbed("未知的球種");
            if (!run.Balls.TryGetValue(ballKey, out int ballCount) || ballCount <= 0)
                return BuildCatchEmbed(run, $"⚠️ 沒有 {ballInfo.DisplayName} 了！");

            run.Balls[ballKey]--;
            if (run.Balls[ballKey] == 0) run.Balls.Remove(ballKey);

            // curse_no_catch: cannot catch
            if (run.CursedRelicIds.Contains("curse_no_catch"))
                return BuildCatchEmbed(run, "🔒 **鐵籠詛咒**：無法捕獲任何 Pokemon！");

            float catchRate = ballInfo.Rate;
            if (HasRelic(run, "relic_hunter")) catchRate = Math.Min(1.0f, catchRate + 0.30f);
            if (HasPassive(run, "passive_catchmaster")) catchRate = Math.Min(1.0f, catchRate + 0.40f);
            if (run.CursedRelicIds.Contains("curse_unlucky")) catchRate = Math.Max(0.02f, catchRate - 0.30f);
            bool caught = (float)_rng.NextDouble() < catchRate;
            if (caught)
            {
                var newPoke = CatchFromEnemy(run.PendingCatch);
                int partyCap = HasPassive(run, "passive_packrat") ? 4 : 3;
                if (run.Party.Count < partyCap)
                {
                    run.Party.Add(newPoke);
                    run.PendingCatch = null;
                    run.State = TowerRunState.SelectingPath;
                    run.CurrentEnemy = null;
                    run.RunLog.Add($"🎉 捕獲了 {newPoke.Name}！");
                    await SaveAsync(run);
                    return BuildPathEmbed(run, $"🎉 成功捕獲 **{newPoke.Name}**！（HP: {newPoke.CurrentHP}/{newPoke.MaxHP}）");
                }
                else
                {
                    // 背包滿 → 進入交換選擇
                    run.State = TowerRunState.SelectingCatchSwap;
                    // PendingCatch 仍保留以便交換
                    await SaveAsync(run);
                    return BuildCatchSwapEmbed(run, newPoke);
                }
            }
            else
            {
                string ballsLeft = BallsDisplay(run);
                await SaveAsync(run);
                return BuildCatchEmbed(run, $"{ballInfo.Emoji} 投出 **{ballInfo.DisplayName}**……逃脫了！真是囂張的傢伙（剩餘：{ballsLeft}）");
            }
        }

        /// <summary>背包滿時選擇交換（partyIdx = 0-2 釋放並放入新捕獲，-1 = 取消）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleCatchSwapAsync(
            ulong channelId, int partyIdx)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            var caughtName = run.PendingCatch?.Name ?? "新寶可夢";
            if (partyIdx < 0)
            {
                // 取消 → 釋放剛捕獲的（背包保持原樣）
                run.PendingCatch = null;
                run.State = TowerRunState.SelectingPath;
                run.CurrentEnemy = null;
                await SaveAsync(run);
                return BuildPathEmbed(run, $"放棄了帶走 {caughtName}。");
            }

            if (partyIdx >= run.Party.Count)
                return ErrEmbed("無效的選擇");

            string releasedName = run.Party[partyIdx].DisplayName;
            var newPoke = CatchFromEnemy(run.PendingCatch);

            // 若釋放的是目前上陣中的 → 換成新的
            bool wasActive = run.Party[partyIdx].PokeId == run.ActivePokemon.PokeId
                          && run.Party[partyIdx].CaughtAt == run.ActivePokemon.CaughtAt;
            run.Party[partyIdx] = newPoke;
            if (wasActive) run.ActivePokemon = newPoke;

            run.PendingCatch = null;
            run.State = TowerRunState.SelectingPath;
            run.CurrentEnemy = null;
            run.RunLog.Add($"🔄 釋放了 {releasedName}，捕獲了 {newPoke.Name}！");
            await SaveAsync(run);
            return BuildPathEmbed(run, $"🔄 釋放了 **{releasedName}**，🎉 捕獲了 **{newPoke.Name}**！");
        }

        #endregion

        #region 事件處理

        /// <summary>選擇事件選項（choiceIdx）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleEventChoiceAsync(
            ulong channelId, int choiceIdx)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");
            if (run.PendingEventIdx < 0 || run.PendingEventIdx >= _events.Count)
                return ErrEmbed("無效的事件");
            var ev = _events[run.PendingEventIdx];
            if (choiceIdx < 0 || choiceIdx >= ev.Choices.Count)
                return ErrEmbed("無效的選項");

            var choice = ev.Choices[choiceIdx];
            string result = await choice.Apply(run);
            run.RunLog.Add($"{ev.Emoji} 第{run.CurrentFloor}層【{ev.Title}】→ {choice.Label}");
            run.UsedEventIndices.Add(run.PendingEventIdx);
            run.PendingEventIdx = -1;
            string eventHeader = $"{ev.Emoji} **【{ev.Title}】**\n> {choice.Emoji} {choice.Label}\n\n{result}";
            // If the event set up a catch (e.g. legendary encounter), route to catch screen
            if (run.PendingCatch != null)
            {
                run.State = TowerRunState.SelectingCatch;
                await SaveAsync(run);
                return BuildCatchEmbed(run, eventHeader);
            }
            // If the event set up a move learning choice
            if (run.EventMoveRewardPending)
            {
                run.State = TowerRunState.SelectingMoveReward;
                await SaveAsync(run);
                return BuildMoveRewardEmbed(run, eventHeader);
            }
            // If the event set up a quiz
            if (run.State == TowerRunState.InMiniGameQuiz)
            {
                await SaveAsync(run);
                return BuildQuizEmbed(run, eventHeader);
            }
            // If the event set up a 2048 game
            if (run.State == TowerRunState.InMiniGame2048)
            {
                await SaveAsync(run);
                return Build2048Embed(run, eventHeader);
            }
            // If the event set up a minesweeper game
            if (run.State == TowerRunState.InMiniGameMine)
            {
                await SaveAsync(run);
                return BuildMinesweeperEmbed(run, eventHeader);
            }
            run.State = TowerRunState.SelectingPath;
            await SaveAsync(run);
            return BuildPathEmbed(run, eventHeader);
        }

        #endregion

        #region 休息 & 商店

        /// <summary>商店購買</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleShopItemAsync(
            ulong channelId, string itemKey)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            // curse_paranoia: cannot use shop
            if (run.CursedRelicIds.Contains("curse_paranoia"))
                return ErrEmbed("😱 **妄想症詛咒**：商店老闆看起來很危險，你不敢進去！");

            string msg;
            switch (itemKey)
            {
                case "heal_full":
                    { int cost = ShopCost(run, 30, "heal_full"); if (run.Gold < cost) return BuildShopEmbed(run, $"💸 金幣不足！需要 {cost} 金幣。"); run.Gold -= cost; foreach (var pk in run.Party) pk.CurrentHP = pk.MaxHP; run.ActivePokemon.CurrentHP = run.ActivePokemon.MaxHP; msg = $"💊 使用「全回復」— 全隊 HP 完全恢復！（-{cost}💰）"; break; }
                case "heal_half":
                    { int cost = ShopCost(run, 15, "heal_half"); if (run.Gold < cost) return BuildShopEmbed(run, $"💸 金幣不足！需要 {cost} 金幣。"); run.Gold -= cost; foreach (var pk in run.Party) { int h = Math.Max(1, pk.MaxHP / 2); pk.CurrentHP = Math.Min(pk.MaxHP, pk.CurrentHP + h); } run.ActivePokemon.CurrentHP = Math.Min(run.ActivePokemon.MaxHP, run.ActivePokemon.CurrentHP + Math.Max(1, run.ActivePokemon.MaxHP / 2)); msg = $"🧃 使用「超級樹果」— 全隊恢復 50% HP！（-{cost}💰）"; break; }
                case "pp_restore":
                    { int cost = ShopCost(run, 20, "pp_restore"); if (run.Gold < cost) return BuildShopEmbed(run, $"💸 金幣不足！需要 {cost} 金幣。"); run.Gold -= cost; foreach (var pk in run.Party) foreach (var m in pk.Moves) m.CurrentPP = m.MaxPP; foreach (var m in run.ActivePokemon.Moves) m.CurrentPP = m.MaxPP; msg = $"🔋 全隊技能 PP 完全恢復！（-{cost}💰）"; break; }
                case "new_move":
                    {
                        int cost = ShopCost(run, 25, "new_move");
                        if (run.Gold < cost) return BuildShopEmbed(run, $"💸 金幣不足！需要 {cost} 金幣。");
                        run.Gold -= cost;
                        run.ShopBuyCounts ??= new();
                        run.ShopBuyCounts["new_move"] = run.ShopBuyCounts.GetValueOrDefault("new_move", 0) + 1;
                        var movePool = PickMoves(run.ActivePokemon.Types);
                        run.PendingMoveRewards = movePool.OrderBy(_ => _rng.Next()).Take(3).ToList();
                        run.ShopMoveRewardPending = true;
                        run.State = TowerRunState.SelectingMoveReward;
                        await SaveAsync(run);
                        return BuildMoveRewardEmbed(run, $"📀 **技能學習器**（-{cost}💰）\n請選擇一個技能讓你的寶可夢學習！");
                    }
                case "buy_normal":
                    if (HasPassive(run, "passive_catchmaster")) return BuildShopEmbed(run, "🎯 **捕獲大師**：不需要購買球！");
                    { int cost = ShopCost(run, 8, "buy_normal"); if (run.Gold < cost) return BuildShopEmbed(run, $"💸 金幣不足！需要 {cost} 金幣。"); run.Gold -= cost; run.Balls["normal"] = run.Balls.GetValueOrDefault("normal") + 3; msg = $"⚽ 購入 **普通球×3**！（-{cost}💰）"; break; }
                case "buy_super":
                    if (HasPassive(run, "passive_catchmaster")) return BuildShopEmbed(run, "🎯 **捕獲大師**：不需要購買球！");
                    { int cost = ShopCost(run, 15, "buy_super"); if (run.Gold < cost) return BuildShopEmbed(run, $"💸 金幣不足！需要 {cost} 金幣。"); run.Gold -= cost; run.Balls["super"] = run.Balls.GetValueOrDefault("super") + 2; msg = $"🔵 購入 **超級球×2**！（-{cost}💰）"; break; }
                case "buy_ultra":
                    if (HasPassive(run, "passive_catchmaster")) return BuildShopEmbed(run, "🎯 **捕獲大師**：不需要購買球！");
                    { int cost = ShopCost(run, 25, "buy_ultra"); if (run.Gold < cost) return BuildShopEmbed(run, $"💸 金幣不足！需要 {cost} 金幣。"); run.Gold -= cost; run.Balls["ultra"] = run.Balls.GetValueOrDefault("ultra") + 1; msg = $"🟡 購入 **高級球×1**！（-{cost}💰）"; break; }
                case "leave":
                    msg = "👋 離開商店，繼續爬塔！";
                    break;
                default:
                    return ErrEmbed("未知的道具");
            }

            // 每購買一次，下次購買同商品+10💰（new_move 已在 case 內自行處理）
            run.ShopBuyCounts ??= new();
            if (itemKey != "leave" && itemKey != "new_move")
                run.ShopBuyCounts[itemKey] = run.ShopBuyCounts.GetValueOrDefault(itemKey, 0) + 1;
            if (itemKey == "leave") run.State = TowerRunState.SelectingPath;
            run.RunLog.Add(msg);
            await SaveAsync(run);
            if (itemKey == "leave") return BuildPathEmbed(run, msg);
            return BuildShopEmbed(run, msg);
        }

        /// <summary>顯示換寶可夢畫面</summary>
        public (Embed embed, ComponentBuilder component) ShowSwapSelection(
            ulong channelId)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            var embed = new EmbedBuilder()
                .WithTitle("🔄 換寶可夢")
                .WithDescription(
                    $"目前：**{run.ActivePokemon.DisplayName}** HP {run.ActivePokemon.CurrentHP}/{run.ActivePokemon.MaxHP}\n\n" +
                    "**背包成員（HP 保留）：**")
                .WithColor(Color.Blue);

            var cb = new ComponentBuilder();
            int btnRow = 0;

            for (int i = 0; i < run.Party.Count; i++)
            {
                var p = run.Party[i];
                bool isActive = p.PokeId == run.ActivePokemon.PokeId && p.CaughtAt == run.ActivePokemon.CaughtAt;
                embed.AddField(
                    $"{(isActive ? "▶ " : "")}{p.DisplayName} {TypeBadge(p.Types)}",
                    $"HP {HpBar(p.CurrentHP, p.MaxHP, 6)} | {string.Join(" ", p.Moves.Select(m => $"{m.Emoji}{m.Name}"))}",
                    inline: false);
                if (!isActive && p.CurrentHP > 0)
                    cb.WithButton(p.DisplayName, $"tower_swap_{channelId}_{i}", ButtonStyle.Primary, row: btnRow++);
            }

            cb.WithButton("取消", $"tower_swap_cancel_{channelId}", ButtonStyle.Secondary, row: 4);
            return (embed.Build(), cb);
        }

        /// <summary>換上已在背包中的寶可夢</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleSwapConfirmAsync(
            ulong channelId, int partyIndex)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");
            if (partyIndex < 0 || partyIndex >= run.Party.Count)
                return ErrEmbed("找不到選擇的寶可夢");

            var target = run.Party[partyIndex];
            if (target.CurrentHP <= 0)
                return ErrEmbed("這隻寶可夢已經無法戰鬥！");

            string prev = run.ActivePokemon.DisplayName;
            run.ActivePokemon = target;
            run.RunLog.Add($"🔄 換上了 {target.DisplayName}（換下 {prev}）");
            await SaveAsync(run);
            return BuildCurrentStateEmbed(channelId);
        }

        /// <summary>加入新成員到背包</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleAddNewPokemonAsync(
            ulong channelId, PokeGamePokemon src)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");
            if (run.Party.Count >= 3)
                return ErrEmbed("背包已滿！最多只能帶 3 隻。");

            var newPoke = ConvertPokemon(src);
            newPoke.Moves = PickMovesStatic(src.Types?.ToList() ?? new());
            run.Party.Add(newPoke);
            run.RunLog.Add($"➕ {newPoke.DisplayName} 加入了爬塔！");
            await SaveAsync(run);
            return BuildCurrentStateEmbed(channelId);
        }

        #endregion

        #region 威力升級系統

        /// <summary>進入威力升級介面（來自商店或休息）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> EnterPowerUpgradeAsync(ulong channelId, string returnTo)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");
            run.PowerUpgradeReturn = returnTo;
            run.State = TowerRunState.SelectingPowerUpgrade;
            await SaveAsync(run);
            return BuildPowerUpgradeEmbed(run);
        }

        /// <summary>威力升級：選擇技能 (0-3=槽位, 4=跳過)</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandlePowerUpgradeAsync(ulong channelId, int moveIndex)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            var ret = run.PowerUpgradeReturn;
            run.PowerUpgradeReturn = "";

            if (moveIndex >= 0 && moveIndex < run.ActivePokemon.Moves.Count)
            {
                var m = run.ActivePokemon.Moves[moveIndex];
                int upgradeLimit = HasPassive(run, "passive_techgeek") ? 8 : 5;
                if (m.UpgradeCount >= upgradeLimit)
                {
                    run.State = TowerRunState.SelectingPowerUpgrade;
                    run.PowerUpgradeReturn = ret;
                    return BuildPowerUpgradeEmbed(run, $"【{m.Name}】已達強化上限（{upgradeLimit}/{upgradeLimit}）！");
                }
                if (ret == "shop")
                {
                    int cost = ShopCost(run, 20, "powerup");
                    if (run.Gold < cost)
                    {
                        run.State = TowerRunState.SelectingPowerUpgrade;
                        run.PowerUpgradeReturn = ret;
                        return BuildPowerUpgradeEmbed(run, $"金幣不足！需要 {cost}💰");
                    }
                    run.Gold -= cost;
                    run.ShopBuyCounts ??= new();
                    run.ShopBuyCounts["powerup"] = run.ShopBuyCounts.GetValueOrDefault("powerup", 0) + 1;
                }
                m.Power += 20;
                m.UpgradeCount++;
                run.RunLog.Add($"⚡ 強化【{m.Name}】威力提升至 {m.Power}！（{m.UpgradeCount}/5）");
            }

            if (ret == "battle")
            {
                // 選了威力升級就不再進技能獎勵
                run.PendingMoveRewards.Clear();
                await SaveAsync(run);
                return CheckCatch(run);
            }
            if (ret == "shop")
            {
                run.State = TowerRunState.Shopping;
                await SaveAsync(run);
                return BuildShopEmbed(run);
            }
            // rest or default → path select
            run.State = TowerRunState.SelectingPath;
            await SaveAsync(run);
            return BuildPathEmbed(run);
        }

        /// <summary>休息後繼續前進</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleRestContinueAsync(ulong channelId)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");
            run.State = TowerRunState.SelectingPath;
            await SaveAsync(run);
            return BuildPathEmbed(run);
        }

        /// <summary>休息後換季能</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleRestMoveSwapAsync(ulong channelId)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");
            run.PendingMoveRewards = _movePool.OrderBy(_ => _rng.Next()).Take(3).ToList();
            run.RestMoveRewardPending = true;
            run.State = TowerRunState.SelectingMoveReward;
            await SaveAsync(run);
            return BuildMoveRewardEmbed(run);
        }

        /// <summary>取消換寶可夢，回到當前狀態</summary>
        public (Embed embed, ComponentBuilder component) BuildCurrentStateEmbed(ulong channelId)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");
            return run.State switch
            {
                TowerRunState.InBattle => BuildBattleEmbed(run),
                TowerRunState.Shopping => BuildShopEmbed(run),
                TowerRunState.SelectingEvent => BuildEventEmbed(run),
                TowerRunState.SelectingMoveReward => BuildMoveRewardEmbed(run),
                TowerRunState.SelectingMoveSlot => BuildMoveSlotEmbed(run),
                TowerRunState.SelectingCatch => BuildCatchEmbed(run),
                TowerRunState.SelectingCatchSwap => BuildCatchSwapEmbed(run, CatchFromEnemy(run.PendingCatch)),
                TowerRunState.Resting => BuildRestEmbed(run, 0),
                TowerRunState.SelectingPowerUpgrade => BuildPowerUpgradeEmbed(run),
                TowerRunState.SelectingRelic => BuildRelicEmbed(run),
                TowerRunState.InCasino => BuildCasinoEmbed(run),
                TowerRunState.SelectingPassive => BuildPassiveSelectionEmbed(run.ChannelId),
                TowerRunState.SelectingCursedRelic => BuildCursedRelicEmbed(run),
                TowerRunState.InMiniGame2048 => Build2048Embed(run),
                TowerRunState.InMiniGameMine => BuildMinesweeperEmbed(run),
                TowerRunState.InMiniGameQuiz => BuildQuizEmbed(run),
                _ => BuildPathEmbed(run)
            };
        }

        /// <summary>取消爬塔</summary>
        public async Task<bool> CancelRunAsync(ulong channelId)
        {
            if (!_activeRuns.ContainsKey(channelId)) return false;
            await RemoveAsync(channelId);
            return true;
        }

        #endregion

        #region 神器系統（Relic）
        // ── Relic system ──────────────────────────────────────

        private static bool HasRelic(TowerRun run, string id) => run.RelicIds.Contains(id);
        private static bool HasPassive(TowerRun run, string id) => run.PassiveId == id;
        private static int ShopCost(TowerRun run, int baseCost, string itemKey = "")
        {
            run.ShopBuyCounts ??= new();   // 舊存檔反序列化後可能為 null
            int cost = baseCost;
            if (!string.IsNullOrEmpty(itemKey))
                cost += run.ShopBuyCounts.GetValueOrDefault(itemKey, 0) * 10;
            if (HasPassive(run, "passive_richboy")) cost = (int)(cost * 0.7f);
            if (run.CursedRelicIds.Contains("curse_expensive")) cost = (int)(cost * 1.5f);
            return Math.Max(1, cost);
        }

        private static void ApplyRelicOnPickup(TowerRun run, string relicId)
        {
            switch (relicId)
            {
                case "relic_atk_up":
                    foreach (var p in run.Party) { p.Attack = (int)(p.Attack * 1.20f); p.SpecialAttack = (int)(p.SpecialAttack * 1.20f); }
                    break;
                case "relic_def_up":
                    foreach (var p in run.Party) { p.Defense = (int)(p.Defense * 1.20f); p.SpecialDefense = (int)(p.SpecialDefense * 1.20f); }
                    break;
                case "relic_hp_up":
                    foreach (var p in run.Party) { int boost = Math.Max(1, (int)(p.MaxHP * 0.25f)); p.MaxHP += boost; p.CurrentHP = Math.Min(p.MaxHP, p.CurrentHP + boost); }
                    break;
                case "relic_move_pow":
                    foreach (var m in run.ActivePokemon.Moves) m.Power += 20;
                    break;
                case "relic_move_pp":
                    foreach (var m in run.ActivePokemon.Moves) { m.MaxPP += 5; m.CurrentPP = m.MaxPP; }
                    break;
                case "relic_all_stats":
                    foreach (var p in run.Party)
                    {
                        p.Attack = (int)(p.Attack * 1.15f); p.Defense = (int)(p.Defense * 1.15f);
                        p.SpecialAttack = (int)(p.SpecialAttack * 1.15f); p.SpecialDefense = (int)(p.SpecialDefense * 1.15f);
                        p.Speed = (int)(p.Speed * 1.15f);
                        int boost = Math.Max(1, (int)(p.MaxHP * 0.15f)); p.MaxHP += boost; p.CurrentHP = Math.Min(p.MaxHP, p.CurrentHP + boost);
                    }
                    break;
                case "relic_gold":
                    run.Gold += 80;
                    break;
                case "relic_exp":
                    {
                        run.Exp += run.Level * 120;
                        while (run.Exp >= run.ExpToNext)
                        {
                            run.Exp -= run.ExpToNext;
                            run.Level++;
                            foreach (var member in run.Party)
                            {
                                member.Attack = (int)(member.Attack * 1.10f); member.Defense = (int)(member.Defense * 1.10f);
                                member.SpecialAttack = (int)(member.SpecialAttack * 1.10f); member.SpecialDefense = (int)(member.SpecialDefense * 1.10f);
                                member.Speed = (int)(member.Speed * 1.10f);
                                int hpBoost = Math.Max(1, (int)(member.MaxHP * 0.10f)); member.MaxHP += hpBoost; member.CurrentHP += hpBoost;
                            }
                        }
                    }
                    break;
                case "relic_swift":
                    foreach (var p in run.Party) p.Speed = (int)(p.Speed * 1.3f);
                    break;
                // relic_time_warp, relic_executioner, relic_mirror_coat, relic_parasite,
                // relic_feast, relic_double_edge, relic_lucky_charm, relic_exp_boost,
                // relic_gold_mine, relic_berserker_r, relic_scholar, relic_comeback,
                // relic_shared_pain: passive effects applied in HandleMoveAsync
                default:
                    break;
            }
        }

        public async Task<(Embed embed, ComponentBuilder component)> HandleRelicChoiceAsync(ulong channelId, int idx)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            if (idx >= 0 && idx < run.PendingRelicChoices.Count)
            {
                string chosenId = run.PendingRelicChoices[idx];
                run.RelicIds.Add(chosenId);
                ApplyRelicOnPickup(run, chosenId);
                var relic = _relics.FirstOrDefault(r => r.Id == chosenId);
                run.RunLog.Add($"🏺 獲得神器【{relic?.Name}】！");
            }
            run.PendingRelicChoices.Clear();

            // Proceed to power upgrade (battle chain)
            run.PowerUpgradeReturn = "battle";
            run.State = TowerRunState.SelectingPowerUpgrade;
            await SaveAsync(run);
            return BuildPowerUpgradeEmbed(run);
        }

        #endregion

        #region 賭場（Casino）
        // ── Casino ────────────────────────────────────────────

        private (Embed embed, ComponentBuilder component) BuildCasinoEmbed(TowerRun run, string lastResult = "")
        {
            var desc = new StringBuilder();
            if (!string.IsNullOrEmpty(lastResult)) desc.AppendLine(lastResult).AppendLine("---").AppendLine();
            desc.AppendLine($"💰 現有金幣：**{run.Gold}**　本場總損益：**{(run.CasinoProfit >= 0 ? "+" : "")}{run.CasinoProfit}💰**");
            desc.AppendLine();

            ComponentBuilder cb;
            if (run.CasinoBet == 0)
            {
                // Phase 1: 選下注金額（動態去重，避免 CustomId 重複）
                desc.AppendLine("🎰 **選擇你的籌碼：**（贏 = 籌碼×2 進袋，輸 = 籌碼全沒）");
                // 建立不重複的下注選項：固定 10、金幣一半、全押
                var betOptions = new List<(int Amount, string Label, ButtonStyle Style)>();
                var seenAmounts = new HashSet<int>();
                void AddBet(int amt, string label, ButtonStyle style)
                {
                    if (amt > 0 && amt <= run.Gold && seenAmounts.Add(amt))
                        betOptions.Add((amt, label, style));
                }
                AddBet(10,            "🟡 小注 10💰",                 ButtonStyle.Primary);
                AddBet(run.Gold / 2,  $"🟠 半押 {run.Gold / 2}💰",   ButtonStyle.Primary);
                AddBet(run.Gold,      $"🔴 全押 {run.Gold}💰",        ButtonStyle.Danger);

                cb = new ComponentBuilder();
                bool canBet = run.Gold > 0;
                if (betOptions.Count == 0)
                    desc.AppendLine("💸 金幣不足，無法下注！");
                else
                    foreach (var (amt, lbl, sty) in betOptions)
                        cb.WithButton($"{lbl}（贏+{amt}）", $"tower_casino_{run.ChannelId}_bet_{amt}", canBet ? sty : ButtonStyle.Secondary, row: 0, disabled: !canBet);
                cb.WithButton("🚪 離開賭場", $"tower_casino_{run.ChannelId}_leave", ButtonStyle.Secondary, row: 1);
            }
            else
            {
                // Phase 2: 猜大小
                desc.AppendLine($"🎯 籌碼：**{run.CasinoBet}💰** — 猜大（4-6）還是猜小（1-3）？");
                desc.AppendLine("猜中 → 賺回籌碼×2 💰　猜錯 → 籌碼歸零 💀");
                cb = new ComponentBuilder()
                    .WithButton("🔼 猜大 (4~6)", $"tower_casino_{run.ChannelId}_high", ButtonStyle.Primary, row: 0)
                    .WithButton("🔽 猜小 (1~3)", $"tower_casino_{run.ChannelId}_low", ButtonStyle.Success, row: 0)
                    .WithButton("😅 反悔不賭了", $"tower_casino_{run.ChannelId}_cancel", ButtonStyle.Secondary, row: 1);
            }

            var embed = new EmbedBuilder()
                .WithTitle("🎰 老虎機賭場")
                .WithDescription(desc.ToString())
                .WithColor(new Color(220, 50, 47))
                .WithFooter($"{run.PlayerName} • 第 {run.CurrentFloor}/{run.MaxFloor} 層 • 本場 {run.CasinoRound} 局")
                .Build();
            return (embed, cb);
        }

        public async Task<(Embed embed, ComponentBuilder component)> HandleCasinoAsync(ulong channelId, string action)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            if (action == "leave")
            {
                run.State = TowerRunState.SelectingPath;
                run.CasinoBet = 0;
                string profitMsg = run.CasinoProfit == 0 ? "🎲 不輸不贏，拍拍屁股離開了。"
                    : run.CasinoProfit > 0 ? $"🤑 賭場贏家！本次淨賺 **+{run.CasinoProfit}💰**！"
                    : $"😭 輸光了！本次虧了 **{Math.Abs(run.CasinoProfit)}💰**！";
                await SaveAsync(run);
                return BuildPathEmbed(run, profitMsg);
            }

            if (action == "cancel")
            {
                // 反悔：退回籌碼，回到下注選擇
                run.Gold += run.CasinoBet;
                run.CasinoProfit += run.CasinoBet; // 補回已扣的
                run.CasinoBet = 0;
                await SaveAsync(run);
                return BuildCasinoEmbed(run, "😅 算了算了，籌碼還你，重新選吧。");
            }

            // bet_{amount}: 下注
            if (action.StartsWith("bet_"))
            {
                if (int.TryParse(action["bet_".Length..], out int betAmt) && betAmt > 0)
                {
                    betAmt = Math.Min(betAmt, run.Gold); // 不能超過現有金幣
                    run.Gold -= betAmt;
                    run.CasinoBet = betAmt;
                    await SaveAsync(run);
                    return BuildCasinoEmbed(run);
                }
                return ErrEmbed("無效的下注金額");
            }

            if (action == "high" || action == "low")
            {
                if (run.CasinoBet <= 0)
                    return BuildCasinoEmbed(run, "⚠️ 先選籌碼再猜大小！");

                int bet = run.CasinoBet;
                run.CasinoBet = 0;
                int dice = _rng.Next(1, 7);
                string diceFace = dice switch { 1 => "⚀", 2 => "⚁", 3 => "⚂", 4 => "⚃", 5 => "⚄", 6 => "⚅", _ => $"{dice}" };
                bool won = (action == "high" && dice >= 4) || (action == "low" && dice <= 3);
                string result;
                if (won)
                {
                    run.Gold += bet * 2;  // 還回本金 + 獎金
                    run.CasinoProfit += bet;
                    result = $"🎲 {diceFace} 骰出 **{dice}** — 猜中！**+{bet}💰** 入袋！（現在 {run.Gold}💰）";
                }
                else
                {
                    // 本金已在下注時扣掉，輸了就什麼都沒了
                    run.CasinoProfit -= bet;
                    result = $"🎲 {diceFace} 骰出 **{dice}** — 猜錯！**{bet}💰** 被吃掉 💀（現在 {run.Gold}💰）";
                }
                run.CasinoRound++;
                await SaveAsync(run);
                return BuildCasinoEmbed(run, result);
            }

            return ErrEmbed("未知的賭場操作");
        }

        #endregion

        #region 小遊戲：2048
        // ── Mini-game: 2048 ────────────────────────────────────

        /// <summary>初始化2048爬塔挑戰（由事件觸發）</summary>
        private static void Setup2048(TowerRun run, int reward)
        {
            run.MiniGame2048Board = Enumerable.Repeat(0, 16).ToList();
            run.MiniGame2048Reward = reward;
            run.MiniGame2048MovesLeft = 15;
            Place2048Tile(run);
            Place2048Tile(run);
        }

        private static void Place2048Tile(TowerRun run)
        {
            var empty = Enumerable.Range(0, 16).Where(i => run.MiniGame2048Board[i] == 0).ToList();
            if (empty.Count == 0) return;
            int idx = empty[_rng.Next(empty.Count)];
            run.MiniGame2048Board[idx] = _rng.Next(10) < 9 ? 2 : 4;
        }

        private static int[] Slide2048Row(int[] row)
        {
            var nonZero = row.Where(x => x != 0).ToArray();
            var merged = new List<int>();
            for (int i = 0; i < nonZero.Length; i++)
            {
                if (i + 1 < nonZero.Length && nonZero[i] == nonZero[i + 1]) { merged.Add(nonZero[i] * 2); i++; }
                else merged.Add(nonZero[i]);
            }
            while (merged.Count < 4) merged.Add(0);
            return merged.ToArray();
        }

        private bool Apply2048Move(TowerRun run, string dir)
        {
            var old = run.MiniGame2048Board.ToList();
            var b = run.MiniGame2048Board.ToArray();
            // Extract rows
            int[][] rows = new int[4][];
            for (int r = 0; r < 4; r++) rows[r] = new[] { b[r*4], b[r*4+1], b[r*4+2], b[r*4+3] };

            switch (dir)
            {
                case "left":
                    for (int r = 0; r < 4; r++) rows[r] = Slide2048Row(rows[r]);
                    break;
                case "right":
                    for (int r = 0; r < 4; r++) { Array.Reverse(rows[r]); rows[r] = Slide2048Row(rows[r]); Array.Reverse(rows[r]); }
                    break;
                case "up":
                    // Transpose → slide left → transpose
                    int[][] cols = new int[4][];
                    for (int c = 0; c < 4; c++) cols[c] = new[] { rows[0][c], rows[1][c], rows[2][c], rows[3][c] };
                    for (int c = 0; c < 4; c++) cols[c] = Slide2048Row(cols[c]);
                    for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) rows[r][c] = cols[c][r];
                    break;
                case "down":
                    cols = new int[4][];
                    for (int c = 0; c < 4; c++) cols[c] = new[] { rows[3][c], rows[2][c], rows[1][c], rows[0][c] };
                    for (int c = 0; c < 4; c++) cols[c] = Slide2048Row(cols[c]);
                    for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) rows[3-r][c] = cols[c][r];
                    break;
            }
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) run.MiniGame2048Board[r*4+c] = rows[r][c];
            bool changed = !run.MiniGame2048Board.SequenceEqual(old);
            if (changed) Place2048Tile(run);
            return changed;
        }

        private (Embed embed, ComponentBuilder component) Build2048Embed(TowerRun run, string notice = "")
        {
            var desc = new StringBuilder();
            if (!string.IsNullOrEmpty(notice)) desc.AppendLine(notice).AppendLine();
            int maxTile = run.MiniGame2048Board.Max();
            desc.AppendLine($"🎯 目標：達到 **32** 以上！　剩餘步數：**{run.MiniGame2048MovesLeft}**　最高方塊：**{maxTile}**");
            desc.AppendLine($"獎勵：**{run.MiniGame2048Reward}💰**");
            desc.AppendLine();
            desc.AppendLine("```");
            for (int r = 0; r < 4; r++)
            {
                var rowStr = "";
                for (int c = 0; c < 4; c++)
                {
                    int v = run.MiniGame2048Board[r*4+c];
                    rowStr += v == 0 ? "  .  " : v.ToString().PadLeft(4) + " ";
                }
                desc.AppendLine(rowStr.TrimEnd());
            }
            desc.AppendLine("```");

            var cb = new ComponentBuilder()
                .WithButton("⬆️", $"tower_2048_{run.ChannelId}_up",    ButtonStyle.Primary, row: 0)
                .WithButton("⬅️", $"tower_2048_{run.ChannelId}_left",  ButtonStyle.Primary, row: 1)
                .WithButton("⬇️", $"tower_2048_{run.ChannelId}_down",  ButtonStyle.Primary, row: 1)
                .WithButton("➡️", $"tower_2048_{run.ChannelId}_right", ButtonStyle.Primary, row: 1)
                .WithButton("🏳️ 放棄", $"tower_2048_{run.ChannelId}_give_up", ButtonStyle.Danger, row: 2);

            return (new EmbedBuilder()
                .WithTitle("🎮 2048 爬塔挑戰")
                .WithDescription(desc.ToString())
                .WithColor(new Color(237, 194, 46))
                .WithFooter($"{run.PlayerName} • 第 {run.CurrentFloor}/{run.MaxFloor} 層")
                .Build(), cb);
        }

        public async Task<(Embed embed, ComponentBuilder component)> Handle2048Async(ulong channelId, string dir)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            if (dir == "give_up")
            {
                run.State = TowerRunState.SelectingPath;
                await SaveAsync(run);
                return BuildPathEmbed(run, "🏳️ 放棄了 2048 挑戰，什麼獎勵都沒有。");
            }

            Apply2048Move(run, dir);
            run.MiniGame2048MovesLeft--;

            int maxTile = run.MiniGame2048Board.Max();
            if (maxTile >= 32)
            {
                run.Gold += run.MiniGame2048Reward;
                run.RunLog.Add($"🎮 2048 成功！+{run.MiniGame2048Reward}💰");
                run.State = TowerRunState.SelectingPath;
                await SaveAsync(run);
                return BuildPathEmbed(run, $"🎉 太厲害了！達到了 **{maxTile}**！獲得 **+{run.MiniGame2048Reward}💰**！");
            }

            if (run.MiniGame2048MovesLeft <= 0)
            {
                run.State = TowerRunState.SelectingPath;
                await SaveAsync(run);
                return BuildPathEmbed(run, $"⏰ 用完了所有步數！最高方塊是 **{maxTile}**，差一點點就成功了……");
            }

            await SaveAsync(run);
            return Build2048Embed(run);
        }

        #endregion

        #region 小遊戲：踩地雷
        // ── Mini-game: Minesweeper ──────────────────────────────

        /// <summary>初始化地雷挑戰（由事件觸發）</summary>
        private static void SetupMinesweeper(TowerRun run, int reward, int mineCount = 3)
        {
            // 3x3 grid
            var board = Enumerable.Repeat(0, 9).ToList();
            var minePositions = Enumerable.Range(0, 9).OrderBy(_ => _rng.Next()).Take(mineCount).ToList();
            foreach (int pos in minePositions) board[pos] = -1;
            // Calculate numbers
            for (int i = 0; i < 9; i++)
            {
                if (board[i] == -1) continue;
                int r = i / 3, c = i % 3, count = 0;
                for (int dr = -1; dr <= 1; dr++) for (int dc = -1; dc <= 1; dc++)
                {
                    int nr = r+dr, nc = c+dc;
                    if (nr>=0 && nr<3 && nc>=0 && nc<3 && board[nr*3+nc] == -1) count++;
                }
                board[i] = count;
            }
            run.MiniGameMineBoard = board;
            run.MiniGameMineRevealed = Enumerable.Repeat(false, 9).ToList();
            run.MiniGameMineSafeLeft = 5;
            run.MiniGameMineReward = reward;
        }

        private (Embed embed, ComponentBuilder component) BuildMinesweeperEmbed(TowerRun run, string notice = "")
        {
            var desc = new StringBuilder();
            if (!string.IsNullOrEmpty(notice)) desc.AppendLine(notice).AppendLine();
            desc.AppendLine($"🎯 安全踩 **{run.MiniGameMineSafeLeft}** 步就能獲得 **{run.MiniGameMineReward}💰**！");
            desc.AppendLine("3×3 地圖，裡面有 **3 個地雷**。踩到地雷 → 立即失敗！");
            desc.AppendLine();

            // Display revealed cells
            string[] nums = { "⬜", "1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣", "8️⃣" };
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    int i = r*3+c;
                    if (run.MiniGameMineRevealed[i])
                    {
                        int v = run.MiniGameMineBoard[i];
                        desc.Append(v == -1 ? "💥" : (v == 0 ? "🟩" : nums[v]));
                    }
                    else desc.Append("⬛");
                    desc.Append(" ");
                }
                desc.AppendLine();
            }

            var cb = new ComponentBuilder();
            for (int r = 0; r < 3; r++)
            {
                var row = new ActionRowBuilder();
                for (int c = 0; c < 3; c++)
                {
                    int i = r*3+c;
                    bool revealed = run.MiniGameMineRevealed[i];
                    row.WithButton(new ButtonBuilder()
                        .WithLabel(revealed ? "✓" : $"{(char)('A'+i)}")
                        .WithCustomId($"tower_mine_{run.ChannelId}_{i}")
                        .WithStyle(revealed ? ButtonStyle.Secondary : ButtonStyle.Primary)
                        .WithDisabled(revealed));
                }
                cb.AddRow(row);
            }
            cb.WithButton("🏳️ 放棄", $"tower_mine_{run.ChannelId}_give_up", ButtonStyle.Danger, row: 3);

            return (new EmbedBuilder()
                .WithTitle("💣 踩地雷挑戰！")
                .WithDescription(desc.ToString())
                .WithColor(new Color(100, 200, 100))
                .WithFooter($"{run.PlayerName} • 第 {run.CurrentFloor}/{run.MaxFloor} 層")
                .Build(), cb);
        }

        public async Task<(Embed embed, ComponentBuilder component)> HandleMinesweeperAsync(ulong channelId, string action)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            if (action == "give_up")
            {
                run.State = TowerRunState.SelectingPath;
                await SaveAsync(run);
                return BuildPathEmbed(run, "🏳️ 你膽怯了，一步都沒踩就跑了。");
            }

            if (!int.TryParse(action, out int cellIdx) || cellIdx < 0 || cellIdx >= 9)
                return ErrEmbed("無效操作");
            if (run.MiniGameMineRevealed[cellIdx])
                return BuildMinesweeperEmbed(run, "⚠️ 那格已經翻開了！");

            run.MiniGameMineRevealed[cellIdx] = true;
            int cellVal = run.MiniGameMineBoard[cellIdx];

            if (cellVal == -1)
            {
                // 踩到地雷！揭示全部
                for (int i = 0; i < 9; i++) run.MiniGameMineRevealed[i] = true;
                run.State = TowerRunState.SelectingPath;
                await SaveAsync(run);
                return BuildPathEmbed(run, "💥 **踩到地雷了！** 爆炸聲在塔裡迴響，什麼獎勵都沒有……");
            }

            run.MiniGameMineSafeLeft--;
            if (run.MiniGameMineSafeLeft <= 0)
            {
                run.Gold += run.MiniGameMineReward;
                run.RunLog.Add($"💣 踩地雷挑戰成功！+{run.MiniGameMineReward}💰");
                for (int i = 0; i < 9; i++) run.MiniGameMineRevealed[i] = true;
                run.State = TowerRunState.SelectingPath;
                await SaveAsync(run);
                return BuildPathEmbed(run, $"🎉 安全通過！**+{run.MiniGameMineReward}💰** 獎勵到手！");
            }

            await SaveAsync(run);
            return BuildMinesweeperEmbed(run, $"✅ 安全！還需再踩 **{run.MiniGameMineSafeLeft}** 步！");
        }

        #endregion

        #region 小遊戲：問答（英雄 / 單字）
        // ── Mini-game: Quiz ────────────────────────────────────

        private (Embed embed, ComponentBuilder component) BuildQuizEmbed(TowerRun run, string notice = "")
        {
            var desc = new StringBuilder();
            if (!string.IsNullOrEmpty(notice)) desc.AppendLine(notice).AppendLine();
            desc.AppendLine(run.MiniGameQuizQuestion);
            desc.AppendLine();
            string[] labels = { "A", "B", "C", "D" };
            for (int i = 0; i < run.MiniGameQuizChoices.Count; i++)
                desc.AppendLine($"**{labels[i]}. {run.MiniGameQuizChoices[i]}**");

            var cb = new ComponentBuilder();
            for (int i = 0; i < run.MiniGameQuizChoices.Count; i++)
                cb.WithButton($"{labels[i]}. {run.MiniGameQuizChoices[i]}", $"tower_quiz_{run.ChannelId}_{i}", ButtonStyle.Primary, row: 0);
            cb.WithButton("🚶 跳過", $"tower_quiz_{run.ChannelId}_skip", ButtonStyle.Secondary, row: 1);

            return (new EmbedBuilder()
                .WithTitle("🧠 知識問答")
                .WithDescription(desc.ToString())
                .WithColor(new Color(100, 100, 220))
                .WithFooter($"{run.PlayerName} • 第 {run.CurrentFloor}/{run.MaxFloor} 層")
                .Build(), cb);
        }

        public async Task<(Embed embed, ComponentBuilder component)> HandleQuizAsync(ulong channelId, string action)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            if (action == "skip")
            {
                run.State = TowerRunState.SelectingPath;
                await SaveAsync(run);
                return BuildPathEmbed(run, "🚶 跳過了問題，繼續爬塔。");
            }

            if (!int.TryParse(action, out int choiceIdx) || choiceIdx < 0 || choiceIdx >= run.MiniGameQuizChoices.Count)
                return ErrEmbed("無效選項");

            bool correct = choiceIdx == run.MiniGameQuizAnswerIdx;
            string answerLabel = $"**{run.MiniGameQuizChoices[run.MiniGameQuizAnswerIdx]}**";
            run.State = TowerRunState.SelectingPath;
            if (correct)
            {
                run.Gold += run.MiniGameQuizReward;
                run.RunLog.Add($"🧠 答題正確！+{run.MiniGameQuizReward}💰");
                await SaveAsync(run);
                return BuildPathEmbed(run, $"✅ 答對了！正確答案是 {answerLabel}。**+{run.MiniGameQuizReward}💰** 入袋！");
            }
            else
            {
                await SaveAsync(run);
                return BuildPathEmbed(run, $"❌ 答錯了！正確答案是 {answerLabel}。很可惜，下次加油！");
            }
        }

        #endregion

        #region 詛咒神器（Cursed Relic）
        // ── Cursed Relics ─────────────────────────────────────

        private (Embed embed, ComponentBuilder component) BuildCursedRelicEmbed(TowerRun run)
        {
            var desc = new StringBuilder();
            desc.AppendLine("⚠️ 詛咒降臨！你**必須**選擇一個詛咒承受……無路可逃！");
            desc.AppendLine();
            for (int i = 0; i < run.PendingCursedRelicChoices.Count; i++)
            {
                var r = _cursedRelics.FirstOrDefault(x => x.Id == run.PendingCursedRelicChoices[i]);
                if (r == null) continue;
                desc.AppendLine($"**{i + 1}. {r.Emoji} {r.Name}**");
                desc.AppendLine($"　　{r.Desc}");
                desc.AppendLine();
            }
            if (run.CursedRelicIds.Count > 0)
            {
                desc.AppendLine("─────────────────");
                desc.AppendLine($"已承受詛咒：{string.Join(" ", run.CursedRelicIds.Select(id => { var r = _cursedRelics.FirstOrDefault(x => x.Id == id); return r != null ? $"{r.Emoji}{r.Name}" : id; }))}");
            }

            var embed = new EmbedBuilder()
                .WithTitle("💀 詛咒降臨！")
                .WithDescription(desc.ToString())
                .WithColor(Color.DarkRed)
                .WithFooter($"{run.PlayerName} • 第 {run.CurrentFloor}/{run.MaxFloor} 層")
                .Build();

            var cb = new ComponentBuilder();
            for (int i = 0; i < run.PendingCursedRelicChoices.Count; i++)
            {
                var r = _cursedRelics.FirstOrDefault(x => x.Id == run.PendingCursedRelicChoices[i]);
                if (r == null) continue;
                cb.WithButton($"{r.Emoji} {r.Name}", $"tower_cursed_{run.ChannelId}_{i}", ButtonStyle.Danger, row: 0);
            }
            return (embed, cb);
        }

        public async Task<(Embed embed, ComponentBuilder component)> HandleCursedRelicChoiceAsync(ulong channelId, int idx)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            if (idx >= 0 && idx < run.PendingCursedRelicChoices.Count)
            {
                string chosenId = run.PendingCursedRelicChoices[idx];
                run.CursedRelicIds.Add(chosenId);
                ApplyCursedRelicOnPickup(run, chosenId);
                var curse = _cursedRelics.FirstOrDefault(r => r.Id == chosenId);
                run.RunLog.Add($"💀 承受詛咒【{curse?.Name}】！");
            }
            run.PendingCursedRelicChoices.Clear();
            run.State = TowerRunState.SelectingPath;
            await SaveAsync(run);
            return BuildPathEmbed(run);
        }

        private void ApplyCursedRelicOnPickup(TowerRun run, string curseId)
        {
            switch (curseId)
            {
                case "curse_half_pp":
                    foreach (var p in run.Party) foreach (var m in p.Moves) { m.MaxPP = Math.Max(1, m.MaxPP / 2); m.CurrentPP = Math.Min(m.CurrentPP, m.MaxPP); }
                    foreach (var m in run.ActivePokemon.Moves) { m.MaxPP = Math.Max(1, m.MaxPP / 2); m.CurrentPP = Math.Min(m.CurrentPP, m.MaxPP); }
                    break;
                case "curse_slow":
                    foreach (var p in run.Party) p.Speed = (int)(p.Speed * 0.6f);
                    break;
                case "curse_weak_atk":
                    foreach (var p in run.Party) { p.Attack = (int)(p.Attack * 0.75f); p.SpecialAttack = (int)(p.SpecialAttack * 0.75f); }
                    break;
                case "curse_fragile":
                    foreach (var p in run.Party) { p.Defense = (int)(p.Defense * 0.7f); p.SpecialDefense = (int)(p.SpecialDefense * 0.7f); }
                    break;
                case "curse_hp_cap":
                    foreach (var p in run.Party) { int loss = Math.Max(1, (int)(p.MaxHP * 0.2f)); p.MaxHP -= loss; p.CurrentHP = Math.Min(p.CurrentHP, p.MaxHP); }
                    break;
                case "curse_silence":
                    var strongestMove = run.ActivePokemon.Moves.OrderByDescending(m => m.Power).FirstOrDefault();
                    if (strongestMove != null) { strongestMove.MaxPP = 1; strongestMove.CurrentPP = Math.Min(1, strongestMove.CurrentPP); }
                    break;
                // curse_forget, curse_weaken, curse_gold_drain, curse_mirror, curse_fragile2,
                // curse_hungry, curse_unlucky, curse_decay, curse_paranoia: effects in battle/path methods
                default:
                    break;
            }
        }

        private (Embed embed, ComponentBuilder component) BuildRelicEmbed(TowerRun run)
        {
            var desc = new StringBuilder();
            desc.AppendLine("✨ 爬塔獎勵！從以下 **3 件神器**中選擇一件：");
            desc.AppendLine();
            for (int i = 0; i < run.PendingRelicChoices.Count; i++)
            {
                var r = _relics.FirstOrDefault(x => x.Id == run.PendingRelicChoices[i]);
                if (r == null) continue;
                desc.AppendLine($"**{i + 1}. {r.Emoji} {r.Name}**");
                desc.AppendLine($"　　{r.Desc}");
                desc.AppendLine();
            }
            if (run.RelicIds.Count > 0)
            {
                desc.AppendLine("─────────────────");
                desc.AppendLine($"已擁有神器：{string.Join(" ", run.RelicIds.Select(id => { var r = _relics.FirstOrDefault(x => x.Id == id); return r != null ? $"{r.Emoji}{r.Name}" : id; }))}");
            }

            var embed = new EmbedBuilder()
                .WithTitle("🏺 神器選擇")
                .WithDescription(desc.ToString())
                .WithColor(new Color(180, 100, 255))
                .WithFooter($"{run.PlayerName} • 第 {run.CurrentFloor}/{run.MaxFloor} 層")
                .Build();

            var cb = new ComponentBuilder();
            for (int i = 0; i < run.PendingRelicChoices.Count; i++)
            {
                var r = _relics.FirstOrDefault(x => x.Id == run.PendingRelicChoices[i]);
                if (r == null) continue;
                cb.WithButton($"{r.Emoji} {r.Name}", $"tower_relic_{run.ChannelId}_{i}", ButtonStyle.Primary, row: 0);
            }
            cb.WithButton("跳過", $"tower_relic_{run.ChannelId}_3", ButtonStyle.Secondary, row: 1);
            return (embed, cb);
        }

        #endregion

        #region 通用輔助方法

        private TowerPokemon ConvertPokemon(PokeGamePokemon src) => new()
        {
            PokeId = src.Id,
            Name = src.Name,
            CustomName = src.CustomName,
            Types = src.Types?.ToList() ?? new(),
            MaxHP = src.HP,
            CurrentHP = src.HP,
            Attack = src.Attack,
            Defense = src.Defense,
            SpecialAttack = src.SpecialAttack,
            SpecialDefense = src.SpecialDefense,
            Speed = src.Speed,
            IsShiny = src.isShiny,
            CaughtAt = src.CaughtDate,
            ImageUrl = src.Front_GIF ?? src.ImageUrl,
            BackImageUrl = src.Back_GIF ?? src.Back_ImageUrl,
        };

        // ── 圖片 URL helpers (供 Program.cs 送訊息用) ─────────────
        public (string enemyUrl, string playerUrl) GetBattleImageUrls(ulong channelId)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run)) return (null, null);
            string enemy = run.CurrentEnemy?.FrontGifUrl ?? run.CurrentEnemy?.FrontFallbackUrl;
            string player = run.ActivePokemon?.BackImageUrl ?? run.ActivePokemon?.ImageUrl;
            return (enemy, player);
        }

        public void SetBattleImageMsgIds(ulong channelId, ulong enemyMsgId, ulong playerMsgId)
        {
            if (_activeRuns.TryGetValue(channelId, out var run))
            {
                run.EnemyImgMsgId = enemyMsgId;
                run.PlayerImgMsgId = playerMsgId;
                _ = SaveAsync(run);
            }
        }

        public (ulong enemyMsgId, ulong playerMsgId) GetBattleImageMsgIds(ulong channelId)
        {
            if (_activeRuns.TryGetValue(channelId, out var run))
                return (run.EnemyImgMsgId, run.PlayerImgMsgId);
            return (0, 0);
        }

        public void ClearBattleImageMsgIds(ulong channelId)
        {
            if (_activeRuns.TryGetValue(channelId, out var run))
            {
                run.EnemyImgMsgId = 0;
                run.PlayerImgMsgId = 0;
            }
        }

        private TowerPokemon CatchFromEnemy(TowerEnemy e) => new()
        {
            PokeId = e.PokeId > 0 ? e.PokeId : -(Math.Abs(e.Name.GetHashCode()) % 10000),
            Name = e.Name.Replace("👑 ", ""),
            Types = e.Types?.ToList() ?? new(),
            MaxHP = e.MaxHP,
            CurrentHP = Math.Max(1, e.MaxHP / 3),
            Attack = e.Attack,
            Defense = e.Defense,
            SpecialAttack = e.SpecialAttack,
            SpecialDefense = e.SpecialDefense,
            Speed = e.Speed,
            ImageUrl = e.FrontGifUrl ?? e.FrontFallbackUrl,
            BackImageUrl = e.PokeId > 0
                ? $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/showdown/back/{e.PokeId}.gif"
                : null,
            Moves = e.Moves.Select(m => new TowerMove
            {
                Name = m.Name, Type = m.Type, Power = m.Power,
                Category = m.Category, Emoji = m.Emoji,
                MaxPP = m.MaxPP, CurrentPP = m.MaxPP
            }).ToList(),
            CaughtAt = DateTime.UtcNow,
        };

        private List<TowerMove> PickMoves(List<string> types) => PickMovesStatic(types);

        private static List<TowerMove> PickMovesStatic(List<string> types)
        {
            var relevant = (types ?? new()).Concat(new[] { "一般" }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var pool = _movePool
                .Where(m => relevant.Any(t => t.Equals(m.Type, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(_ => _rng.Next()).ToList();
            if (pool.Count < 4)
                pool.AddRange(_movePool.OrderBy(_ => _rng.Next()).Take(4 - pool.Count));
            return pool.Take(4).Select(m => new TowerMove
            {
                Name = m.Name, Type = m.Type, Power = m.Power,
                Category = m.Category, Emoji = m.Emoji,
                MaxPP = m.MaxPP, CurrentPP = m.MaxPP
            }).ToList();
        }

        private static async Task<TowerMove?> FetchMoveDetailAsync(string url)
        {
            try
            {
                var json = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("power", out var pp) || pp.ValueKind == JsonValueKind.Null) return null;
                int power = pp.GetInt32();
                if (power <= 0) return null;

                string typeEn = root.GetProperty("type").GetProperty("name").GetString() ?? "normal";
                string typeZh = _typeEnToZh.TryGetValue(typeEn, out var tz) ? tz : "一般";
                string dmg = root.GetProperty("damage_class").GetProperty("name").GetString() ?? "physical";
                int maxPP = root.TryGetProperty("pp", out var ppV) ? Math.Clamp(ppV.GetInt32(), 5, 15) : 10;

                string name = root.GetProperty("name").GetString() ?? "???";
                if (root.TryGetProperty("names", out var names))
                    foreach (var n in names.EnumerateArray())
                        if (n.GetProperty("language").GetProperty("name").GetString() == "zh-hant")
                        { name = n.GetProperty("name").GetString() ?? name; break; }

                string emoji = _typeEmoji.TryGetValue(typeZh, out var em) ? em : "💥";
                return new TowerMove { Name=name, Type=typeZh, Power=Math.Min(power,150),
                    Category=dmg=="special"?"Special":"Physical", Emoji=emoji, MaxPP=maxPP, CurrentPP=maxPP };
            }
            catch { return null; }
        }

        private static async Task<List<TowerMove>> FetchMovesFromApiAsync(int pokeId, List<string> fallbackTypes)
        {
            if (pokeId <= 0) return PickMovesStatic(fallbackTypes);

            if (_movesApiCache.TryGetValue(pokeId, out var cached) && cached.Count >= 4)
                return cached.OrderBy(_ => _rng.Next()).Take(4)
                    .Select(m => new TowerMove { Name=m.Name, Type=m.Type, Power=m.Power,
                        Category=m.Category, Emoji=m.Emoji, MaxPP=m.MaxPP, CurrentPP=m.MaxPP }).ToList();

            try
            {
                var json = await _http.GetStringAsync($"https://pokeapi.co/api/v2/pokemon/{pokeId}");
                using var doc = JsonDocument.Parse(json);
                var urls = doc.RootElement.GetProperty("moves").EnumerateArray()
                    .Select(m => m.GetProperty("move").GetProperty("url").GetString())
                    .Where(u => u != null).OrderBy(_ => _rng.Next()).Take(20).ToList();

                var tasks = urls.Select(u => FetchMoveDetailAsync(u!)).ToArray();
                var results = await Task.WhenAll(tasks);
                var valid = results.Where(m => m != null).Cast<TowerMove>().ToList();

                if (valid.Count > 0) _movesApiCache[pokeId] = valid;

                var picked = valid.Take(4).ToList();
                if (picked.Count < 4) picked.AddRange(PickMovesStatic(fallbackTypes).Take(4 - picked.Count));
                return picked;
            }
            catch { return PickMovesStatic(fallbackTypes); }
        }

        private TowerEnemy GenEnemy(int floor, bool isBoss)
        {
            IEnumerable<(string Name, string[] Types, int StatTotal, int PokeId)> tier;
            if (isBoss)          tier = _enemyPool.Where(e => e.StatTotal >= 590);
            else if (floor <= 3) tier = _enemyPool.Where(e => e.StatTotal < 430);
            else if (floor <= 6) tier = _enemyPool.Where(e => e.StatTotal >= 430 && e.StatTotal < 545);
            else                 tier = _enemyPool.Where(e => e.StatTotal >= 480 && e.StatTotal < 590);

            var choices = tier.ToList();
            if (choices.Count == 0) choices = _enemyPool;
            var t = choices[_rng.Next(choices.Count)];
            float scale = isBoss ? 1.0f + (floor - 1) * 0.09f : 1.0f + (floor - 1) * 0.06f;
            int b = Math.Max(30, (int)(t.StatTotal * scale / 7));
            int gold = isBoss ? (floor == 20 ? 120 : 80) : _rng.Next(20, 40);

            return new TowerEnemy
            {
                Name = isBoss ? $"👑 {t.Name}" : t.Name,
                PokeId = t.PokeId,
                Types = t.Types.ToList(),
                MaxHP = (int)(b * 1.6), CurrentHP = (int)(b * 1.6),
                Attack = b, Defense = (int)(b * 0.85),
                SpecialAttack = b, SpecialDefense = (int)(b * 0.85),
                Speed = b, IsBoss = isBoss, GoldReward = gold,
                Moves = PickMovesStatic(t.Types.ToList()),
            };
        }

        private int CalcDamage(TowerMove move, int atk, int spAtk, int def, int spDef, List<string> defTypes)
        {
            int a = move.Category == "Physical" ? atk : spAtk;
            int d = move.Category == "Physical" ? def : spDef;
            float eff = TypeEff(move.Type, defTypes);
            int raw = (int)(move.Power * a / (float)Math.Max(1, d) * eff / 5.0f);
            int base_ = Math.Max(1, (int)(raw * (0.85 + _rng.NextDouble() * 0.15)));
            // 高暴擊率：15% → 30%
            if (move.HighCrit && _rng.Next(100) < 30) base_ = (int)(base_ * 1.5f);
            return base_;
        }

        // ── 技能效果 Helper ────────────────────────────────────────

        /// <summary>能力段數倍率（-6~+6）</summary>
        private static float StatMult(int stage) =>
            (float)Math.Max(2, 2 + stage) / Math.Max(2, 2 - stage);

        private static string BuildStageStr(int atk, int def, int spAtk, int spDef, int spd)
        {
            var parts = new List<string>();
            if (atk   != 0) parts.Add($"攻{(atk>0?"+":"")}{atk}");
            if (def   != 0) parts.Add($"防{(def>0?"+":"")}{def}");
            if (spAtk != 0) parts.Add($"特攻{(spAtk>0?"+":"")}{spAtk}");
            if (spDef != 0) parts.Add($"特防{(spDef>0?"+":"")}{spDef}");
            if (spd   != 0) parts.Add($"速{(spd>0?"+":"")}{spd}");
            return parts.Count > 0 ? $"`{string.Join(" ", parts)}`" : "";
        }

        private static string StatusEmoji(string s) => s switch
        {
            "burn"   => "🔥", "para"   => "⚡", "freeze" => "❄️",
            "sleep"  => "💤", "poison" => "☠️", _ => ""
        };
        private static string StatusName(string s) => s switch
        {
            "burn"   => "燒傷", "para" => "麻痺", "freeze" => "冰凍",
            "sleep"  => "睡眠", "poison" => "中毒", _ => ""
        };

        /// <summary>應用攻擊方的招式效果（狀態/stat變化/吸血）至防守方。
        /// isPlayerAttacking: true=玩家打敵人，false=敵人打玩家</summary>
        private static void ApplyMoveEffect(
            TowerMove move, bool isPlayerAttacking,
            TowerPokemon player, TowerEnemy enemy,
            int damageDealt, StringBuilder sb, TowerRun run = null)
        {
            // 渾沌大師：玩家攻擊時，負面效果機率+30%
            int chaosBonus = (isPlayerAttacking && run != null && HasPassive(run, "passive_chaosmaster")) ? 30 : 0;

            // 吸血 / 後座力
            if (move.DrainPercent != 0 && damageDealt > 0)
            {
                int val = Math.Max(1, Math.Abs(damageDealt * move.DrainPercent / 100));
                if (move.DrainPercent > 0) // 吸血
                {
                    if (isPlayerAttacking) { player.CurrentHP = Math.Min(player.MaxHP, player.CurrentHP + val); sb.AppendLine($"  💚 {player.DisplayName} 吸收 +{val}HP"); }
                    else                   { enemy.CurrentHP  = Math.Min(enemy.MaxHP,  enemy.CurrentHP  + val); sb.AppendLine($"  💚 {enemy.Name} 吸收 +{val}HP"); }
                }
                else // 後座力
                {
                    if (isPlayerAttacking) { player.CurrentHP = Math.Max(1, player.CurrentHP - val); sb.AppendLine($"  💥 {player.DisplayName} 後座力 -{val}HP"); }
                    else                   { enemy.CurrentHP  = Math.Max(1, enemy.CurrentHP  - val); sb.AppendLine($"  💥 {enemy.Name} 後座力 -{val}HP"); }
                }
            }

            // 附加狀態（只對存活目標）
            if (!string.IsNullOrEmpty(move.EffectAilment) && move.EffectChance > 0)
            {
                if (move.EffectAilment == "flinch")
                {
                    // flinch 畏縮只影響敵方
                    int flinchChance = Math.Min(100, move.EffectChance + (isPlayerAttacking ? chaosBonus : 0));
                    if (isPlayerAttacking && enemy.CurrentHP > 0 && _rng.Next(100) < flinchChance)
                    { enemy.Flinched = true; sb.AppendLine($"  😨 {enemy.Name} 因畏縮無法行動！"); }
                }
                else
                {
                    int ailmentChance = Math.Min(100, move.EffectChance + chaosBonus);
                    if (_rng.Next(100) < ailmentChance)
                    {
                        if (isPlayerAttacking && string.IsNullOrEmpty(enemy.BattleStatus) && enemy.CurrentHP > 0)
                        {
                            enemy.BattleStatus = move.EffectAilment;
                            if (move.EffectAilment == "sleep") enemy.SleepTurns = _rng.Next(1, 4);
                            sb.AppendLine($"  {StatusEmoji(move.EffectAilment)} {enemy.Name} 陷入{StatusName(move.EffectAilment)}！");
                        }
                        else if (!isPlayerAttacking && string.IsNullOrEmpty(player.BattleStatus) && player.CurrentHP > 0)
                        {
                            player.BattleStatus = move.EffectAilment;
                            if (move.EffectAilment == "sleep") player.SleepTurns = _rng.Next(1, 4);
                            sb.AppendLine($"  {StatusEmoji(move.EffectAilment)} {player.DisplayName} 陷入{StatusName(move.EffectAilment)}！");
                        }
                    }
                }
            }

            // 能力變化
            if (!string.IsNullOrEmpty(move.StatTarget))
            {
                // 降能力也受渾沌大師加成（只針對負向變化打對方）
                bool isFoeDebuff = move.StatTarget.StartsWith("foe_") && move.StatStageChange < 0;
                int statChance = move.EffectChance == 0 ? 100
                    : Math.Min(100, move.EffectChance + (isFoeDebuff ? chaosBonus : 0));
                if (_rng.Next(100) < statChance)
                    ApplyStatChange(move.StatTarget, move.StatStageChange, isPlayerAttacking, player, enemy, sb);
            }
        }

        private static void ApplyStatChange(string statTarget, int change, bool isPlayerAttacking,
            TowerPokemon player, TowerEnemy enemy, StringBuilder sb)
        {
            bool isFoe  = statTarget.StartsWith("foe_");
            string stat = statTarget[(isFoe ? 4 : 5)..];
            // 「foe」相對攻擊方 → 攻擊方為玩家時 foe=敵人，攻擊方為敵人時 foe=玩家
            bool affectsPlayer = (isFoe && !isPlayerAttacking) || (!isFoe && isPlayerAttacking);

            string targetName;
            if (affectsPlayer)
            {
                switch (stat)
                {
                    case "atk":   player.AtkStage   = Math.Clamp(player.AtkStage   + change, -6, 6); break;
                    case "def":   player.DefStage   = Math.Clamp(player.DefStage   + change, -6, 6); break;
                    case "spd":   player.SpdStage   = Math.Clamp(player.SpdStage   + change, -6, 6); break;
                    case "spatk": player.SpAtkStage = Math.Clamp(player.SpAtkStage + change, -6, 6); break;
                    case "spdef": player.SpDefStage = Math.Clamp(player.SpDefStage + change, -6, 6); break;
                }
                targetName = player.DisplayName;
            }
            else
            {
                switch (stat)
                {
                    case "atk":   enemy.AtkStage   = Math.Clamp(enemy.AtkStage   + change, -6, 6); break;
                    case "def":   enemy.DefStage   = Math.Clamp(enemy.DefStage   + change, -6, 6); break;
                    case "spd":   enemy.SpdStage   = Math.Clamp(enemy.SpdStage   + change, -6, 6); break;
                    case "spatk": enemy.SpAtkStage = Math.Clamp(enemy.SpAtkStage + change, -6, 6); break;
                    case "spdef": enemy.SpDefStage = Math.Clamp(enemy.SpDefStage + change, -6, 6); break;
                }
                targetName = enemy.Name;
            }
            string sn = stat switch { "atk"=>"攻擊","def"=>"防禦","spd"=>"速度","spatk"=>"特攻","spdef"=>"特防",_=>stat };
            string dir = change > 0 ? $"⬆️上升{Math.Abs(change)}段" : $"⬇️下降{Math.Abs(change)}段";
            sb.AppendLine($"  💫 {targetName} 的{sn}{dir}！");
        }

        private static void ApplyEndOfTurnStatus(TowerPokemon poke, TowerEnemy enemy, StringBuilder sb)
        {
            if (poke.BattleStatus == "burn")
            {
                int d = Math.Max(1, poke.MaxHP / 16);
                poke.CurrentHP = Math.Max(0, poke.CurrentHP - d);
                sb.AppendLine($"  🔥 {poke.DisplayName} 受到燒傷 -{d}HP");
            }
            else if (poke.BattleStatus == "poison")
            {
                int d = Math.Max(1, poke.MaxHP / 8);
                poke.CurrentHP = Math.Max(0, poke.CurrentHP - d);
                sb.AppendLine($"  ☠️ {poke.DisplayName} 受到毒傷 -{d}HP");
            }

            if (enemy.BattleStatus == "burn")
            {
                int d = Math.Max(1, enemy.MaxHP / 16);
                enemy.CurrentHP = Math.Max(0, enemy.CurrentHP - d);
                sb.AppendLine($"  🔥 {enemy.Name} 受到燒傷 -{d}HP");
            }
            else if (enemy.BattleStatus == "poison")
            {
                int d = Math.Max(1, enemy.MaxHP / 8);
                enemy.CurrentHP = Math.Max(0, enemy.CurrentHP - d);
                sb.AppendLine($"  ☠️ {enemy.Name} 受到毒傷 -{d}HP");
            }
        }

        private float TypeEff(string moveType, List<string> defTypes)
        {
            if (!_typeChart.TryGetValue(moveType, out var row)) return 1f;
            float m = 1f;
            foreach (var dt in defTypes) if (row.TryGetValue(dt, out var v)) m *= v;
            return m;
        }

        private void AppendHit(StringBuilder sb, string atkName, string defName,
            TowerMove move, int dmg, List<string> defTypes, bool isPlayer)
        {
            float eff = TypeEff(move.Type, defTypes);
            string effNote = eff switch { >= 2f => "★超效", 0f => "×無效", < 1f => "▼不佳", _ => "" };
            string tag = isPlayer ? "▶" : "◀";
            sb.AppendLine($"{tag} {atkName} → {move.Emoji}{move.Name} -{dmg}HP {effNote}");
        }

        private string HpBar(int cur, int max, int len = 10)
        {
            float r = max == 0 ? 0 : (float)cur / max;
            int filled = (int)(r * len);
            string col = r > 0.5f ? "🟩" : r > 0.25f ? "🟨" : "🟥";
            return string.Concat(Enumerable.Repeat(col, Math.Max(0, filled)))
                 + string.Concat(Enumerable.Repeat("⬛", Math.Max(0, len - filled)))
                 + $" **{cur}/{max}**";
        }

        private string TypeBadge(List<string> types) =>
            string.Join(" ", (types ?? new()).Select(t => _typeEmoji.GetValueOrDefault(t, "❓") + t));

        private string MovesDisplay(TowerPokemon p) =>
            string.Join(" | ", p.Moves.Select(m => $"{m.Emoji}{m.Name}({m.CurrentPP}/{m.MaxPP}PP)"));

        // ── Random path generation ────────────────────────────
        private List<string> GenPaths(int floor)
        {
            if (floor % 10 == 0) return new() { "battle" }; // boss floors 10, 20
            if (floor == 9 || floor == 19) return new() { "shop", "rest" }; // pre-boss
            if (floor == 7 || floor == 17) return new() { "cursed_relic" }; // forced cursed relic
            var pool = new List<string> { "rest", "shop", "event", "casino" };
            pool = pool.OrderBy(_ => _rng.Next()).Take(1).ToList();
            pool.Add("battle");
            return pool.OrderBy(_ => _rng.Next()).ToList();
        }

        private (string Label, ButtonStyle Style, string Emoji) PathDisplay(string choice) => choice switch
        {
            "battle"       => ("⚔️ 戰鬥", ButtonStyle.Danger, "⚔️"),
            "rest"         => ("🏕️ 休息 +35%HP+PP", ButtonStyle.Success, "🏕️"),
            "shop"         => ("🏪 神秘商店", ButtonStyle.Secondary, "🏪"),
            "event"        => ("❓ 神秘事件", ButtonStyle.Primary, "❓"),
            "casino"       => ("🎲 賭場", ButtonStyle.Primary, "🎲"),
            "cursed_relic" => ("💀 詛咒降臨", ButtonStyle.Danger, "💀"),
            _              => ("?", ButtonStyle.Secondary, "?"),
        };

        #endregion

        #region Embed 建構器

        private (Embed embed, ComponentBuilder component) BuildPathEmbed(TowerRun run, string extra = "")
        {
            bool nextIsBoss = (run.CurrentFloor + 1) % 10 == 0;
            var p = run.ActivePokemon;
            if (run.CurrentPaths == null || run.CurrentPaths.Count == 0)
                run.CurrentPaths = GenPaths(run.CurrentFloor + 1);
            var paths = run.CurrentPaths;

            var desc = new StringBuilder();
            if (!string.IsNullOrEmpty(extra)) desc.AppendLine(extra).AppendLine();
            desc.AppendLine($"**{p.DisplayName}** {TypeBadge(p.Types)}");
            desc.AppendLine($"HP: {HpBar(p.CurrentHP, p.MaxHP)}");
            desc.AppendLine($"技能: {MovesDisplay(p)}");
            desc.AppendLine($"💰 金幣: **{run.Gold}** | ⭐ Lv.**{run.Level}** EXP {run.Exp}/{run.ExpToNext}");
            if (run.Party.Count > 1)
                desc.AppendLine($"🎒 背包: {string.Join("、", run.Party.Select(pk => $"{pk.DisplayName}({pk.CurrentHP}HP)"))}");
            if (run.RelicIds.Count > 0)
                desc.AppendLine($"🏺 {string.Join(" ", run.RelicIds.Select(id => { var r = _relics.FirstOrDefault(x => x.Id == id); return r != null ? $"{r.Emoji}{r.Name}" : ""; }))}");
            if (run.CursedRelicIds.Count > 0)
                desc.AppendLine($"💀 {string.Join(" ", run.CursedRelicIds.Select(id => { var r = _cursedRelics.FirstOrDefault(x => x.Id == id); return r != null ? $"{r.Emoji}{r.Name}" : ""; }))}");
            if (!string.IsNullOrEmpty(run.PassiveId))
            {
                var passive = _passives.FirstOrDefault(p => p.Id == run.PassiveId);
                if (passive != null) desc.AppendLine($"🌟 被動：{passive.Emoji} {passive.Name}");
            }
            desc.AppendLine();
            desc.AppendLine($"─ 進入第 **{run.CurrentFloor + 1}/{run.MaxFloor}** 層 {(nextIsBoss ? "⚠️ **BOSS**" : "")} ─");
            desc.AppendLine("選擇路線：");

            var embed = new EmbedBuilder()
                .WithTitle($"🏔️ 第 {run.CurrentFloor}/{run.MaxFloor} 層已清除")
                .WithDescription(desc.ToString())
                .WithColor(nextIsBoss ? Color.Gold : new Color(70, 130, 180))
                .WithFooter($"{run.PlayerName} • 累積傷害 {run.TotalDamageDealt}")
                .Build();

            var cb = new ComponentBuilder();
            for (int i = 0; i < paths.Count; i++)
            {
                var (label, style, _) = PathDisplay(paths[i]);
                cb.WithButton(label, $"tower_path_{run.ChannelId}_{paths[i]}", style, row: 0);
            }
            cb.WithButton("🔄 換寶可夢", $"tower_swap_request_{run.ChannelId}", ButtonStyle.Secondary, row: 1);

            return (embed, cb);
        }

        private (Embed embed, ComponentBuilder component) BuildBattleEmbed(TowerRun run)
        {
            var p = run.ActivePokemon;
            var e = run.CurrentEnemy;
            var nextMove = e.Moves[e.NextMoveIdx % e.Moves.Count];

            var desc = new StringBuilder();
            string pStatus = !string.IsNullOrEmpty(p.BattleStatus) ? $" {StatusEmoji(p.BattleStatus)}" : "";
            string pStages = BuildStageStr(p.AtkStage, p.DefStage, p.SpAtkStage, p.SpDefStage, p.SpdStage);
            desc.AppendLine($"**你的 {p.DisplayName}**{pStatus} {TypeBadge(p.Types)}");
            desc.AppendLine($"HP: {HpBar(p.CurrentHP, p.MaxHP)}{(pStages != "" ? $"  {pStages}" : "")}");
            desc.AppendLine($"技能: {MovesDisplay(p)}");
            desc.AppendLine();
            string eStatus = !string.IsNullOrEmpty(e.BattleStatus) ? $" {StatusEmoji(e.BattleStatus)}" : "";
            string eStages = BuildStageStr(e.AtkStage, e.DefStage, e.SpAtkStage, e.SpDefStage, e.SpdStage);
            desc.AppendLine($"**{(e.IsBoss ? "👑" : "🎯")} {e.Name}**{eStatus} {TypeBadge(e.Types)}");
            desc.AppendLine($"HP: {HpBar(e.CurrentHP, e.MaxHP)}{(eStages != "" ? $"  {eStages}" : "")}");
            desc.AppendLine();
            bool isBlind = run.CursedRelicIds.Contains("curse_blind");
            if (isBlind)
                desc.AppendLine($"🔮 **{e.Name}** 的下一步：**???**（詛咒遮蔽）");
            else if (HasPassive(run, "passive_strategist"))
            {
                var move1 = e.Moves[e.NextMoveIdx % e.Moves.Count];
                var move2 = e.Moves[(e.NextMoveIdx + 1) % e.Moves.Count];
                desc.AppendLine($"🔮 **{e.Name}** 預告未來兩步：{move1.Emoji}**{move1.Name}** → {move2.Emoji}**{move2.Name}**");
            }
            else
                desc.AppendLine($"🔮 **{e.Name}** 預告下一步：{nextMove.Emoji}**{nextMove.Name}**（{nextMove.Power}威力）");

            if (!string.IsNullOrEmpty(run.CurrentBattleLog))
            {
                desc.AppendLine();
                desc.AppendLine("```");
                desc.AppendLine(run.CurrentBattleLog);
                desc.AppendLine("```");
            }

            var embed = new EmbedBuilder()
                .WithTitle($"⚔️ 第 {run.CurrentFloor}/{run.MaxFloor} 層 — 戰鬥！")
                .WithDescription(desc.ToString())
                .WithColor(e.IsBoss ? Color.Gold : Color.Red)
                .WithFooter($"{run.PlayerName} • 💰{run.Gold}")
                .Build();

            var cb = new ComponentBuilder();
            var row = new ActionRowBuilder();
            for (int i = 0; i < p.Moves.Count; i++)
            {
                var m = p.Moves[i];
                bool noPP = m.CurrentPP <= 0;
                row.AddComponent(new ButtonBuilder()
                    .WithLabel($"{m.Emoji}{m.Name}({m.CurrentPP}PP)")
                    .WithCustomId($"tower_move_{run.ChannelId}_{i}")
                    .WithStyle(noPP ? ButtonStyle.Secondary : ButtonStyle.Primary)
                    .WithDisabled(noPP));
            }
            cb.AddRow(row);
            cb.WithButton("🔄 換寶可夢", $"tower_swap_request_{run.ChannelId}", ButtonStyle.Secondary, row: 1);
            return (embed, cb);
        }

        private (Embed embed, ComponentBuilder component) BuildPowerUpgradeEmbed(TowerRun run, string notice = "")
        {
            var p = run.ActivePokemon;
            var desc = new StringBuilder();
            int upLimit = HasPassive(run, "passive_techgeek") ? 8 : 5;
            if (!string.IsNullOrEmpty(notice)) desc.AppendLine($"⚠️ {notice}").AppendLine();
            desc.AppendLine($"選擇想**強化威力的招式**（+20 威力，每招最多強化 {upLimit} 次）：");
            desc.AppendLine();
            for (int i = 0; i < p.Moves.Count; i++)
            {
                var m = p.Moves[i];
                string upgradeTag = m.UpgradeCount >= upLimit ? "🔒 已達上限" : $"{m.UpgradeCount}/{upLimit}";
                string arrow = m.UpgradeCount >= upLimit ? $"威力 **{m.Power}**" : $"威力 **{m.Power}** → **{m.Power + 20}**";
                desc.AppendLine($"{i + 1}. {m.Emoji}**{m.Name}** — {m.Type} {m.Category}  {arrow}  `{upgradeTag}`");
            }

            var embed = new EmbedBuilder()
                .WithTitle("⚡ 威力升級")
                .WithDescription(desc.ToString())
                .WithColor(new Color(255, 200, 0))
                .WithFooter($"{run.PlayerName} • 第 {run.CurrentFloor}/{run.MaxFloor} 層")
                .Build();

            var cb = new ComponentBuilder();
            var upgradeRow = new ActionRowBuilder();
            for (int i = 0; i < p.Moves.Count; i++)
            {
                var m = p.Moves[i];
                bool maxed = m.UpgradeCount >= upLimit;
                string label = maxed ? $"{m.Emoji}{m.Name}(上限)" : $"{m.Emoji}{m.Name}({m.Power}→{m.Power + 20})";
                upgradeRow.WithButton(new ButtonBuilder()
                    .WithLabel(label)
                    .WithCustomId($"tower_powerup_select_{run.ChannelId}_{i}")
                    .WithStyle(maxed ? ButtonStyle.Secondary : ButtonStyle.Primary)
                    .WithDisabled(maxed));
            }
            cb.AddRow(upgradeRow);
            cb.WithButton("跳過", $"tower_powerup_select_{run.ChannelId}_4", ButtonStyle.Secondary, row: 1);
            if (run.PowerUpgradeReturn == "battle")
                cb.WithButton("📀 換技能代替", $"tower_powerup_switch_{run.ChannelId}", ButtonStyle.Secondary, row: 1);
            return (embed, cb);
        }

        /// <summary>從威力升級切換到技能獎勵（戰鬥後二選一）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> SwitchToMoveRewardAsync(ulong channelId)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");
            run.PowerUpgradeReturn = "";
            run.State = TowerRunState.SelectingMoveReward;
            await SaveAsync(run);
            return BuildMoveRewardEmbed(run);
        }

        private (Embed embed, ComponentBuilder component) BuildRestEmbed(TowerRun run, int healedHp)
        {
            var p = run.ActivePokemon;
            string healText = healedHp > 0 ? $"恢復了 **{healedHp} HP** 並回復所有技能 PP。" : "已休息完畢。";
            var desc = new StringBuilder();
            desc.AppendLine($"🏕️ **{p.DisplayName}** {healText}");
            desc.AppendLine($"HP: {HpBar(p.CurrentHP, p.MaxHP)}");
            desc.AppendLine($"技能: {MovesDisplay(p)}");
            desc.AppendLine();
            desc.AppendLine("想趁休息時**換一個技能**嗎？");

            var embed = new EmbedBuilder()
                .WithTitle("🏕️ 休息")
                .WithDescription(desc.ToString())
                .WithColor(new Color(100, 200, 100))
                .WithFooter($"{run.PlayerName} • 第 {run.CurrentFloor}/{run.MaxFloor} 層")
                .Build();

            var cb = new ComponentBuilder()
                .WithButton("🔃 換技能", $"tower_rest_swap_{run.ChannelId}", ButtonStyle.Primary, row: 0)
                .WithButton("⚡ 強化招式", $"tower_powerup_{run.ChannelId}_rest", ButtonStyle.Primary, row: 0)
                .WithButton("繼續前進", $"tower_rest_continue_{run.ChannelId}", ButtonStyle.Secondary, row: 0);
            return (embed, cb);
        }

        private (Embed embed, ComponentBuilder component) BuildShopEmbed(TowerRun run, string notice = "")
        {
            var p = run.ActivePokemon;
            var desc = new StringBuilder();
            if (!string.IsNullOrEmpty(notice)) desc.AppendLine($"⚠️ {notice}").AppendLine();
            desc.AppendLine($"**{p.DisplayName}** HP: {HpBar(p.CurrentHP, p.MaxHP)}");
            desc.AppendLine($"技能: {MovesDisplay(p)}");
            desc.AppendLine($"💰 金幣: **{run.Gold}**");
            desc.AppendLine();
            int cHealFull   = ShopCost(run, 30, "heal_full");
            int cHealHalf   = ShopCost(run, 15, "heal_half");
            int cPpRestore  = ShopCost(run, 20, "pp_restore");
            int cNewMove    = ShopCost(run, 25, "new_move");
            int cNormal     = ShopCost(run, 8,  "buy_normal");
            int cSuper      = ShopCost(run, 15, "buy_super");
            int cUltra      = ShopCost(run, 25, "buy_ultra");
            int cPowerUp    = ShopCost(run, 20, "powerup");
            desc.AppendLine("**商品：**");
            desc.AppendLine($"💊 **全回復** — HP完全恢復 ({cHealFull}💰)");
            desc.AppendLine($"🧃 **超級樹果** — 恢復50% HP ({cHealHalf}💰)");
            desc.AppendLine($"🔋 **PP全回復** — 所有技能PP滿 ({cPpRestore}💰)");
            desc.AppendLine($"📀 **技能學習器** — 三選一學習技能 ({cNewMove}💰)");
            desc.AppendLine($"⚽ **普通球×3** — 30%捕獲率 ({cNormal}💰)");
            desc.AppendLine($"🔵 **超級球×2** — 55%捕獲率 ({cSuper}💰)");
            desc.AppendLine($"🟡 **高級球×1** — 75%捕獲率 ({cUltra}💰)");
            desc.AppendLine($"⚡ **強化招式** — 選一招威力+20 ({cPowerUp}💰)");
            desc.AppendLine($"\n現有球：{BallsDisplay(run)}");

            var cb = new ComponentBuilder()
                .WithButton($"💊 全回復({cHealFull}💰)",    $"tower_shop_{run.ChannelId}_heal_full",  ButtonStyle.Success,   row: 0)
                .WithButton($"🧃 超級樹果({cHealHalf}💰)",  $"tower_shop_{run.ChannelId}_heal_half",  ButtonStyle.Primary,   row: 0)
                .WithButton($"🔋 PP全回復({cPpRestore}💰)", $"tower_shop_{run.ChannelId}_pp_restore", ButtonStyle.Primary,   row: 1)
                .WithButton($"📀 學習技能({cNewMove}💰)",   $"tower_shop_{run.ChannelId}_new_move",   ButtonStyle.Secondary, row: 1)
                .WithButton($"⚽ 普通球×3({cNormal}💰)",   $"tower_shop_{run.ChannelId}_buy_normal", ButtonStyle.Secondary, row: 2)
                .WithButton($"🔵 超級球×2({cSuper}💰)",    $"tower_shop_{run.ChannelId}_buy_super",  ButtonStyle.Primary,   row: 2)
                .WithButton($"🟡 高級球×1({cUltra}💰)",    $"tower_shop_{run.ChannelId}_buy_ultra",  ButtonStyle.Primary,   row: 2)
                .WithButton($"⚡ 強化招式({cPowerUp}💰)",    $"tower_powerup_{run.ChannelId}_shop",    ButtonStyle.Primary,   row: 3)
                .WithButton("離開商店", $"tower_shop_{run.ChannelId}_leave", ButtonStyle.Danger, row: 4);

            return (new EmbedBuilder()
                .WithTitle("🏪 神秘商店")
                .WithDescription(desc.ToString())
                .WithColor(new Color(255, 215, 0)).Build(), cb);
        }

        private (Embed embed, ComponentBuilder component) BuildEventEmbed(TowerRun run)
        {
            if (run.PendingEventIdx < 0 || run.PendingEventIdx >= _events.Count)
                return BuildPathEmbed(run);
            var ev = _events[run.PendingEventIdx];

            var desc = new StringBuilder();
            desc.AppendLine($"_{ev.Desc}_");
            if (ev.Title == "神秘數學題" && !string.IsNullOrEmpty(run.MathProblemText))
            {
                desc.AppendLine();
                desc.AppendLine($"```\n{run.MathProblemText}\n```");
                desc.AppendLine("**求 x 和 y 的值：**");
            }
            desc.AppendLine();
            desc.AppendLine("**請選擇應對方式：**");
            for (int i = 0; i < ev.Choices.Count; i++)
                desc.AppendLine($"{ev.Choices[i].Emoji} **{ev.Choices[i].Label}**");
            desc.AppendLine();
            desc.AppendLine($"**{run.ActivePokemon.DisplayName}** HP: {HpBar(run.ActivePokemon.CurrentHP, run.ActivePokemon.MaxHP, 6)}　💰 {run.Gold}");

            var cb = new ComponentBuilder();
            bool isMathEvent = ev.Title == "神秘數學題" && run.MathChoiceLabels.Count == 3;
            for (int i = 0; i < ev.Choices.Count; i++)
            {
                string label = isMathEvent ? run.MathChoiceLabels[i] : ev.Choices[i].Label;
                cb.WithButton(label, $"tower_event_{run.ChannelId}_{i}", ButtonStyle.Primary, row: i / 3);
            }

            return (new EmbedBuilder()
                .WithTitle($"{ev.Emoji} 神秘事件：{ev.Title}")
                .WithDescription(desc.ToString())
                .WithColor(new Color(148, 0, 211)).Build(), cb);
        }

        private (Embed embed, ComponentBuilder component) BuildMoveRewardEmbed(TowerRun run, string notice = "")
        {
            var desc = new StringBuilder();
            if (!string.IsNullOrEmpty(notice)) desc.AppendLine(notice).AppendLine("---").AppendLine();
            else desc.AppendLine($"🎉 擊倒 **{run.CurrentEnemy?.Name ?? "敵人"}**，獲得 **{run.CurrentEnemy?.GoldReward ?? 0} 💰**！").AppendLine();
            desc.AppendLine("✨ **選擇一個技能學習（可跳過）：**");
            for (int i = 0; i < run.PendingMoveRewards.Count; i++)
            {
                var m = run.PendingMoveRewards[i];
                string cat = m.Category == "Physical" ? "物理" : "特殊";
                desc.AppendLine($"{i + 1}. {m.Emoji}**{m.Name}** （{m.Type}屬性 · {m.Power}威力 · {cat} · {m.MaxPP}PP）");
            }
            desc.AppendLine();
            desc.AppendLine($"現有技能：{MovesDisplay(run.ActivePokemon)}");

            var cb = new ComponentBuilder();
            for (int i = 0; i < run.PendingMoveRewards.Count; i++)
            {
                var m = run.PendingMoveRewards[i];
                cb.WithButton($"{m.Emoji}{m.Name}", $"tower_movereward_{run.ChannelId}_{i}", ButtonStyle.Primary, row: 0);
            }
            cb.WithButton("⏭️ 跳過", $"tower_movereward_{run.ChannelId}_3", ButtonStyle.Secondary, row: 0);

            return (new EmbedBuilder()
                .WithTitle("🏆 戰鬥勝利！")
                .WithDescription(desc.ToString())
                .WithColor(Color.Green).Build(), cb);
        }

        private (Embed embed, ComponentBuilder component) BuildMoveSlotEmbed(TowerRun run)
        {
            var nm = run.PendingSelectedMove;
            var desc = new StringBuilder();
            desc.AppendLine($"學習 {nm.Emoji}**{nm.Name}**（{nm.Type} · {nm.Power}威力 · {nm.MaxPP}PP）");
            desc.AppendLine();
            desc.AppendLine("**選擇要替換的技能：**");
            for (int i = 0; i < run.ActivePokemon.Moves.Count; i++)
            {
                var m = run.ActivePokemon.Moves[i];
                desc.AppendLine($"{i + 1}. {m.Emoji}**{m.Name}**（{m.Power}威力 · {m.CurrentPP}/{m.MaxPP}PP）");
            }

            var cb = new ComponentBuilder();
            for (int i = 0; i < run.ActivePokemon.Moves.Count; i++)
                cb.WithButton(run.ActivePokemon.Moves[i].Name, $"tower_moveslot_{run.ChannelId}_{i}", ButtonStyle.Primary, row: 0);
            cb.WithButton("取消", $"tower_moveslot_{run.ChannelId}_4", ButtonStyle.Secondary, row: 0);

            return (new EmbedBuilder()
                .WithTitle("📀 選擇替換技能槽")
                .WithDescription(desc.ToString())
                .WithColor(Color.Blue).Build(), cb);
        }

        private (Embed embed, ComponentBuilder component) BuildCatchEmbed(TowerRun run, string notice = "")
        {
            var e = run.PendingCatch;
            var desc = new StringBuilder();
            if (!string.IsNullOrEmpty(notice)) desc.AppendLine(notice).AppendLine();
            desc.AppendLine($"野生的 **{e.Name}** {TypeBadge(e.Types)} 可以捕獲！");
            desc.AppendLine($"HP: {HpBar(e.MaxHP / 3, e.MaxHP)} · ATK: {e.Attack} · DEF: {e.Defense}");
            desc.AppendLine($"技能: {string.Join("、", e.Moves.Select(m => m.Name))}");
            desc.AppendLine();
            desc.AppendLine($"🎒 背包：{run.Party.Count}/3");
            desc.AppendLine($"🎾 持有的球：{BallsDisplay(run)}");

            bool hasBalls = run.Balls.Any(b => b.Value > 0);
            var cb = new ComponentBuilder();
            int btnRow = 0;
            foreach (var (key, info) in _balls)
            {
                if (run.Balls.TryGetValue(key, out int cnt) && cnt > 0)
                {
                    cb.WithButton($"{info.Emoji}{info.DisplayName}×{cnt}({info.Rate:P0})",
                        $"tower_catch_{run.ChannelId}_{key}", ButtonStyle.Primary, row: btnRow / 3);
                    btnRow++;
                }
            }
            cb.WithButton("放走", $"tower_catch_{run.ChannelId}_pass", ButtonStyle.Secondary, row: 1);

            return (new EmbedBuilder()
                .WithTitle("🎯 要嘗試捕獲嗎？")
                .WithDescription(desc.ToString())
                .WithColor(hasBalls ? Color.Orange : Color.DarkGrey).Build(), cb);
        }

        private (Embed embed, ComponentBuilder component) BuildCatchSwapEmbed(TowerRun run, TowerPokemon newPoke)
        {
            var desc = new StringBuilder();
            desc.AppendLine($"🎉 成功捕獲 **{newPoke.Name}** {TypeBadge(newPoke.Types)}！");
            desc.AppendLine($"但背包已滿（3/3）。選擇釋放哪一隻，或取消：");
            desc.AppendLine();
            for (int i = 0; i < run.Party.Count; i++)
            {
                var p = run.Party[i];
                bool active = p.PokeId == run.ActivePokemon.PokeId && p.CaughtAt == run.ActivePokemon.CaughtAt;
                desc.AppendLine($"{i + 1}. {(active ? "▶ " : "")}{p.DisplayName} {TypeBadge(p.Types)} HP:{p.CurrentHP}/{p.MaxHP}");
            }

            var cb = new ComponentBuilder();
            for (int i = 0; i < run.Party.Count; i++)
                cb.WithButton($"釋放 {run.Party[i].DisplayName}", $"tower_catchswap_{run.ChannelId}_{i}", ButtonStyle.Danger, row: 0);
            cb.WithButton("取消", $"tower_catchswap_{run.ChannelId}_cancel", ButtonStyle.Secondary, row: 1);

            return (new EmbedBuilder()
                .WithTitle("🔄 背包已滿 — 選擇釋放")
                .WithDescription(desc.ToString())
                .WithColor(Color.Gold).Build(), cb);
        }

        private string BallsDisplay(TowerRun run) =>
            run.Balls.Any()
                ? string.Join(" ", run.Balls
                    .Where(b => b.Value > 0)
                    .Select(b => _balls.TryGetValue(b.Key, out var info)
                        ? $"{info.Emoji}{info.DisplayName}×{b.Value}"
                        : $"{b.Key}×{b.Value}"))
                : "（無）";

        private (Embed embed, ComponentBuilder component) BuildVictoryEmbed(TowerRun run)
        {
            var elapsed = (int)(DateTime.UtcNow - run.StartedAt).TotalMinutes;
            return (new EmbedBuilder()
                .WithTitle("🎉🏆 媽媽 我成功登上荒版大樓的塔頂了")
                .WithDescription(
                    $"**{run.PlayerName}** 帶著 **{run.ActivePokemon.DisplayName}** 征服了全 **{run.MaxFloor}** 層！\n\n" +
                    $"📊 **最終成績**\n" +
                    $"• 攻克：{run.MaxFloor}/{run.MaxFloor} 層\n" +
                    $"• 累積傷害：{run.TotalDamageDealt}\n" +
                    $"• 剩餘 HP：{run.ActivePokemon.CurrentHP}/{run.ActivePokemon.MaxHP}\n" +
                    $"• 金幣：{run.Gold} 💰\n" +
                    $"• 用時：{elapsed} 分鐘\n\n" +
                    $"✨ **塔頂限定獎勵**：下一次使用 /抓pokemon 保證閃光！（限一次）")
                .WithColor(Color.Gold).Build(), new ComponentBuilder());
        }

        private (Embed embed, ComponentBuilder component) BuildDefeatEmbed(TowerRun run)
        {
            var elapsed = (int)(DateTime.UtcNow - run.StartedAt).TotalMinutes;
            var desc = new StringBuilder();
            desc.AppendLine($"**{run.PlayerName}** 的全部寶可夢在第 **{run.CurrentFloor}** 層倒下。\n");
            desc.AppendLine($"📊 **成績**");
            desc.AppendLine($"• 攻克：{run.CurrentFloor - 1}/{run.MaxFloor} 層");
            desc.AppendLine($"• 累積傷害：{run.TotalDamageDealt}");
            desc.AppendLine($"• 用時：{elapsed} 分鐘");
            if (!string.IsNullOrEmpty(run.CurrentBattleLog))
            {
                desc.AppendLine();
                desc.AppendLine("⚔️ **最後戰況：**");
                // Show only the last round
                var rounds = run.CurrentBattleLog.Split(new[] { "════════" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
                if (rounds.Count > 0)
                    desc.AppendLine(rounds.Last());
            }
            desc.AppendLine("\n下次再挑戰！");
            return (new EmbedBuilder()
                .WithTitle("💀 全滅...")
                .WithDescription(desc.ToString())
                .WithColor(Color.DarkRed).Build(), new ComponentBuilder());
        }

        private static string MathReward(TowerRun run)
        {
            int g = _rng.Next(30, 60); run.Gold += g;
            int exp = 80; run.Exp += exp;
            return $"獲得 **{g} 金幣** 和 **{exp} EXP**！";
        }

        private static string MathPunish(TowerRun run)
        {
            int dmg = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.25));
            run.ActivePokemon.CurrentHP = Math.Max(1, run.ActivePokemon.CurrentHP - dmg);
            return $"**{run.ActivePokemon.DisplayName}** 受到 **{dmg}** 傷害！";
        }

        private static void GenerateMathEvent(TowerRun run)
        {
            var r = new Random(run.CurrentFloor * 7919 + (int)(run.PlayerId % 1000));
            int x = r.Next(1, 9), y = r.Next(1, 9);
            int a1 = r.Next(1, 5), b1 = r.Next(1, 5);
            int a2 = r.Next(1, 5), b2 = r.Next(1, 5);
            // Ensure unique equations
            while (a1 * b2 == a2 * b1) { a2 = r.Next(1, 5); b2 = r.Next(1, 5); }
            int c1 = a1 * x + b1 * y;
            int c2 = a2 * x + b2 * y;
            run.MathProblemText = $"{a1}x + {b1}y = {c1}\n{a2}x + {b2}y = {c2}";

            int correct = r.Next(3);
            run.MathCorrectChoice = correct;
            run.MathChoiceLabels = new List<string>();
            var usedWrong = new HashSet<string>();
            for (int i = 0; i < 3; i++)
            {
                if (i == correct)
                {
                    run.MathChoiceLabels.Add($"x={x}, y={y}");
                }
                else
                {
                    int wx, wy;
                    string key;
                    do {
                        wx = Math.Clamp(x + r.Next(-3, 4), 1, 12);
                        wy = Math.Clamp(y + r.Next(-3, 4), 1, 12);
                        key = $"{wx},{wy}";
                    } while ((wx == x && wy == y) || usedWrong.Contains(key));
                    usedWrong.Add(key);
                    run.MathChoiceLabels.Add($"x={wx}, y={wy}");
                }
            }
        }

        private (Embed embed, ComponentBuilder component) ErrEmbed(string msg) =>
            (new EmbedBuilder().WithTitle("❌ 錯誤").WithDescription(msg).WithColor(Color.Red).Build(),
             new ComponentBuilder());

        #endregion

        #region 資料持久化（Redis / 記憶體）

        private async Task SaveAsync(TowerRun run)
        {
            _activeRuns[run.ChannelId] = run;
            if (!_useRedis) return;
            try
            {
                await _redisDb.StringSetAsync(
                    $"{REDIS_PREFIX}{run.ChannelId}",
                    JsonSerializer.Serialize(run),
                    TimeSpan.FromDays(30));
            }
            catch (Exception ex) { Console.WriteLine($"[Tower] Redis save: {ex.Message}"); }
        }

        private async Task RemoveAsync(ulong channelId)
        {
            _activeRuns.Remove(channelId);
            if (!_useRedis) return;
            try { await _redisDb.KeyDeleteAsync($"{REDIS_PREFIX}{channelId}"); } catch { }
        }

        private async Task LoadRunsAsync()
        {
            if (!_useRedis) return;
            try
            {
                var server = _redisDb.Multiplexer.GetServer(_redisDb.Multiplexer.GetEndPoints()[0]);
                await foreach (var key in server.KeysAsync(pattern: $"{REDIS_PREFIX}*"))
                {
                    var val = await _redisDb.StringGetAsync(key);
                    if (!val.HasValue) continue;
                    var run = JsonSerializer.Deserialize<TowerRun>(val.ToString());
                    if (run != null && run.State != TowerRunState.Victory && run.State != TowerRunState.Defeated)
                        _activeRuns[run.ChannelId] = run;
                }
                Console.WriteLine($"[Tower] 載入 {_activeRuns.Count} 個進行中爬塔");
            }
            catch (Exception ex) { Console.WriteLine($"[Tower] Redis load: {ex.Message}"); }
        }

        #endregion
    }
}
