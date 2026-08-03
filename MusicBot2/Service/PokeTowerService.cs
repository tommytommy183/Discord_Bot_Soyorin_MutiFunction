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
        SelectingMoveReward,
        SelectingMoveSlot,
        SelectingCatch,
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

        [JsonIgnore]
        public string DisplayName => !string.IsNullOrWhiteSpace(CustomName) ? CustomName : Name;
    }

    public class TowerEnemy
    {
        public string Name { get; set; }
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
    }

    public class PokeTowerService
    {
        private readonly IDatabase _redisDb;
        private readonly bool _useRedis;
        private readonly Dictionary<ulong, TowerRun> _activeRuns = new();
        private const string REDIS_PREFIX = "tower:run:";
        private static readonly Random _rng = new();

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

        // ── 中文敵人池 ─────────────────────────────────────────
        private static readonly List<(string Name, string[] Types, int StatTotal)> _enemyPool = new()
        {
            // 低階 (floor 1-3)
            ("比雕",     new[]{"一般","飛行"}, 349),
            ("隆隆石",   new[]{"岩石","地面"}, 390),
            ("鬼斯通",   new[]{"幽靈","毒"},   405),
            ("卡咪龜",   new[]{"水"},           405),
            ("火恐龍",   new[]{"火"},           405),
            ("妙蛙草",   new[]{"草","毒"},      405),
            ("電擊獸",   new[]{"電"},           490),
            ("皮卡丘",   new[]{"電"},           320),
            ("瞌睡貘",   new[]{"超能力"},       303),
            ("小磁怪",   new[]{"電"},           325),
            // 中階 (floor 4-6)
            ("暴鯉龍",   new[]{"水","飛行"},    540),
            ("拉普拉斯", new[]{"水","冰"},      535),
            ("雷電獸",   new[]{"電"},           525),
            ("寶石海星", new[]{"水","超能力"},  520),
            ("飛天螳螂", new[]{"蟲","飛行"},    500),
            ("鴨嘴火獸", new[]{"火"},           495),
            ("椰蛋樹",   new[]{"草","超能力"},  530),
            ("刺殼菊兒", new[]{"水","冰"},      525),
            ("強行固執", new[]{"岩石"},         490),
            ("多刺球",   new[]{"蟲"},           395),
            // 高階 (floor 7-9)
            ("怪力",     new[]{"格鬥"},         505),
            ("耿鬼",     new[]{"幽靈","毒"},    500),
            ("胡地",     new[]{"超能力"},       500),
            ("風速狗",   new[]{"火"},           555),
            ("尼多王",   new[]{"毒","地面"},    505),
            ("哈克龍",   new[]{"龍"},           420),
            ("化石翼龍", new[]{"岩石","飛行"},  515),
            ("袋獸",     new[]{"一般"},         490),
            ("水箭龜",   new[]{"水"},           530),
            ("噴火龍",   new[]{"火","飛行"},    534),
            // Boss (floor 10)
            ("快龍",     new[]{"龍","飛行"},    600),
            ("超夢",     new[]{"超能力"},       680),
            ("班基拉斯", new[]{"岩石","惡"},    600),
            ("烈咬陸鯊", new[]{"龍","地面"},    600),
            ("暴飛龍",   new[]{"龍","飛行"},    600),
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

        // ── 事件池 ────────────────────────────────────────────
        private static readonly List<(string Title, string Emoji, string Desc, Func<TowerRun, string> Apply)> _events = new()
        {
            ("神秘寶箱", "🎁", "面前有一個閃閃發光的寶箱……",
                run => { int g = _rng.Next(20, 55); run.Gold += g; return $"箱子裡有 **{g} 金幣**！"; }),

            ("迷路訓練師", "👟", "遇到一個迷路的訓練師，他感謝你指路……",
                run => { int g = _rng.Next(10, 35); run.Gold += g; return $"訓練師送了你 **{g} 金幣** 表示感謝！"; }),

            ("神秘藥水", "💊", "地上有一個神秘的藥水……",
                run => {
                    int h = Math.Max(1, run.ActivePokemon.MaxHP / 3);
                    run.ActivePokemon.CurrentHP = Math.Min(run.ActivePokemon.MaxHP, run.ActivePokemon.CurrentHP + h);
                    return $"**{run.ActivePokemon.DisplayName}** 恢復了 **{h} HP**！";
                }),

            ("能量泉", "⛲", "發現了一個神奇的能量泉……",
                run => {
                    foreach (var m in run.ActivePokemon.Moves) m.CurrentPP = m.MaxPP;
                    return $"**{run.ActivePokemon.DisplayName}** 的所有技能 **PP 完全恢復**！";
                }),

            ("遺落的技能機", "📀", "地上有一個廢棄的技能機……",
                run => {
                    var pool = PickMovesStatic(run.ActivePokemon.Types);
                    var nm = pool.FirstOrDefault(m => run.ActivePokemon.Moves.All(e => e.Name != m.Name)) ?? pool[0];
                    int slot = run.ActivePokemon.Moves.Select((m,i)=>(m,i)).OrderBy(x=>x.m.Power).First().i;
                    string old = run.ActivePokemon.Moves[slot].Name;
                    run.ActivePokemon.Moves[slot] = nm;
                    return $"忘掉了 **{old}**，學會了 {nm.Emoji} **{nm.Name}**！";
                }),

            ("毒刺陷阱", "🕳️", "踩到了隱藏的毒刺陷阱！",
                run => {
                    int dmg = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.15));
                    run.ActivePokemon.CurrentHP = Math.Max(1, run.ActivePokemon.CurrentHP - dmg);
                    return $"**{run.ActivePokemon.DisplayName}** 受到 **{dmg}** 點傷害！（HP不會歸零）";
                }),

            ("訓練師被搶劫", "😈", "遇到了一個壞訓練師，搶走了你的金幣……",
                run => {
                    int lost = Math.Min(run.Gold, _rng.Next(10, 30));
                    run.Gold -= lost;
                    return $"失去了 **{lost} 金幣**！（剩餘 {run.Gold} 金幣）";
                }),

            ("精靈的祝福", "🌟", "遇到了傳說中的精靈，獲得了祝福……",
                run => {
                    run.ActivePokemon.CurrentHP = run.ActivePokemon.MaxHP;
                    foreach (var m in run.ActivePokemon.Moves) m.CurrentPP = m.MaxPP;
                    return $"**{run.ActivePokemon.DisplayName}** HP 和 PP **完全恢復**！";
                }),

            ("迷失樹林", "🌲", "迷失在黑暗的樹林中，浪費了時間……",
                run => "走了很長一段路，什麼也沒發生……（浪費一層）"),

            ("強化石", "💎", "找到了神奇的強化石，暫時強化了寶可夢……",
                run => {
                    int g = _rng.Next(15, 40);
                    run.Gold += g;
                    return $"強化石化為 **{g} 金幣** 存入口袋！";
                }),

            ("老修行者", "🧙", "遇到了一位老修行者，他傳授了珍貴的知識……",
                run => {
                    int g = _rng.Next(25, 60);
                    run.Gold += g;
                    return $"老修行者給了你 **{g} 金幣** 作為旅費！";
                }),
        };

        // ── Constructor ────────────────────────────────────────
        public PokeTowerService(string redisConnectionString = null)
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
                var ev = _events[_rng.Next(_events.Count)];
                string result = ev.Apply(run);
                run.RunLog.Add($"{ev.Emoji} 第{run.CurrentFloor}層【{ev.Title}】");
                await SaveAsync(run);
                return BuildPathEmbed(run,
                    $"{ev.Emoji} **【{ev.Title}】**\n_{ev.Desc}_\n\n{result}");
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
            var sb = new StringBuilder();
            sb.AppendLine($"─────────────────────────");
            sb.AppendLine($"**【回合 {run.CurrentBattleLog.Count(c => c == '─') / 26 + 1}】**");

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

            // Accumulate log, keep last ~1800 chars
            run.CurrentBattleLog += sb.ToString();
            if (run.CurrentBattleLog.Length > 1800)
                run.CurrentBattleLog = run.CurrentBattleLog[^1800..];

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

        /// <summary>選擇是否捕獲（yes/no）</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleCatchAsync(
            ulong channelId, bool doCatch)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            string msg = "";
            if (doCatch && run.PendingCatch != null)
            {
                if (run.Party.Count >= 3)
                {
                    msg = "⚠️ 背包已滿（最多3隻），無法捕獲！";
                }
                else
                {
                    var newPoke = CatchFromEnemy(run.PendingCatch);
                    run.Party.Add(newPoke);
                    run.RunLog.Add($"🎉 捕獲了 {newPoke.Name}！");
                    msg = $"🎉 成功捕獲 **{newPoke.Name}**！（HP: {newPoke.CurrentHP}/{newPoke.MaxHP}）";
                }
            }
            else
            {
                msg = $"放走了 {run.PendingCatch?.Name ?? "敵人"}。";
            }

            run.PendingCatch = null;
            run.State = TowerRunState.SelectingPath;
            run.CurrentEnemy = null;
            await SaveAsync(run);
            return BuildPathEmbed(run, msg);
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
                case "catch_ball":
                    if (run.Gold < 20) return BuildShopEmbed(run, "💸 金幣不足！需要 20 金幣。");
                    if (run.Party.Count >= 3) return BuildShopEmbed(run, "⚠️ 背包已滿（最多3隻）！");
                    run.Gold -= 20;
                    var wildPoke = CatchFromEnemy(GenEnemy(run.CurrentFloor, false));
                    run.Party.Add(wildPoke);
                    msg = $"🎾 捕獲了野生的 **{wildPoke.Name}**！（HP: {wildPoke.CurrentHP}/{wildPoke.MaxHP}，-20💰）";
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
                TowerRunState.SelectingMoveReward => BuildMoveRewardEmbed(run),
                TowerRunState.SelectingMoveSlot => BuildMoveSlotEmbed(run),
                TowerRunState.SelectingCatch => BuildCatchEmbed(run),
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
        };

        private TowerPokemon CatchFromEnemy(TowerEnemy e) => new()
        {
            PokeId = -(Math.Abs(e.Name.GetHashCode()) % 10000),
            Name = e.Name.Replace("👑 ", ""),
            Types = e.Types?.ToList() ?? new(),
            MaxHP = e.MaxHP,
            CurrentHP = Math.Max(1, e.MaxHP / 3),
            Attack = e.Attack,
            Defense = e.Defense,
            SpecialAttack = e.SpecialAttack,
            SpecialDefense = e.SpecialDefense,
            Speed = e.Speed,
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
            IEnumerable<(string Name, string[] Types, int StatTotal)> tier;
            if (isBoss)     tier = _enemyPool.Where(e => e.StatTotal >= 590);
            else if (floor <= 3) tier = _enemyPool.Where(e => e.StatTotal < 430);
            else if (floor <= 6) tier = _enemyPool.Where(e => e.StatTotal >= 430 && e.StatTotal < 545);
            else            tier = _enemyPool.Where(e => e.StatTotal >= 480 && e.StatTotal < 590);

            var choices = tier.ToList();
            if (choices.Count == 0) choices = _enemyPool;
            var t = choices[_rng.Next(choices.Count)];
            float scale = isBoss ? 1.5f : 1.0f + (floor - 1) * 0.07f;
            int b = Math.Max(30, (int)(t.StatTotal * scale / 6));
            int gold = isBoss ? floor * 15 : floor * 5 + _rng.Next(5);

            return new TowerEnemy
            {
                Name = isBoss ? $"👑 {t.Name}" : t.Name,
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
            string effNote = eff switch { >= 2f => "**（超級效果！）**", 0f => "**（完全無效！）**", < 1f => "（效果不佳）", _ => "" };
            string tag = isPlayer ? "🗡️" : "💢";
            sb.AppendLine($"{tag} **{atkName}** → {move.Emoji}**{move.Name}** {effNote}");
            sb.AppendLine($"　　對 **{defName}** 造成 **{dmg}** 傷害");
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
                desc.Append(run.CurrentBattleLog.Replace("**", "").Replace("`", "").Replace("🗡️", "▶").Replace("💢", "◀"));
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
            if (run.Party.Count < 3)
                desc.AppendLine("🎾 **精靈球** — 捕獲一隻當層的野生精靈 (20💰)");

            var cb = new ComponentBuilder()
                .WithButton("💊 全回復(30💰)",    $"tower_shop_{run.ChannelId}_heal_full", ButtonStyle.Success, row: 0)
                .WithButton("🧃 超級樹果(15💰)",  $"tower_shop_{run.ChannelId}_heal_half", ButtonStyle.Primary, row: 0)
                .WithButton("🔋 PP全回復(20💰)",  $"tower_shop_{run.ChannelId}_pp_restore", ButtonStyle.Primary, row: 1)
                .WithButton("📀 技能學習器(25💰)",$"tower_shop_{run.ChannelId}_new_move",  ButtonStyle.Secondary, row: 1);
            if (run.Party.Count < 3)
                cb.WithButton("🎾 精靈球(20💰)", $"tower_shop_{run.ChannelId}_catch_ball", ButtonStyle.Secondary, row: 2);
            cb.WithButton("離開商店", $"tower_shop_{run.ChannelId}_leave", ButtonStyle.Danger, row: 2);

            return (new EmbedBuilder()
                .WithTitle("🏪 神秘商店")
                .WithDescription(desc.ToString())
                .WithColor(new Color(255, 215, 0)).Build(), cb);
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

        private (Embed embed, ComponentBuilder component) BuildCatchEmbed(TowerRun run)
        {
            var e = run.PendingCatch;
            bool full = run.Party.Count >= 3;

            var desc = new StringBuilder();
            desc.AppendLine($"野生的 **{e.Name}** {TypeBadge(e.Types)} 可以捕獲！");
            desc.AppendLine($"HP: {HpBar(e.MaxHP / 3, e.MaxHP)} · ATK: {e.Attack} · DEF: {e.Defense}");
            desc.AppendLine($"技能: {string.Join("、", e.Moves.Select(m => m.Name))}");
            desc.AppendLine();
            if (full)
                desc.AppendLine("⚠️ 背包已滿（3/3），無法捕獲。");
            else
                desc.AppendLine($"背包：{run.Party.Count}/3");

            var cb = new ComponentBuilder()
                .WithButton("🎾 捕獲！", $"tower_catch_{run.ChannelId}_yes", ButtonStyle.Success, disabled: full)
                .WithButton("放行", $"tower_catch_{run.ChannelId}_no", ButtonStyle.Secondary);

            return (new EmbedBuilder()
                .WithTitle("🎯 是否捕獲？")
                .WithDescription(desc.ToString())
                .WithColor(Color.Orange).Build(), cb);
        }

        private (Embed embed, ComponentBuilder component) BuildVictoryEmbed(TowerRun run)
        {
            var elapsed = (int)(DateTime.UtcNow - run.StartedAt).TotalMinutes;
            return (new EmbedBuilder()
                .WithTitle("🎉🏆 爬塔完成！恭喜！")
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
