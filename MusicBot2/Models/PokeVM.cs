using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Models
{
    public class Pokemon
    {
        public int id { get; set; }
        public Cries cries { get; set; }
        public Sprites sprites { get; set; }
        public List<PokeStats> stats { get; set; }
        public List<PokeType> types { get; set; }
        public PokeSpecies formatted_name { get; set; }
        public ResultData species { get; set; }

    }

    public class Cries
    {
        public string latest { get; set; }
        public string legacy { get; set; }
    }

    public class Sprites
    {
        public string front_default { get; set; }
        public string back_default { get; set; }
        public string front_shiny { get; set; }
        public SpritesOther other { get; set; }
    }

    public class SpritesOther
    {
        public SprotesImage dream_world { get; set; }
        public SprotesImage home { get; set; }
        [JsonProperty("official-artwork")]
        public SprotesImage official_artwork { get; set; }
        public SprotesImage showdown { get; set; }
    }

    public class SprotesImage
    {
        public string front_default { get; set; }
        public string back_default { get; set; }
    }

    public class PokeStats
    {
        public int base_stat { get; set; }
        public int effort { get; set; }
        public ResultData stat { get; set; }
    }

    public class PokeType
    {
        public int slot { get; set; }
        public ResultData type { get; set; }
    }


    public class PokeSpecies
    {
        public int id { get; set; }
        public List<PokeGenera> genera { get; set; }
        public List<PokeNames> names { get; set; }
        public List<FlavorTextEntries> flavor_text_entries { get; set; }
    }

    public class FlavorTextEntries
    {
        public string flavor_text { get; set; }
        public ResultData language { get; set; }
    }

    public class PokeGenera
    {
        public string genus { get; set; }
        public ResultData language { get; set; }
    }

    public class PokeNames
    {
        public string name { get; set; }
        public ResultData language { get; set; }
    }




    public class RandomResponse
    {
        public int count { get; set; }
        public List<ResultData> results { get; set; }
    }
    public class ResultData
    {
        public string name { get; set; }
        public string url { get; set; }
    }



    //--------------------------------以下為招式資料結構---------------------------------
    public class Move
    {
        public int id { get; set; }
        public ResultData damage_class { get; set; }
        public List<FlavorTextEntries> flavor_text_entries { get; set; }
        public List<PokeNames> names { get; set; }
        public List<ResultData> learned_by_pokemon { get; set; }
    }

    //--------------------------------以下為poke遊戲相關---------------------------------

    #region 客製化遊戲相關
    public class PokeGamePlayer
    {
        public ulong UserId { get; set; }
        public string UserName { get; set; }
        public List<PokeGamePokemon> CaughtPokemon { get; set; } = new List<PokeGamePokemon>();
        public DateTime? LastCatchDate { get; set; }
        public int TotalBattles { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
    }

    public class PokeGamePokemon
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string CustomName { get; set; }
        public string ImageUrl { get; set; }
        public int HP { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int SpecialAttack { get; set; }
        public int SpecialDefense { get; set; }
        public int Speed { get; set; }
        public List<string> Types { get; set; } = new List<string>();
        public DateTime CaughtDate { get; set; }
        public bool isShiny { get; set; }
    }

    public class BattleMatchmaking
    {
        public ulong UserId { get; set; }
        public string UserName { get; set; }
        public PokeGamePokemon Pokemon { get; set; }
        public DateTime SearchStartTime { get; set; }
    }

    public class BattleResult
    {
        public ulong WinnerId { get; set; }
        public string WinnerName { get; set; }
        public PokeGamePokemon WinnerPokemon { get; set; }
        public ulong LoserId { get; set; }
        public string LoserName { get; set; }
        public PokeGamePokemon LoserPokemon { get; set; }
        public string BattleDescription { get; set; }
    }
    #endregion







}
