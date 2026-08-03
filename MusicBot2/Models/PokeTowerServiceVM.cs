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

namespace MusicBot2.Models
{
    public enum TowerRunState
    {
        SelectingPath,
        InBattle,
        Shopping,
        Victory,
        Defeated
    }

    public class TowerMove
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int Power { get; set; }
        public string Category { get; set; } // Physical / Special
        public string Emoji { get; set; } = "⚡";
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
    }

    public class TowerRun
    {
        public ulong PlayerId { get; set; }
        public string PlayerName { get; set; }
        public ulong ChannelId { get; set; }
        public int CurrentFloor { get; set; } = 0;
        public int MaxFloor { get; set; } = 10;
        public TowerPokemon ActivePokemon { get; set; }
        public List<TowerPokemon> TeamPokemon { get; set; } = new();
        public TowerEnemy CurrentEnemy { get; set; }
        public TowerRunState State { get; set; } = TowerRunState.SelectingPath;
        public List<string> RunLog { get; set; } = new();
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public int TotalDamageDealt { get; set; }
        public int FloorsCleared { get; set; }
    }
}
