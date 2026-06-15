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
        public SpritesOther other { get; set; }
    }

    public class SpritesOther
    {
        public SprotesImage dream_world { get; set; }
        public SprotesImage home { get; set; }
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









}
