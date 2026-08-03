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
    // ══════════════════════════════════════════════════════════
    //  Models
    // ══════════════════════════════════════════════════════════



    // ══════════════════════════════════════════════════════════
    //  Service
    // ══════════════════════════════════════════════════════════

    public class PokeTowerService
    {
        private readonly IDatabase _redisDb;
        private readonly bool _useRedis;
        private readonly Dictionary<ulong, TowerRun> _activeRuns = new();
        private const string REDIS_PREFIX = "tower:run:";
        private static readonly Random _rng = new();

        // ── Type emojis ──────────────────────────────────────
        private static readonly Dictionary<string, string> _typeEmoji = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Normal"] = "⬜", ["Fire"] = "🔥", ["Water"] = "💧", ["Electric"] = "⚡",
            ["Grass"] = "🌿", ["Ice"] = "❄️", ["Fighting"] = "🥊", ["Poison"] = "☠️",
            ["Ground"] = "🏔️", ["Flying"] = "🌪️", ["Psychic"] = "🔮", ["Bug"] = "🐛",
            ["Rock"] = "🪨", ["Ghost"] = "👻", ["Dragon"] = "🐉", ["Dark"] = "🌑",
            ["Steel"] = "⚙️", ["Fairy"] = "🌸"
        };

        // ── Move pool (real names, real types) ────────────────
        private static readonly List<TowerMove> _movePool = new()
        {
            // Normal
            new() { Name="Body Slam",    Type="Normal",   Power=85,  Category="Physical", Emoji="💪" },
            new() { Name="Hyper Beam",   Type="Normal",   Power=120, Category="Special",  Emoji="💥" },
            new() { Name="Quick Attack", Type="Normal",   Power=40,  Category="Physical", Emoji="💨" },
            new() { Name="Slash",        Type="Normal",   Power=70,  Category="Physical", Emoji="🗡️" },
            new() { Name="Tri Attack",   Type="Normal",   Power=80,  Category="Special",  Emoji="🔺" },
            // Fire
            new() { Name="Flamethrower", Type="Fire",     Power=90,  Category="Special",  Emoji="🔥" },
            new() { Name="Fire Blast",   Type="Fire",     Power=110, Category="Special",  Emoji="🔥" },
            new() { Name="Flame Wheel",  Type="Fire",     Power=60,  Category="Physical", Emoji="🔥" },
            new() { Name="Heat Wave",    Type="Fire",     Power=95,  Category="Special",  Emoji="🌡️" },
            new() { Name="Fire Fang",    Type="Fire",     Power=65,  Category="Physical", Emoji="🦷" },
            // Water
            new() { Name="Water Gun",    Type="Water",    Power=40,  Category="Special",  Emoji="💧" },
            new() { Name="Hydro Pump",   Type="Water",    Power=110, Category="Special",  Emoji="🌊" },
            new() { Name="Surf",         Type="Water",    Power=90,  Category="Special",  Emoji="🏄" },
            new() { Name="Aqua Tail",    Type="Water",    Power=90,  Category="Physical", Emoji="🐟" },
            new() { Name="Scald",        Type="Water",    Power=80,  Category="Special",  Emoji="♨️" },
            // Electric
            new() { Name="Thunderbolt",  Type="Electric", Power=90,  Category="Special",  Emoji="⚡" },
            new() { Name="Thunder",      Type="Electric", Power=110, Category="Special",  Emoji="🌩️" },
            new() { Name="Thunder Punch",Type="Electric", Power=75,  Category="Physical", Emoji="⚡" },
            new() { Name="Wild Charge",  Type="Electric", Power=90,  Category="Physical", Emoji="⚡" },
            new() { Name="Volt Switch",  Type="Electric", Power=70,  Category="Special",  Emoji="🔌" },
            // Grass
            new() { Name="Razor Leaf",   Type="Grass",    Power=55,  Category="Physical", Emoji="🍃" },
            new() { Name="Solar Beam",   Type="Grass",    Power=120, Category="Special",  Emoji="☀️" },
            new() { Name="Leaf Storm",   Type="Grass",    Power=130, Category="Special",  Emoji="🌿" },
            new() { Name="Seed Bomb",    Type="Grass",    Power=80,  Category="Physical", Emoji="💣" },
            new() { Name="Energy Ball",  Type="Grass",    Power=90,  Category="Special",  Emoji="🟢" },
            // Ice
            new() { Name="Ice Beam",     Type="Ice",      Power=90,  Category="Special",  Emoji="❄️" },
            new() { Name="Blizzard",     Type="Ice",      Power=110, Category="Special",  Emoji="🌨️" },
            new() { Name="Ice Punch",    Type="Ice",      Power=75,  Category="Physical", Emoji="❄️" },
            new() { Name="Icicle Crash", Type="Ice",      Power=85,  Category="Physical", Emoji="🧊" },
            new() { Name="Freeze Dry",   Type="Ice",      Power=70,  Category="Special",  Emoji="🥶" },
            // Fighting
            new() { Name="Close Combat", Type="Fighting", Power=120, Category="Physical", Emoji="🥊" },
            new() { Name="Brick Break",  Type="Fighting", Power=75,  Category="Physical", Emoji="🧱" },
            new() { Name="Aura Sphere",  Type="Fighting", Power=80,  Category="Special",  Emoji="🔵" },
            new() { Name="Superpower",   Type="Fighting", Power=120, Category="Physical", Emoji="💪" },
            new() { Name="Cross Chop",   Type="Fighting", Power=100, Category="Physical", Emoji="✂️" },
            // Psychic
            new() { Name="Psychic",      Type="Psychic",  Power=90,  Category="Special",  Emoji="🔮" },
            new() { Name="Psybeam",      Type="Psychic",  Power=65,  Category="Special",  Emoji="🌀" },
            new() { Name="Psycho Cut",   Type="Psychic",  Power=70,  Category="Physical", Emoji="🔮" },
            new() { Name="Zen Headbutt", Type="Psychic",  Power=80,  Category="Physical", Emoji="💫" },
            // Dragon
            new() { Name="Dragon Claw",  Type="Dragon",   Power=80,  Category="Physical", Emoji="🐉" },
            new() { Name="Draco Meteor", Type="Dragon",   Power=130, Category="Special",  Emoji="☄️" },
            new() { Name="Dragon Pulse", Type="Dragon",   Power=85,  Category="Special",  Emoji="🐉" },
            new() { Name="Outrage",      Type="Dragon",   Power=120, Category="Physical", Emoji="😡" },
            // Dark
            new() { Name="Crunch",       Type="Dark",     Power=80,  Category="Physical", Emoji="🌑" },
            new() { Name="Dark Pulse",   Type="Dark",     Power=80,  Category="Special",  Emoji="🌑" },
            new() { Name="Night Slash",  Type="Dark",     Power=70,  Category="Physical", Emoji="🌙" },
            new() { Name="Sucker Punch", Type="Dark",     Power=70,  Category="Physical", Emoji="👊" },
            // Ghost
            new() { Name="Shadow Ball",  Type="Ghost",    Power=80,  Category="Special",  Emoji="👻" },
            new() { Name="Shadow Claw",  Type="Ghost",    Power=70,  Category="Physical", Emoji="👻" },
            new() { Name="Phantom Force",Type="Ghost",    Power=90,  Category="Physical", Emoji="👻" },
            new() { Name="Hex",          Type="Ghost",    Power=65,  Category="Special",  Emoji="🔱" },
            // Rock
            new() { Name="Rock Slide",   Type="Rock",     Power=75,  Category="Physical", Emoji="🪨" },
            new() { Name="Stone Edge",   Type="Rock",     Power=100, Category="Physical", Emoji="🪨" },
            new() { Name="Power Gem",    Type="Rock",     Power=80,  Category="Special",  Emoji="💎" },
            // Ground
            new() { Name="Earthquake",   Type="Ground",   Power=100, Category="Physical", Emoji="🌍" },
            new() { Name="Earth Power",  Type="Ground",   Power=90,  Category="Special",  Emoji="🌏" },
            new() { Name="Dig",          Type="Ground",   Power=80,  Category="Physical", Emoji="⛏️" },
            // Flying
            new() { Name="Air Slash",    Type="Flying",   Power=75,  Category="Special",  Emoji="🌬️" },
            new() { Name="Brave Bird",   Type="Flying",   Power=120, Category="Physical", Emoji="🦅" },
            new() { Name="Hurricane",    Type="Flying",   Power=110, Category="Special",  Emoji="🌀" },
            new() { Name="Aerial Ace",   Type="Flying",   Power=60,  Category="Physical", Emoji="✈️" },
            // Bug
            new() { Name="X-Scissor",    Type="Bug",      Power=80,  Category="Physical", Emoji="✂️" },
            new() { Name="Bug Buzz",      Type="Bug",      Power=90,  Category="Special",  Emoji="🐝" },
            new() { Name="U-turn",        Type="Bug",      Power=70,  Category="Physical", Emoji="🔄" },
            // Poison
            new() { Name="Sludge Bomb",  Type="Poison",   Power=90,  Category="Special",  Emoji="☠️" },
            new() { Name="Poison Jab",   Type="Poison",   Power=80,  Category="Physical", Emoji="💉" },
            new() { Name="Gunk Shot",    Type="Poison",   Power=120, Category="Physical", Emoji="🗑️" },
            // Steel
            new() { Name="Iron Head",    Type="Steel",    Power=80,  Category="Physical", Emoji="⚙️" },
            new() { Name="Flash Cannon", Type="Steel",    Power=80,  Category="Special",  Emoji="💡" },
            new() { Name="Meteor Mash",  Type="Steel",    Power=90,  Category="Physical", Emoji="🌠" },
            // Fairy
            new() { Name="Moonblast",    Type="Fairy",    Power=95,  Category="Special",  Emoji="🌸" },
            new() { Name="Play Rough",   Type="Fairy",    Power=90,  Category="Physical", Emoji="🎀" },
            new() { Name="Dazzling Gleam",Type="Fairy",   Power=80,  Category="Special",  Emoji="✨" },
        };

        // ── Enemy templates (name, types, stat-total) ─────────
        private static readonly List<(string Name, string[] Types, int StatTotal)> _enemyPool = new()
        {
            // Tier 1 — floors 1–3
            ("Pidgeotto",  new[]{"Normal","Flying"}, 349),
            ("Graveler",   new[]{"Rock","Ground"},   390),
            ("Haunter",    new[]{"Ghost","Poison"},   405),
            ("Wartortle",  new[]{"Water"},            405),
            ("Charmeleon", new[]{"Fire"},             405),
            ("Ivysaur",    new[]{"Grass","Poison"},   405),
            ("Electabuzz", new[]{"Electric"},         490),
            ("Pikachu",    new[]{"Electric"},         320),
            // Tier 2 — floors 4–6
            ("Gyarados",   new[]{"Water","Flying"},   540),
            ("Lapras",     new[]{"Water","Ice"},      535),
            ("Jolteon",    new[]{"Electric"},         525),
            ("Starmie",    new[]{"Water","Psychic"},  520),
            ("Scyther",    new[]{"Bug","Flying"},     500),
            ("Magmar",     new[]{"Fire"},             495),
            ("Exeggutor",  new[]{"Grass","Psychic"},  530),
            ("Cloyster",   new[]{"Water","Ice"},      525),
            // Tier 3 — floors 7–9
            ("Machamp",    new[]{"Fighting"},         505),
            ("Gengar",     new[]{"Ghost","Poison"},   500),
            ("Alakazam",   new[]{"Psychic"},          500),
            ("Arcanine",   new[]{"Fire"},             555),
            ("Nidoking",   new[]{"Poison","Ground"},  505),
            ("Dragonair",  new[]{"Dragon"},           420),
            ("Aerodactyl", new[]{"Rock","Flying"},    515),
            ("Kangaskhan", new[]{"Normal"},           490),
            // Boss — floor 10
            ("Dragonite",  new[]{"Dragon","Flying"},  600),
            ("Mewtwo",     new[]{"Psychic"},          680),
            ("Tyranitar",  new[]{"Rock","Dark"},      600),
            ("Garchomp",   new[]{"Dragon","Ground"},  600),
            ("Salamence",  new[]{"Dragon","Flying"},  600),
        };

        // ── Type effectiveness chart ──────────────────────────
        private static readonly Dictionary<string, Dictionary<string, float>> _typeChart = BuildTypeChart();

        private static Dictionary<string, Dictionary<string, float>> BuildTypeChart()
        {
            var types = new[]
            {
                "Normal","Fire","Water","Electric","Grass","Ice","Fighting","Poison",
                "Ground","Flying","Psychic","Bug","Rock","Ghost","Dragon","Dark","Steel","Fairy"
            };
            var c = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in types)
            {
                c[a] = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in types) c[a][d] = 1.0f;
            }

            void SE(string a, string d) => c[a][d] = 2.0f;
            void NE(string a, string d) => c[a][d] = 0.5f;
            void IM(string a, string d) => c[a][d] = 0.0f;

            SE("Fire","Grass");SE("Fire","Ice");SE("Fire","Bug");SE("Fire","Steel");
            NE("Fire","Fire");NE("Fire","Water");NE("Fire","Rock");NE("Fire","Dragon");

            SE("Water","Fire");SE("Water","Ground");SE("Water","Rock");
            NE("Water","Water");NE("Water","Grass");NE("Water","Dragon");

            SE("Electric","Water");SE("Electric","Flying");
            NE("Electric","Electric");NE("Electric","Grass");NE("Electric","Dragon");
            IM("Electric","Ground");

            SE("Grass","Water");SE("Grass","Ground");SE("Grass","Rock");
            NE("Grass","Fire");NE("Grass","Grass");NE("Grass","Poison");NE("Grass","Flying");NE("Grass","Bug");NE("Grass","Dragon");NE("Grass","Steel");

            SE("Ice","Grass");SE("Ice","Ground");SE("Ice","Flying");SE("Ice","Dragon");
            NE("Ice","Fire");NE("Ice","Water");NE("Ice","Ice");NE("Ice","Steel");

            SE("Fighting","Normal");SE("Fighting","Ice");SE("Fighting","Rock");SE("Fighting","Dark");SE("Fighting","Steel");
            NE("Fighting","Poison");NE("Fighting","Flying");NE("Fighting","Psychic");NE("Fighting","Bug");NE("Fighting","Fairy");
            IM("Fighting","Ghost");

            SE("Poison","Grass");SE("Poison","Fairy");
            NE("Poison","Poison");NE("Poison","Ground");NE("Poison","Rock");NE("Poison","Ghost");
            IM("Poison","Steel");

            SE("Ground","Fire");SE("Ground","Electric");SE("Ground","Poison");SE("Ground","Rock");SE("Ground","Steel");
            NE("Ground","Grass");NE("Ground","Bug");
            IM("Ground","Flying");

            SE("Flying","Grass");SE("Flying","Fighting");SE("Flying","Bug");
            NE("Flying","Electric");NE("Flying","Rock");NE("Flying","Steel");

            SE("Psychic","Fighting");SE("Psychic","Poison");
            NE("Psychic","Psychic");NE("Psychic","Steel");
            IM("Psychic","Dark");

            SE("Bug","Grass");SE("Bug","Psychic");SE("Bug","Dark");
            NE("Bug","Fire");NE("Bug","Fighting");NE("Bug","Flying");NE("Bug","Ghost");NE("Bug","Steel");NE("Bug","Fairy");

            SE("Rock","Fire");SE("Rock","Ice");SE("Rock","Flying");SE("Rock","Bug");
            NE("Rock","Fighting");NE("Rock","Ground");NE("Rock","Steel");

            SE("Ghost","Psychic");SE("Ghost","Ghost");
            NE("Ghost","Dark");
            IM("Ghost","Normal");

            SE("Dragon","Dragon");
            NE("Dragon","Steel");
            IM("Dragon","Fairy");

            SE("Dark","Psychic");SE("Dark","Ghost");
            NE("Dark","Fighting");NE("Dark","Dark");NE("Dark","Fairy");

            SE("Steel","Ice");SE("Steel","Rock");SE("Steel","Fairy");
            NE("Steel","Fire");NE("Steel","Water");NE("Steel","Electric");NE("Steel","Steel");

            SE("Fairy","Fighting");SE("Fairy","Dragon");SE("Fairy","Dark");
            NE("Fairy","Fire");NE("Fairy","Poison");NE("Fairy","Steel");

            IM("Normal","Ghost");

            return c;
        }

        // ══════════════════════════════════════════════════════
        //  Constructor
        // ══════════════════════════════════════════════════════

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
                catch (Exception ex)
                {
                    Console.WriteLine($"[Tower] Redis 連線失敗: {ex.Message}");
                }
            }
            _ = LoadRunsAsync();
        }

        // ══════════════════════════════════════════════════════
        //  Public API
        // ══════════════════════════════════════════════════════

        public bool HasActiveRun(ulong channelId) => _activeRuns.ContainsKey(channelId);

        public TowerRun GetRun(ulong channelId)
            => _activeRuns.TryGetValue(channelId, out var r) ? r : null;

        /// <summary>顯示 Pokemon 選擇畫面（傳入玩家持有的 Pokemon 列表）</summary>
        public (Embed embed, ComponentBuilder component) ShowPokemonSelection(
            ulong channelId, ulong playerId, string playerName, List<PokeGamePokemon> pokemons)
        {
            if (_activeRuns.TryGetValue(channelId, out var existing))
            {
                return (new EmbedBuilder()
                    .WithTitle("❌ 此頻道已有爬塔進行中")
                    .WithDescription($"**{existing.PlayerName}** 正在第 {existing.CurrentFloor} 層（共 {existing.MaxFloor} 層）。\n" +
                                     $"等待結束，或讓當事人用 `/取消爬塔` 中止。")
                    .WithColor(Color.Red).Build(), new ComponentBuilder());
            }

            if (pokemons == null || pokemons.Count == 0)
            {
                return (new EmbedBuilder()
                    .WithTitle("😅 你還沒有寶可夢")
                    .WithDescription("先用 `/抓寶可夢` 抓一隻再來挑戰爬塔！")
                    .WithColor(Color.Orange).Build(), new ComponentBuilder());
            }

            var showList = pokemons.Take(6).ToList();
            var embed = new EmbedBuilder()
                .WithTitle("🏔️ 寶可夢爬塔 — 選擇你的夥伴")
                .WithDescription(
                    "選一隻進入爬塔。\n" +
                    "**爬塔期間造成的傷害、HP 都會保留**，直到你退出為止。\n\n" +
                    "共 **10 層**，第 10 層是 Boss。途中可以換寶可夢和技能。")
                .WithColor(new Color(70, 130, 180))
                .WithFooter($"{playerName} 的爬塔請求");

            for (int i = 0; i < showList.Count; i++)
            {
                var p = showList[i];
                var shiny = p.isShiny ? " ✨" : "";
                var types = TypeBadge(p.Types);
                embed.AddField(
                    $"{i + 1}. {p.CustomName ?? p.Name}{shiny} {types}",
                    $"HP {p.HP} | ATK {p.Attack} | DEF {p.Defense} | SPD {p.Speed}",
                    inline: true);
            }

            var cb = new ComponentBuilder();
            var row1 = new ActionRowBuilder();
            var row2 = new ActionRowBuilder();
            for (int i = 0; i < showList.Count; i++)
            {
                var p = showList[i];
                var btn = new ButtonBuilder()
                    .WithLabel(p.CustomName ?? p.Name)
                    .WithCustomId($"tower_select_{channelId}_{playerId}_{i}")
                    .WithStyle(ButtonStyle.Primary);
                (i < 3 ? row1 : row2).AddComponent(btn);
            }
            cb.AddRow(row1);
            if (showList.Count > 3) cb.AddRow(row2);

            return (embed.Build(), cb);
        }

        /// <summary>開始爬塔 run（用選定的 Pokemon）</summary>
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
            run.TeamPokemon.Add(pokemon);
            run.RunLog.Add($"🏔️ {playerName} 帶著 {pokemon.DisplayName} 踏進了爬塔！");

            _activeRuns[channelId] = run;
            await SaveAsync(run);

            return BuildPathEmbed(run);
        }

        /// <summary>選擇路徑：battle / rest / shop</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandlePathChoiceAsync(
            ulong channelId, string choice)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            run.CurrentFloor++;
            bool isBoss = run.CurrentFloor == run.MaxFloor;

            switch (choice)
            {
                case "battle":
                    run.CurrentEnemy = GenEnemy(run.CurrentFloor, isBoss);
                    run.State = TowerRunState.InBattle;
                    run.RunLog.Add($"⚔️ F{run.CurrentFloor}：遭遇 {run.CurrentEnemy.Name}！");
                    await SaveAsync(run);
                    return BuildBattleEmbed(run, "");

                case "rest":
                    int healed = Math.Max(1, (int)(run.ActivePokemon.MaxHP * 0.35));
                    run.ActivePokemon.CurrentHP = Math.Min(run.ActivePokemon.MaxHP, run.ActivePokemon.CurrentHP + healed);
                    run.RunLog.Add($"🏕️ F{run.CurrentFloor}：休息恢復 {healed} HP");
                    await SaveAsync(run);
                    return BuildPathEmbed(run, $"🏕️ **{run.ActivePokemon.DisplayName}** 在此休息，恢復了 **{healed} HP**！");

                case "shop":
                    run.State = TowerRunState.Shopping;
                    await SaveAsync(run);
                    return BuildShopEmbed(run);

                default:
                    return ErrEmbed("未知的路徑選擇");
            }
        }

        /// <summary>戰鬥中選擇技能</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleMoveAsync(
            ulong channelId, int moveIdx)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");
            if (run.State != TowerRunState.InBattle)
                return ErrEmbed("目前不在戰鬥中");
            if (moveIdx < 0 || moveIdx >= run.ActivePokemon.Moves.Count)
                return ErrEmbed("無效的技能選擇");

            var poke = run.ActivePokemon;
            var enemy = run.CurrentEnemy;
            var playerMove = poke.Moves[moveIdx];
            var enemyMove = enemy.Moves[enemy.NextMoveIdx % enemy.Moves.Count];
            bool playerFirst = poke.Speed >= enemy.Speed;

            var sb = new StringBuilder();

            if (playerFirst)
            {
                int d = Damage(playerMove, poke.Attack, poke.SpecialAttack, enemy.Defense, enemy.SpecialDefense, enemy.Types);
                enemy.CurrentHP = Math.Max(0, enemy.CurrentHP - d);
                run.TotalDamageDealt += d;
                AppendHit(sb, poke.DisplayName, enemy.Name, playerMove, d, enemy.Types, true);

                if (enemy.CurrentHP > 0)
                {
                    int ed = Damage(enemyMove, enemy.Attack, enemy.SpecialAttack, poke.Defense, poke.SpecialDefense, poke.Types);
                    poke.CurrentHP = Math.Max(0, poke.CurrentHP - ed);
                    AppendHit(sb, enemy.Name, poke.DisplayName, enemyMove, ed, poke.Types, false);
                }
            }
            else
            {
                int ed = Damage(enemyMove, enemy.Attack, enemy.SpecialAttack, poke.Defense, poke.SpecialDefense, poke.Types);
                poke.CurrentHP = Math.Max(0, poke.CurrentHP - ed);
                AppendHit(sb, enemy.Name, poke.DisplayName, enemyMove, ed, poke.Types, false);

                if (poke.CurrentHP > 0)
                {
                    int d = Damage(playerMove, poke.Attack, poke.SpecialAttack, enemy.Defense, enemy.SpecialDefense, enemy.Types);
                    enemy.CurrentHP = Math.Max(0, enemy.CurrentHP - d);
                    run.TotalDamageDealt += d;
                    AppendHit(sb, poke.DisplayName, enemy.Name, playerMove, d, enemy.Types, true);
                }
            }

            enemy.NextMoveIdx = (enemy.NextMoveIdx + 1) % enemy.Moves.Count;
            string battleText = sb.ToString();
            run.RunLog.Add(battleText.Replace("\n", " "));

            // ── Check battle end ──────────────────────────────
            if (enemy.CurrentHP <= 0)
            {
                run.FloorsCleared++;
                run.RunLog.Add($"✅ 擊倒 {enemy.Name}！");

                if (run.CurrentFloor >= run.MaxFloor)
                {
                    run.State = TowerRunState.Victory;
                    await RemoveAsync(channelId);
                    return BuildVictoryEmbed(run, battleText);
                }

                run.State = TowerRunState.SelectingPath;
                run.CurrentEnemy = null;
                await SaveAsync(run);
                return BuildPathEmbed(run, battleText + $"\n\n🎉 **{enemy.Name} 被擊倒！**");
            }

            if (poke.CurrentHP <= 0)
            {
                run.State = TowerRunState.Defeated;
                await RemoveAsync(channelId);
                return BuildDefeatEmbed(run, battleText);
            }

            await SaveAsync(run);
            return BuildBattleEmbed(run, battleText);
        }

        /// <summary>商店購買道具</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleShopItemAsync(
            ulong channelId, string itemKey)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            string msg;
            switch (itemKey)
            {
                case "heal_full":
                    run.ActivePokemon.CurrentHP = run.ActivePokemon.MaxHP;
                    msg = $"💊 使用「全回復」— {run.ActivePokemon.DisplayName} HP 完全恢復！";
                    break;
                case "heal_half":
                    int h = Math.Max(1, run.ActivePokemon.MaxHP / 2);
                    run.ActivePokemon.CurrentHP = Math.Min(run.ActivePokemon.MaxHP, run.ActivePokemon.CurrentHP + h);
                    msg = $"🧃 使用「超級樹果」— 恢復 {h} HP！";
                    break;
                case "new_move":
                    var pool = PickMoves(run.ActivePokemon.Types);
                    var nm = pool.FirstOrDefault(m => run.ActivePokemon.Moves.All(em => em.Name != m.Name)) ?? pool[0];
                    int slot = _rng.Next(4);
                    string old = run.ActivePokemon.Moves[slot].Name;
                    run.ActivePokemon.Moves[slot] = nm;
                    msg = $"📀 忘掉了 **{old}**，學會了 **{nm.Emoji} {nm.Name}**！";
                    break;
                default:
                    return ErrEmbed("未知的道具");
            }

            run.State = TowerRunState.SelectingPath;
            run.RunLog.Add(msg);
            await SaveAsync(run);
            return BuildPathEmbed(run, msg);
        }

        /// <summary>顯示換寶可夢的選擇畫面</summary>
        public (Embed embed, ComponentBuilder component) ShowSwapSelection(
            ulong channelId, List<PokeGamePokemon> allPlayerPokemons)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            // Build list: already-in-team Pokemon (show current HP) + new ones
            var embed = new EmbedBuilder()
                .WithTitle("🔄 換寶可夢")
                .WithDescription(
                    $"目前：**{run.ActivePokemon.DisplayName}** HP {run.ActivePokemon.CurrentHP}/{run.ActivePokemon.MaxHP}\n\n" +
                    "選擇要換上場的寶可夢（爬塔中各自的 HP 都會保留）：")
                .WithColor(Color.Blue);

            var cb = new ComponentBuilder();
            var row1 = new ActionRowBuilder();
            var row2 = new ActionRowBuilder();

            var available = allPlayerPokemons.Take(8).ToList();
            for (int i = 0; i < available.Count; i++)
            {
                var p = available[i];
                // Check if already in team
                var inTeam = run.TeamPokemon.FirstOrDefault(tp =>
                    tp.PokeId == p.Id && tp.CaughtAt == p.CaughtDate);
                string hpNote = inTeam != null
                    ? $"HP {inTeam.CurrentHP}/{inTeam.MaxHP}"
                    : $"新加入 HP {p.HP}";
                bool isActive = inTeam != null &&
                    inTeam.PokeId == run.ActivePokemon.PokeId &&
                    inTeam.CaughtAt == run.ActivePokemon.CaughtAt;

                embed.AddField(
                    $"{(isActive ? "▶ " : "")}{p.CustomName ?? p.Name}",
                    hpNote, inline: true);

                if (!isActive)
                {
                    var btn = new ButtonBuilder()
                        .WithLabel(p.CustomName ?? p.Name)
                        .WithCustomId($"tower_swap_{channelId}_{i}")
                        .WithStyle(ButtonStyle.Primary)
                        .WithDisabled(isActive);
                    (i < 4 ? row1 : row2).AddComponent(btn);
                }
            }
            cb.AddRow(row1);
            if (available.Count > 4) cb.AddRow(row2);
            cb.WithButton("取消", $"tower_swap_cancel_{channelId}", ButtonStyle.Secondary, row: 2);

            return (embed.Build(), cb);
        }

        /// <summary>確認換上某隻 Pokemon</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleSwapConfirmAsync(
            ulong channelId, PokeGamePokemon src)
        {
            if (!_activeRuns.TryGetValue(channelId, out var run))
                return ErrEmbed("找不到進行中的爬塔");

            // Check if already in team
            var existing = run.TeamPokemon.FirstOrDefault(
                tp => tp.PokeId == src.Id && tp.CaughtAt == src.CaughtDate);

            TowerPokemon newActive;
            if (existing != null)
            {
                newActive = existing;
            }
            else
            {
                newActive = ConvertPokemon(src);
                newActive.Moves = PickMoves(src.Types);
                run.TeamPokemon.Add(newActive);
            }

            string prevName = run.ActivePokemon.DisplayName;
            run.ActivePokemon = newActive;
            run.RunLog.Add($"🔄 換上了 {newActive.DisplayName}（換下 {prevName}）");
            await SaveAsync(run);

            return run.State == TowerRunState.InBattle
                ? BuildBattleEmbed(run, $"🔄 換上了 **{newActive.DisplayName}**！")
                : BuildPathEmbed(run, $"🔄 換上了 **{newActive.DisplayName}**！");
        }

        /// <summary>取消爬塔</summary>
        public async Task<bool> CancelRunAsync(ulong channelId)
        {
            if (!_activeRuns.ContainsKey(channelId)) return false;
            await RemoveAsync(channelId);
            return true;
        }

        // ══════════════════════════════════════════════════════
        //  Private helpers
        // ══════════════════════════════════════════════════════

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

        private List<TowerMove> PickMoves(List<string> types)
        {
            var relevantTypes = (types ?? new()).Concat(new[] { "Normal" }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var pool = _movePool
                .Where(m => relevantTypes.Any(t => t.Equals(m.Type, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(_ => _rng.Next())
                .ToList();

            if (pool.Count < 4)
                pool.AddRange(_movePool.OrderBy(_ => _rng.Next()).Take(4 - pool.Count));

            return pool.Take(4).ToList();
        }

        private TowerEnemy GenEnemy(int floor, bool isBoss)
        {
            IEnumerable<(string Name, string[] Types, int StatTotal)> tier;

            if (isBoss)
                tier = _enemyPool.Where(e => e.StatTotal >= 590);
            else if (floor <= 3)
                tier = _enemyPool.Where(e => e.StatTotal < 430);
            else if (floor <= 6)
                tier = _enemyPool.Where(e => e.StatTotal >= 430 && e.StatTotal < 545);
            else
                tier = _enemyPool.Where(e => e.StatTotal >= 480 && e.StatTotal < 590);

            var choices = tier.ToList();
            if (choices.Count == 0) choices = _enemyPool;
            var t = choices[_rng.Next(choices.Count)];

            float scale = isBoss ? 1.5f : 1.0f + (floor - 1) * 0.07f;
            int base_ = Math.Max(30, (int)(t.StatTotal * scale / 6));

            return new TowerEnemy
            {
                Name = isBoss ? $"👑 {t.Name}" : t.Name,
                Types = t.Types.ToList(),
                MaxHP = (int)(base_ * 1.6),
                CurrentHP = (int)(base_ * 1.6),
                Attack = base_,
                Defense = (int)(base_ * 0.85),
                SpecialAttack = base_,
                SpecialDefense = (int)(base_ * 0.85),
                Speed = base_,
                IsBoss = isBoss,
                Moves = PickMoves(t.Types.ToList()),
            };
        }

        private int Damage(TowerMove move, int atk, int spAtk, int def, int spDef, List<string> defTypes)
        {
            int a = move.Category == "Physical" ? atk : spAtk;
            int d = move.Category == "Physical" ? def : spDef;
            float eff = TypeEffectiveness(move.Type, defTypes);
            int raw = (int)(move.Power * a / (float)Math.Max(1, d) * eff / 7.5f);
            return Math.Max(1, (int)(raw * (0.85 + _rng.NextDouble() * 0.15)));
        }

        private float TypeEffectiveness(string moveType, List<string> defTypes)
        {
            if (!_typeChart.TryGetValue(moveType, out var row)) return 1f;
            float m = 1f;
            foreach (var dt in defTypes)
                if (row.TryGetValue(dt, out var v)) m *= v;
            return m;
        }

        private void AppendHit(StringBuilder sb, string atkName, string defName,
            TowerMove move, int dmg, List<string> defTypes, bool isPlayer)
        {
            float eff = TypeEffectiveness(move.Type, defTypes);
            string effNote = eff switch
            {
                >= 2f => " **（超級效果！）**",
                <= 0f => " **（無效！）**",
                < 1f  => " （效果不佳）",
                _     => ""
            };
            string tag = isPlayer ? "🗡️" : "💢";
            sb.AppendLine($"{tag} **{atkName}** 使出 {move.Emoji} **{move.Name}**{effNote}");
            sb.AppendLine($"　→ 對 {defName} 造成 **{dmg}** 傷害");
        }

        private string HpBar(int cur, int max, int len = 10)
        {
            float r = max == 0 ? 0 : (float)cur / max;
            int filled = (int)(r * len);
            string col = r > 0.5f ? "🟩" : r > 0.25f ? "🟨" : "🟥";
            return string.Concat(Enumerable.Repeat(col, filled))
                 + string.Concat(Enumerable.Repeat("⬛", Math.Max(0, len - filled)))
                 + $" **{cur}/{max}**";
        }

        private string TypeBadge(List<string> types) =>
            string.Join(" ", (types ?? new()).Select(t => _typeEmoji.GetValueOrDefault(t, "❓") + t));

        // ── Embed builders ────────────────────────────────────

        private (Embed embed, ComponentBuilder component) BuildPathEmbed(TowerRun run, string extra = "")
        {
            bool nextIsBoss = (run.CurrentFloor + 1) == run.MaxFloor;
            var p = run.ActivePokemon;

            var desc = new StringBuilder();
            if (!string.IsNullOrEmpty(extra)) desc.AppendLine(extra).AppendLine();
            desc.AppendLine($"**{p.DisplayName}** {TypeBadge(p.Types)}");
            desc.AppendLine($"HP: {HpBar(p.CurrentHP, p.MaxHP)}");
            desc.AppendLine();
            desc.Append($"進入第 **{run.CurrentFloor + 1}** 層");
            if (nextIsBoss) desc.Append(" ⚠️ **BOSS FLOOR**");
            desc.AppendLine();
            desc.AppendLine();
            desc.Append("選擇路徑：");

            var embed = new EmbedBuilder()
                .WithTitle($"🏔️ 爬塔進度 {run.FloorsCleared}/{run.MaxFloor} 層")
                .WithDescription(desc.ToString())
                .WithColor(nextIsBoss ? Color.Gold : new Color(70, 130, 180))
                .WithFooter($"{run.PlayerName} • 累積傷害 {run.TotalDamageDealt}")
                .Build();

            var cb = new ComponentBuilder();
            if (nextIsBoss)
            {
                cb.WithButton("👑 挑戰 Boss！", $"tower_path_{run.ChannelId}_battle", ButtonStyle.Danger, row: 0);
            }
            else
            {
                cb.WithButton("⚔️ 戰鬥", $"tower_path_{run.ChannelId}_battle", ButtonStyle.Danger, row: 0)
                  .WithButton("🏕️ 休息 +35%HP", $"tower_path_{run.ChannelId}_rest",   ButtonStyle.Success, row: 0)
                  .WithButton("🏪 商店",          $"tower_path_{run.ChannelId}_shop",   ButtonStyle.Secondary, row: 0);
            }
            cb.WithButton("🔄 換寶可夢", $"tower_swap_request_{run.ChannelId}", ButtonStyle.Secondary, row: 1);

            return (embed, cb);
        }

        private (Embed embed, ComponentBuilder component) BuildBattleEmbed(TowerRun run, string log)
        {
            var p = run.ActivePokemon;
            var e = run.CurrentEnemy;
            var nextMove = e.Moves[e.NextMoveIdx % e.Moves.Count];

            var desc = new StringBuilder();
            if (!string.IsNullOrEmpty(log)) desc.AppendLine(log).AppendLine("───────────────────────").AppendLine();
            desc.AppendLine($"**你的 {p.DisplayName}** {TypeBadge(p.Types)}");
            desc.AppendLine($"HP: {HpBar(p.CurrentHP, p.MaxHP)}");
            desc.AppendLine();
            desc.AppendLine($"**{(e.IsBoss ? "👑" : "🎯")} {e.Name}** {TypeBadge(e.Types)}");
            desc.AppendLine($"HP: {HpBar(e.CurrentHP, e.MaxHP)}");
            desc.AppendLine();
            desc.AppendLine($"🔮 **{e.Name}** 準備使出：{nextMove.Emoji} **{nextMove.Name}**（{nextMove.Power} 威力）");
            desc.AppendLine();
            desc.Append("選擇你的技能：");

            var embed = new EmbedBuilder()
                .WithTitle($"⚔️ 第 {run.CurrentFloor} 層 — 戰鬥中！")
                .WithDescription(desc.ToString())
                .WithColor(e.IsBoss ? Color.Gold : Color.Red)
                .WithFooter($"{run.PlayerName} • F{run.CurrentFloor}/{run.MaxFloor}")
                .Build();

            var cb = new ComponentBuilder();
            var row = new ActionRowBuilder();
            for (int i = 0; i < p.Moves.Count; i++)
            {
                var m = p.Moves[i];
                row.AddComponent(new ButtonBuilder()
                    .WithLabel($"{m.Emoji}{m.Name} ({m.Power})")
                    .WithCustomId($"tower_move_{run.ChannelId}_{i}")
                    .WithStyle(ButtonStyle.Primary));
            }
            cb.AddRow(row);
            cb.WithButton("🔄 換寶可夢", $"tower_swap_request_{run.ChannelId}", ButtonStyle.Secondary, row: 1);

            return (embed, cb);
        }

        private (Embed embed, ComponentBuilder component) BuildShopEmbed(TowerRun run)
        {
            var p = run.ActivePokemon;
            return (new EmbedBuilder()
                .WithTitle("🏪 神秘商店")
                .WithDescription(
                    $"**{p.DisplayName}** HP: {HpBar(p.CurrentHP, p.MaxHP)}\n\n" +
                    "選擇一樣道具（**免費**）：\n\n" +
                    "💊 **全回復** — HP 完全恢復\n" +
                    "🧃 **超級樹果** — 恢復 50% HP\n" +
                    "📀 **技能學習器** — 隨機更換一個技能")
                .WithColor(new Color(255, 215, 0))
                .Build(),
            new ComponentBuilder()
                .WithButton("💊 全回復",    $"tower_shop_{run.ChannelId}_heal_full", ButtonStyle.Success)
                .WithButton("🧃 超級樹果",  $"tower_shop_{run.ChannelId}_heal_half", ButtonStyle.Primary)
                .WithButton("📀 技能學習器",$"tower_shop_{run.ChannelId}_new_move",  ButtonStyle.Secondary));
        }

        private (Embed embed, ComponentBuilder component) BuildVictoryEmbed(TowerRun run, string last)
        {
            var elapsed = (int)(DateTime.UtcNow - run.StartedAt).TotalMinutes;
            return (new EmbedBuilder()
                .WithTitle("🎉🏆 爬塔完成！恭喜！")
                .WithDescription(
                    $"{last}\n\n" +
                    $"**{run.PlayerName}** 帶著 **{run.ActivePokemon.DisplayName}** 征服了全 **{run.MaxFloor}** 層！\n\n" +
                    $"📊 **成績**\n" +
                    $"• 清關：{run.FloorsCleared}/{run.MaxFloor} 層\n" +
                    $"• 累積傷害：{run.TotalDamageDealt}\n" +
                    $"• 剩餘 HP：{run.ActivePokemon.CurrentHP}/{run.ActivePokemon.MaxHP}\n" +
                    $"• 用時：{elapsed} 分鐘")
                .WithColor(Color.Gold)
                .Build(), new ComponentBuilder());
        }

        private (Embed embed, ComponentBuilder component) BuildDefeatEmbed(TowerRun run, string last)
        {
            var elapsed = (int)(DateTime.UtcNow - run.StartedAt).TotalMinutes;
            return (new EmbedBuilder()
                .WithTitle("💀 倒下了...")
                .WithDescription(
                    $"{last}\n\n" +
                    $"**{run.ActivePokemon.DisplayName}** 在第 **{run.CurrentFloor}** 層力竭倒下。\n\n" +
                    $"📊 **成績**\n" +
                    $"• 清關：{run.FloorsCleared}/{run.MaxFloor} 層\n" +
                    $"• 累積傷害：{run.TotalDamageDealt}\n" +
                    $"• 用時：{elapsed} 分鐘\n\n" +
                    "下次再挑戰！")
                .WithColor(Color.DarkRed)
                .Build(), new ComponentBuilder());
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
                var json = JsonSerializer.Serialize(run);
                await _redisDb.StringSetAsync($"{REDIS_PREFIX}{run.ChannelId}", json, TimeSpan.FromDays(30));
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
                var ep = _redisDb.Multiplexer.GetEndPoints()[0];
                var server = _redisDb.Multiplexer.GetServer(ep);
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
