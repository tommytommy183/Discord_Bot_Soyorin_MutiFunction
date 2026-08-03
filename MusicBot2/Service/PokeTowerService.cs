using System;
using System.Collections.Generic;
using System.Linq;
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
        Defeated
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
        public int MaxFloor { get; set; } = 10;
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
    }

    public class PokeTowerService
    {
        private readonly IDatabase _redisDb;
        private readonly bool _useRedis;
        private readonly Dictionary<ulong, TowerRun> _activeRuns = new();
        private const string REDIS_PREFIX = "tower:run:";
        private static readonly Random _rng = new();

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
            // 妖精
            new() { Name="月亮之力",   Type="妖精",   Power=95,  Category="Special",  Emoji="🌸", MaxPP=10 },
            new() { Name="粗野播弄",   Type="妖精",   Power=90,  Category="Physical", Emoji="🎀", MaxPP=10 },
            new() { Name="耀眼魅力",   Type="妖精",   Power=80,  Category="Special",  Emoji="✨", MaxPP=10 },
        };

        // ── 中文敵人池 (Name, Types, StatTotal, PokeApiId) ────────
        private static readonly List<(string Name, string[] Types, int StatTotal, int PokeId)> _enemyPool = new()
        {
            // 低階 (floor 1-3)
            ("比雕",     new[]{"一般","飛行"}, 349, 22),
            ("隆隆石",   new[]{"岩石","地面"}, 390, 74),
            ("鬼斯通",   new[]{"幽靈","毒"},   405, 93),
            ("卡咪龜",   new[]{"水"},           405, 8),
            ("火恐龍",   new[]{"火"},           405, 5),
            ("妙蛙草",   new[]{"草","毒"},      405, 2),
            ("電擊獸",   new[]{"電"},           490, 26),
            ("皮卡丘",   new[]{"電"},           320, 25),
            ("瞌睡貘",   new[]{"超能力"},       303, 96),
            ("小磁怪",   new[]{"電"},           325, 81),
            // 中階 (floor 4-6)
            ("暴鯉龍",   new[]{"水","飛行"},    540, 130),
            ("拉普拉斯", new[]{"水","冰"},      535, 131),
            ("雷電獸",   new[]{"電"},           525, 135),
            ("寶石海星", new[]{"水","超能力"},  520, 121),
            ("飛天螳螂", new[]{"蟲","飛行"},    500, 123),
            ("鴨嘴火獸", new[]{"火"},           495, 126),
            ("椰蛋樹",   new[]{"草","超能力"},  530, 103),
            ("刺殼菊兒", new[]{"水","冰"},      525, 91),
            ("骨恐龍",   new[]{"地面","岩石"},  490, 95),
            ("多刺球",   new[]{"蟲"},           395, 14),
            // 高階 (floor 7-9)
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
            // Boss (floor 10)
            ("快龍",     new[]{"龍","飛行"},    600, 149),
            ("超夢",     new[]{"超能力"},       680, 150),
            ("班基拉斯", new[]{"岩石","惡"},    600, 248),
            ("烈咬陸鯊", new[]{"龍","地面"},    600, 373),
            ("暴飛龍",   new[]{"龍","飛行"},    600, 445),
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

        // ── 事件池（帶選項） ──────────────────────────────────────
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
                            return $"😨 是毒藥！**{run.ActivePokemon.DisplayName}** 損失 **{dmg}** HP！";
                        }
                        int h = Math.Max(1, run.ActivePokemon.MaxHP / 2);
                        run.ActivePokemon.CurrentHP = Math.Min(run.ActivePokemon.MaxHP, run.ActivePokemon.CurrentHP + h);
                        return $"💚 **{run.ActivePokemon.DisplayName}** 恢復了 **{h} HP**，而你的身材雖然縮小了，頭腦還是原來的名偵探！";
                    }),
                    C("👃 先聞一聞", "👃", run => {
                        int h = Math.Max(1, run.ActivePokemon.MaxHP / 4);
                        run.ActivePokemon.CurrentHP = Math.Min(run.ActivePokemon.MaxHP, run.ActivePokemon.CurrentHP + h);
                        return $"🌿 小心地吃了一點，恢復了 **{h} HP**。";
                    }),
                    C("🚫 不碰它", "🚫", run => "謹慎地繞過，繼續前進，珍愛生命，遠離梯歐歪立。"),
                }),

            new("鼎王麻辣鍋", "⛲",
                "發現了超大鍋的鼎王麻辣鍋，你看著你的寶可夢……",
                new() {
                    C("🏊 整隻泡進去", "🏊", run => {
                        run.ActivePokemon.CurrentHP = run.ActivePokemon.MaxHP;
                        foreach (var m in run.ActivePokemon.Moves) m.CurrentPP = m.MaxPP;
                        return $"✨ 冰水一壺冰水一壺 豆腐鴨血豆腐鴨血就飽啦，**{run.ActivePokemon.DisplayName}** HP 和 PP **完全恢復**！";
                    }),
                    C("💧 餵他吃一份豆腐鴨血", "💧", run => {
                        foreach (var m in run.ActivePokemon.Moves) m.CurrentPP = m.MaxPP;
                        return $"🔋 吃一份豆腐鴨血， **{run.ActivePokemon.DisplayName}** 的所有技能 **PP 完全恢復**！";
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
                        var pool = PickMovesStatic(run.ActivePokemon.Types);
                        var nm = pool.FirstOrDefault(m => run.ActivePokemon.Moves.All(e => e.Name != m.Name)) ?? pool[0];
                        int slot = run.ActivePokemon.Moves.Select((m,i)=>(m,i)).OrderBy(x=>x.m.Power).First().i;
                        string old = run.ActivePokemon.Moves[slot].Name;
                        run.ActivePokemon.Moves[slot] = nm;
                        return $"📀 手錶黏到你手上了，完全拿不掉！ 但你的寶可夢忘掉 **{old}**，學會了 {nm.Emoji}**{nm.Name}**！";
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
                        run.ActivePokemon.CurrentHP = run.ActivePokemon.MaxHP;
                        foreach (var m in run.ActivePokemon.Moves) m.CurrentPP = m.MaxPP;
                        return $"🌟 **{run.ActivePokemon.DisplayName}** HP 和 PP **完全恢復**！";
                    }),
                    C("💰 求賜財富", "💰", run => {
                        int g = _rng.Next(30, 70); run.Gold += g;
                        return $"💰 精靈賜予了 **{g} 金幣**！";
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
                            return $"🍄 在林中找到了珍稀藥草，換了 **{g} 金幣**！";
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
                        run.ActivePokemon.CurrentHP = Math.Min(run.ActivePokemon.MaxHP, run.ActivePokemon.CurrentHP + h);
                        foreach (var m in run.ActivePokemon.Moves) m.CurrentPP = m.MaxPP;
                        return $"💤 休息了一會兒，恢復了 **{h} HP** 和 **全部 PP**，再出發！";
                    }),
                }),

            new("現代最強", "🧙",
                "一位帶著眼罩的白髮揍速師，似乎想傳授些什麼。",
                new() {
                    C("📚 請求對練", "📚", run => {
                        int g = _rng.Next(25, 55); run.Gold += g;
                        return $"🧠 你說想跟他實際戰鬥，結果不到10秒他就被腰斬了，從現代最強的半身口袋中獲益，收到 **{g} 金幣**。";
                    }),
                    C("💊 學習反轉術士", "💊", run => {
                        int h = Math.Max(1, run.ActivePokemon.MaxHP / 2);
                        run.ActivePokemon.CurrentHP = Math.Min(run.ActivePokemon.MaxHP, run.ActivePokemon.CurrentHP + h);
                        foreach (var m in run.ActivePokemon.Moves) m.CurrentPP = m.MaxPP;
                        return $"🌿 現代最強飄在空中，喊著甚麼對不起天內，我現在並不是為你生氣... 你聽不懂他在講什麼，但 **{run.ActivePokemon.DisplayName}** 睡著了，恢復 **{h} HP** + PP 全回！";
                    }),
                    C("📀 學習領域展開", "📀", run => {
                        var pool = PickMovesStatic(run.ActivePokemon.Types);
                        var nm = pool.FirstOrDefault(m => run.ActivePokemon.Moves.All(e => e.Name != m.Name)) ?? pool[0];
                        int slot = run.ActivePokemon.Moves.Select((m,i)=>(m,i)).OrderBy(x=>x.m.Power).First().i;
                        string old = run.ActivePokemon.Moves[slot].Name;
                        run.ActivePokemon.Moves[slot] = nm;
                        return $"📀 現代最強開出領域，**{run.ActivePokemon.DisplayName}** 大腦直接當機，但醒過來後學會了新招，{nm.Emoji}**{nm.Name}**，取代了 **{old}**！";
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
                        var pool = PickMovesStatic(run.ActivePokemon.Types);
                        var nm = pool.FirstOrDefault(m => run.ActivePokemon.Moves.All(e => e.Name != m.Name)) ?? pool[0];
                        int slot = run.ActivePokemon.Moves.Select((m,i)=>(m,i)).OrderBy(x=>x.m.Power).First().i;
                        string old = run.ActivePokemon.Moves[slot].Name;
                        run.ActivePokemon.Moves[slot] = nm;
                        return $"📀 忘掉了 **{old}**，學會了 {nm.Emoji}**{nm.Name}**！";
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
                        int h = Math.Max(1, run.ActivePokemon.MaxHP / 5);
                        run.ActivePokemon.CurrentHP = Math.Min(run.ActivePokemon.MaxHP, run.ActivePokemon.CurrentHP + h);
                        foreach (var m in run.ActivePokemon.Moves) m.CurrentPP = m.MaxPP;
                        return $"💤 你陪他玩了一下，但真的太無聊你不小心睡著了，寶可夢成功回血，恢復了 **{h} HP** 和 **全部 PP**！";
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
                        if (_aiService == null) return "（Soyo 你怎麼又似了啦……）";
                        string prompt = $"你是一個名叫Soyo的可愛Discord機器人助手，個性活潑親切，說話帶有一點點傲嬌。有人在神秘塔的路上遇到了正在推銷你的程式男，並走上來搭話。請用繁體中文，以Soyo的口吻，隨機說一句有趣的自我介紹或推銷詞，不超過60字。";
                        string reply = await _aiService.GenerateSimpleTextAsync(prompt);
                        return $"🤖 Soyo 機器人：「{reply.Trim()}」";
                    }),
                    new("🎮 試用看看", "🎮", async run => {
                        if (_aiService == null) return "（系統錯誤：找不到 Soyo……）";
                        string prompt = $"你是Soyo，一個可愛的Discord機器人。有人想試用你，請用繁體中文給出一個簡短的寶可夢爬塔小技巧或鼓勵的話，不超過50字，口氣要俏皮可愛。";
                        string tip = await _aiService.GenerateSimpleTextAsync(prompt);
                        return $"💡 Soyo 給的小提示：「{tip.Trim()}」";
                    }),
                    C("🚶 裝沒看見，繼續趕路", "🚶", run => "你假裝沒看到，快步離開，背後傳來「欸欸欸你要不要試試看～」的聲音……"),
                }),
        };

        private static OpenRouterService _aiService;

        // ── Constructor ────────────────────────────────────────
        public PokeTowerService(string redisConnectionString = null, OpenRouterService aiService = null)
        {
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
            _ = LoadRunsAsync();
        }

        // ── Public API ─────────────────────────────────────────

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
                    "共 **10 層**，第 10 層是 Boss，加油！")
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
            ulong channelId, ulong playerId, string playerName, PokeGamePokemon src)
        {
            var pokemon = ConvertPokemon(src);
            pokemon.Moves = PickMoves(src.Types);

            var run = new TowerRun
            {
                PlayerId = playerId,
                PlayerName = playerName,
                ChannelId = channelId,
                ActivePokemon = pokemon,
                State = TowerRunState.SelectingPath,
            };
            run.Party.Add(pokemon);
            run.RunLog.Add($"🏔️ {playerName} 帶著 {pokemon.DisplayName} 踏入爬塔！");

            _activeRuns[channelId] = run;
            await SaveAsync(run);
            return BuildPathEmbed(run);
        }

        /// <summary>選擇路徑</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandlePathChoiceAsync(
            ulong channelId, string choice)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            run.CurrentFloor++;
            bool isBoss = run.CurrentFloor == run.MaxFloor;

            if (choice == "battle" || isBoss)
            {
                run.CurrentEnemy = GenEnemy(run.CurrentFloor, isBoss);
                run.CurrentBattleLog = "";
                run.State = TowerRunState.InBattle;
                run.RunLog.Add($"⚔️ 第{run.CurrentFloor}層：遭遇 {run.CurrentEnemy.Name}！");
                await SaveAsync(run);
                return BuildBattleEmbed(run);
            }
            if (choice == "rest")
            {
                int hp = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.35));
                run.ActivePokemon.CurrentHP = Math.Min(run.ActivePokemon.MaxHP, run.ActivePokemon.CurrentHP + hp);
                foreach (var m in run.ActivePokemon.Moves) m.CurrentPP = m.MaxPP;
                run.RunLog.Add($"🏕️ 第{run.CurrentFloor}層：休息恢復 {hp}HP + PP全回復");
                await SaveAsync(run);
                return BuildPathEmbed(run, $"🏕️ **{run.ActivePokemon.DisplayName}** 休息後恢復 **{hp} HP**，技能 PP 也全部恢復！");
            }
            if (choice == "shop")
            {
                run.State = TowerRunState.Shopping;
                await SaveAsync(run);
                return BuildShopEmbed(run);
            }
            if (choice == "event")
            {
                run.PendingEventIdx = _rng.Next(_events.Count);
                run.State = TowerRunState.SelectingEvent;
                await SaveAsync(run);
                return BuildEventEmbed(run);
            }
            return ErrEmbed("未知的路徑選擇");
        }

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

            // 掙扎機制：所有PP歸零時
            TowerMove playerMove;
            if (moveIdx >= 0 && moveIdx < poke.Moves.Count && poke.Moves[moveIdx].CurrentPP > 0)
            {
                playerMove = poke.Moves[moveIdx];
                playerMove.CurrentPP--;
            }
            else
            {
                playerMove = new TowerMove { Name="掙扎", Type="一般", Power=50, Category="Physical", Emoji="😤", MaxPP=1, CurrentPP=1 };
            }
            var enemyMove = enemy.Moves[enemy.NextMoveIdx % enemy.Moves.Count];

            bool playerFirst = poke.Speed >= enemy.Speed;
            // 計算目前第幾回合（現有 rounds 數 + 1）
            int roundNum = run.CurrentBattleLog.Split(new[] { "════════" }, StringSplitOptions.RemoveEmptyEntries)
                               .Count(r => r.Trim().Length > 0) + 1;
            var sb = new StringBuilder();
            sb.AppendLine($"【回合 {roundNum}】");

            if (playerFirst)
            {
                int d = CalcDamage(playerMove, poke.Attack, poke.SpecialAttack, enemy.Defense, enemy.SpecialDefense, enemy.Types);
                enemy.CurrentHP = Math.Max(0, enemy.CurrentHP - d);
                run.TotalDamageDealt += d;
                AppendHit(sb, poke.DisplayName, enemy.Name, playerMove, d, enemy.Types, true);
                if (enemy.CurrentHP > 0)
                {
                    int ed = CalcDamage(enemyMove, enemy.Attack, enemy.SpecialAttack, poke.Defense, poke.SpecialDefense, poke.Types);
                    poke.CurrentHP = Math.Max(0, poke.CurrentHP - ed);
                    AppendHit(sb, enemy.Name, poke.DisplayName, enemyMove, ed, poke.Types, false);
                }
            }
            else
            {
                int ed = CalcDamage(enemyMove, enemy.Attack, enemy.SpecialAttack, poke.Defense, poke.SpecialDefense, poke.Types);
                poke.CurrentHP = Math.Max(0, poke.CurrentHP - ed);
                AppendHit(sb, enemy.Name, poke.DisplayName, enemyMove, ed, poke.Types, false);
                if (poke.CurrentHP > 0)
                {
                    int d = CalcDamage(playerMove, poke.Attack, poke.SpecialAttack, enemy.Defense, enemy.SpecialDefense, enemy.Types);
                    enemy.CurrentHP = Math.Max(0, enemy.CurrentHP - d);
                    run.TotalDamageDealt += d;
                    AppendHit(sb, poke.DisplayName, enemy.Name, playerMove, d, enemy.Types, true);
                }
            }

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
                run.Gold += enemy.GoldReward;
                run.RunLog.Add($"✅ 第{run.CurrentFloor}層：擊倒 {enemy.Name}，獲得 {enemy.GoldReward}💰");

                if (run.CurrentFloor >= run.MaxFloor)
                {
                    run.State = TowerRunState.Victory;
                    await RemoveAsync(channelId);
                    return BuildVictoryEmbed(run);
                }

                // 提供技能獎勵
                run.PendingMoveRewards = _movePool.OrderBy(_ => _rng.Next()).Take(3).ToList();
                // 可以捕獲
                run.PendingCatch = enemy;
                run.State = TowerRunState.SelectingMoveReward;
                await SaveAsync(run);
                return BuildMoveRewardEmbed(run);
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

        /// <summary>選擇技能獎勵（0-2=選技能, 3=跳過）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleMoveRewardAsync(
            ulong channelId, int idx)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            if (idx == 3 || idx >= run.PendingMoveRewards.Count)
            {
                // Skip → go to catch or path
                run.PendingMoveRewards.Clear();
                run.PendingSelectedMove = null;
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

            if (slot < run.ActivePokemon.Moves.Count)
            {
                string old = run.ActivePokemon.Moves[slot].Name;
                var nm = run.PendingSelectedMove;
                nm.CurrentPP = nm.MaxPP;
                run.ActivePokemon.Moves[slot] = nm;
                run.RunLog.Add($"📀 換掉【{old}】，學會了【{nm.Name}】");
            }

            run.PendingSelectedMove = null;
            run.PendingMoveRewards.Clear();
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

            bool caught = (float)_rng.NextDouble() < ballInfo.Rate;
            if (caught)
            {
                var newPoke = CatchFromEnemy(run.PendingCatch);
                if (run.Party.Count < 3)
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
                return BuildCatchEmbed(run, $"{ballInfo.Emoji} 投出 **{ballInfo.DisplayName}**……逃脫了！（剩餘：{ballsLeft}）");
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
            run.PendingEventIdx = -1;
            run.State = TowerRunState.SelectingPath;
            await SaveAsync(run);
            return BuildPathEmbed(run,
                $"{ev.Emoji} **【{ev.Title}】**\n> {choice.Emoji} {choice.Label}\n\n{result}");
        }

        /// <summary>商店購買</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleShopItemAsync(
            ulong channelId, string itemKey)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            string msg;
            switch (itemKey)
            {
                case "heal_full":
                    if (run.Gold < 30) return BuildShopEmbed(run, "💸 金幣不足！需要 30 金幣。");
                    run.Gold -= 30;
                    run.ActivePokemon.CurrentHP = run.ActivePokemon.MaxHP;
                    msg = "💊 使用「全回復」— HP 完全恢復！（-30💰）";
                    break;
                case "heal_half":
                    if (run.Gold < 15) return BuildShopEmbed(run, "💸 金幣不足！需要 15 金幣。");
                    run.Gold -= 15;
                    int h = Math.Max(1, run.ActivePokemon.MaxHP / 2);
                    run.ActivePokemon.CurrentHP = Math.Min(run.ActivePokemon.MaxHP, run.ActivePokemon.CurrentHP + h);
                    msg = $"🧃 使用「超級樹果」— 恢復 {h} HP！（-15💰）";
                    break;
                case "pp_restore":
                    if (run.Gold < 20) return BuildShopEmbed(run, "💸 金幣不足！需要 20 金幣。");
                    run.Gold -= 20;
                    foreach (var m in run.ActivePokemon.Moves) m.CurrentPP = m.MaxPP;
                    msg = "🔋 所有技能 PP 完全恢復！（-20💰）";
                    break;
                case "new_move":
                    if (run.Gold < 25) return BuildShopEmbed(run, "💸 金幣不足！需要 25 金幣。");
                    run.Gold -= 25;
                    var pool = PickMoves(run.ActivePokemon.Types);
                    var nm = pool.FirstOrDefault(m => run.ActivePokemon.Moves.All(em => em.Name != m.Name)) ?? pool[0];
                    int slot = run.ActivePokemon.Moves.Select((m, i) => (m, i)).OrderBy(x => x.m.Power).First().i;
                    string old = run.ActivePokemon.Moves[slot].Name;
                    run.ActivePokemon.Moves[slot] = nm;
                    msg = $"📀 忘掉【{old}】，學會了 {nm.Emoji}**{nm.Name}**！（-25💰）";
                    break;
                case "buy_normal":
                    if (run.Gold < 8) return BuildShopEmbed(run, "💸 金幣不足！需要 8 金幣。");
                    run.Gold -= 8;
                    run.Balls["normal"] = run.Balls.GetValueOrDefault("normal") + 3;
                    msg = "⚽ 購入 **普通球×3**！（-8💰）";
                    break;
                case "buy_super":
                    if (run.Gold < 15) return BuildShopEmbed(run, "💸 金幣不足！需要 15 金幣。");
                    run.Gold -= 15;
                    run.Balls["super"] = run.Balls.GetValueOrDefault("super") + 2;
                    msg = "🔵 購入 **超級球×2**！（-15💰）";
                    break;
                case "buy_ultra":
                    if (run.Gold < 25) return BuildShopEmbed(run, "💸 金幣不足！需要 25 金幣。");
                    run.Gold -= 25;
                    run.Balls["ultra"] = run.Balls.GetValueOrDefault("ultra") + 1;
                    msg = "🟡 購入 **高級球×1**！（-25💰）";
                    break;
                case "leave":
                    msg = "👋 離開商店，繼續爬塔！";
                    break;
                default:
                    return ErrEmbed("未知的道具");
            }

            run.State = TowerRunState.SelectingPath;
            run.RunLog.Add(msg);
            await SaveAsync(run);
            return BuildPathEmbed(run, msg);
        }

        /// <summary>顯示換寶可夢畫面</summary>
        public (Embed embed, ComponentBuilder component) ShowSwapSelection(
            ulong channelId, List<PokeGamePokemon> allPlayerPokemons)
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

            // Also allow adding new Pokemon from player collection that aren't in party yet
            var newOnes = allPlayerPokemons
                .Where(p => run.Party.All(tp => !(tp.PokeId == p.Id && tp.CaughtAt == p.CaughtDate)))
                .Take(3).ToList();

            if (newOnes.Count > 0 && run.Party.Count < 3)
            {
                embed.AddField("── 可加入的新成員 ──", "（加入時 HP 為最大值）", inline: false);
                for (int i = 0; i < newOnes.Count && run.Party.Count + i < 3; i++)
                {
                    var p = newOnes[i];
                    embed.AddField(p.CustomName ?? p.Name, $"HP {p.HP}", inline: true);
                    cb.WithButton($"加入 {p.CustomName ?? p.Name}", $"tower_addnew_{channelId}_{i}", ButtonStyle.Success, row: btnRow++);
                }
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
            newPoke.Moves = PickMoves(src.Types);
            run.Party.Add(newPoke);
            run.RunLog.Add($"➕ {newPoke.DisplayName} 加入了爬塔！");
            await SaveAsync(run);
            return BuildCurrentStateEmbed(channelId);
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

        // ── Private helpers ───────────────────────────────────

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
            float scale = isBoss ? 1.5f : 1.0f + (floor - 1) * 0.07f;
            int b = Math.Max(30, (int)(t.StatTotal * scale / 6));
            int gold = isBoss ? floor * 15 : floor * 5 + _rng.Next(5);

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
            int raw = (int)(move.Power * a / (float)Math.Max(1, d) * eff / 7.5f);
            return Math.Max(1, (int)(raw * (0.85 + _rng.NextDouble() * 0.15)));
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
            if (floor == 10) return new() { "battle" };
            var pool = new List<string> { "rest", "shop", "event" };
            pool = pool.OrderBy(_ => _rng.Next()).Take(2).ToList();
            pool.Add("battle");
            return pool.OrderBy(_ => _rng.Next()).ToList();
        }

        private (string Label, ButtonStyle Style, string Emoji) PathDisplay(string choice) => choice switch
        {
            "battle" => ("⚔️ 戰鬥", ButtonStyle.Danger, "⚔️"),
            "rest"   => ("🏕️ 休息 +35%HP+PP", ButtonStyle.Success, "🏕️"),
            "shop"   => ("🏪 神秘商店", ButtonStyle.Secondary, "🏪"),
            "event"  => ("❓ 神秘事件", ButtonStyle.Primary, "❓"),
            _        => ("?", ButtonStyle.Secondary, "?"),
        };

        // ── Embed builders ────────────────────────────────────

        private (Embed embed, ComponentBuilder component) BuildPathEmbed(TowerRun run, string extra = "")
        {
            bool nextIsBoss = (run.CurrentFloor + 1) == run.MaxFloor;
            var p = run.ActivePokemon;
            var paths = GenPaths(run.CurrentFloor + 1);

            var desc = new StringBuilder();
            if (!string.IsNullOrEmpty(extra)) desc.AppendLine(extra).AppendLine();
            desc.AppendLine($"**{p.DisplayName}** {TypeBadge(p.Types)}");
            desc.AppendLine($"HP: {HpBar(p.CurrentHP, p.MaxHP)}");
            desc.AppendLine($"技能: {MovesDisplay(p)}");
            desc.AppendLine($"💰 金幣: **{run.Gold}**");
            if (run.Party.Count > 1)
                desc.AppendLine($"🎒 背包: {string.Join("、", run.Party.Select(pk => $"{pk.DisplayName}({pk.CurrentHP}HP)"))}");
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
            desc.AppendLine($"**你的 {p.DisplayName}** {TypeBadge(p.Types)}");
            desc.AppendLine($"HP: {HpBar(p.CurrentHP, p.MaxHP)}");
            desc.AppendLine($"技能: {MovesDisplay(p)}");
            desc.AppendLine();
            desc.AppendLine($"**{(e.IsBoss ? "👑" : "🎯")} {e.Name}** {TypeBadge(e.Types)}");
            desc.AppendLine($"HP: {HpBar(e.CurrentHP, e.MaxHP)}");
            desc.AppendLine();
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

        private (Embed embed, ComponentBuilder component) BuildShopEmbed(TowerRun run, string notice = "")
        {
            var p = run.ActivePokemon;
            var desc = new StringBuilder();
            if (!string.IsNullOrEmpty(notice)) desc.AppendLine($"⚠️ {notice}").AppendLine();
            desc.AppendLine($"**{p.DisplayName}** HP: {HpBar(p.CurrentHP, p.MaxHP)}");
            desc.AppendLine($"技能: {MovesDisplay(p)}");
            desc.AppendLine($"💰 金幣: **{run.Gold}**");
            desc.AppendLine();
            desc.AppendLine("**商品：**");
            desc.AppendLine("💊 **全回復** — HP完全恢復 (30💰)");
            desc.AppendLine("🧃 **超級樹果** — 恢復50% HP (15💰)");
            desc.AppendLine("🔋 **PP全回復** — 所有技能PP滿 (20💰)");
            desc.AppendLine("📀 **技能學習器** — 隨機換一個技能 (25💰)");
            desc.AppendLine("⚽ **普通球×3** — 30%捕獲率 (8💰)");
            desc.AppendLine("🔵 **超級球×2** — 55%捕獲率 (15💰)");
            desc.AppendLine("🟡 **高級球×1** — 75%捕獲率 (25💰)");
            desc.AppendLine($"\n現有球：{BallsDisplay(run)}");

            var cb = new ComponentBuilder()
                .WithButton("💊 全回復(30💰)",    $"tower_shop_{run.ChannelId}_heal_full",  ButtonStyle.Success,   row: 0)
                .WithButton("🧃 超級樹果(15💰)",  $"tower_shop_{run.ChannelId}_heal_half",  ButtonStyle.Primary,   row: 0)
                .WithButton("🔋 PP全回復(20💰)",  $"tower_shop_{run.ChannelId}_pp_restore", ButtonStyle.Primary,   row: 1)
                .WithButton("📀 技能學習器(25💰)",$"tower_shop_{run.ChannelId}_new_move",   ButtonStyle.Secondary, row: 1)
                .WithButton("⚽ 普通球×3(8💰)",   $"tower_shop_{run.ChannelId}_buy_normal", ButtonStyle.Secondary, row: 2)
                .WithButton("🔵 超級球×2(15💰)",  $"tower_shop_{run.ChannelId}_buy_super",  ButtonStyle.Primary,   row: 2)
                .WithButton("🟡 高級球×1(25💰)",  $"tower_shop_{run.ChannelId}_buy_ultra",  ButtonStyle.Primary,   row: 2)
                .WithButton("離開商店", $"tower_shop_{run.ChannelId}_leave", ButtonStyle.Danger, row: 3);

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
            desc.AppendLine();
            desc.AppendLine("**請選擇應對方式：**");
            for (int i = 0; i < ev.Choices.Count; i++)
                desc.AppendLine($"{ev.Choices[i].Emoji} **{ev.Choices[i].Label}**");
            desc.AppendLine();
            desc.AppendLine($"**{run.ActivePokemon.DisplayName}** HP: {HpBar(run.ActivePokemon.CurrentHP, run.ActivePokemon.MaxHP, 6)}　💰 {run.Gold}");

            var cb = new ComponentBuilder();
            for (int i = 0; i < ev.Choices.Count; i++)
                cb.WithButton($"{ev.Choices[i].Emoji} {ev.Choices[i].Label}",
                    $"tower_event_{run.ChannelId}_{i}",
                    ButtonStyle.Primary, row: i / 3);

            return (new EmbedBuilder()
                .WithTitle($"{ev.Emoji} 神秘事件：{ev.Title}")
                .WithDescription(desc.ToString())
                .WithColor(new Color(148, 0, 211)).Build(), cb);
        }

        private (Embed embed, ComponentBuilder component) BuildMoveRewardEmbed(TowerRun run)
        {
            var desc = new StringBuilder();
            desc.AppendLine($"🎉 擊倒 **{run.CurrentEnemy?.Name ?? "敵人"}**，獲得 **{run.CurrentEnemy?.GoldReward ?? 0} 💰**！");
            desc.AppendLine();
            desc.AppendLine("✨ **戰鬥獎勵 — 選擇一個技能學習（可跳過）：**");
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
                    $"• 用時：{elapsed} 分鐘")
                .WithColor(Color.Gold).Build(), new ComponentBuilder());
        }

        private (Embed embed, ComponentBuilder component) BuildDefeatEmbed(TowerRun run)
        {
            var elapsed = (int)(DateTime.UtcNow - run.StartedAt).TotalMinutes;
            return (new EmbedBuilder()
                .WithTitle("💀 全滅...")
                .WithDescription(
                    $"**{run.PlayerName}** 的全部寶可夢在第 **{run.CurrentFloor}** 層倒下。\n\n" +
                    $"📊 **成績**\n" +
                    $"• 攻克：{run.CurrentFloor - 1}/{run.MaxFloor} 層\n" +
                    $"• 累積傷害：{run.TotalDamageDealt}\n" +
                    $"• 用時：{elapsed} 分鐘\n\n" +
                    "下次再挑戰！")
                .WithColor(Color.DarkRed).Build(), new ComponentBuilder());
        }

        private (Embed embed, ComponentBuilder component) ErrEmbed(string msg) =>
            (new EmbedBuilder().WithTitle("❌ 錯誤").WithDescription(msg).WithColor(Color.Red).Build(),
             new ComponentBuilder());

        // ── Persistence ───────────────────────────────────────

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
    }
}
