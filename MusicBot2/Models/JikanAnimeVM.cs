using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Models
{
    public class CharactersResopnse
    {
        public int mal_id { get; set; }
        public string url { get; set; }
        public Images images { get; set; }
        public string name { get; set; }
        public string name_kanji { get; set; }
        public List<string> nicknames { get; set; }
        public int favorites { get; set; }
        public string about { get; set; }
        public List<Voices> voices { get; set; }
    }

    public class Images
    {
        public Jpg jpg { get; set; }
    }

    public class Jpg
    {
        public string image_url { get; set; }
    }

    public class TopCharactersResponse
    {
        public List<CharactersResopnse> data { get; set; }
    }

    public class AnimeResponse
    {
        public int mal_id { get; set; }
        public string url { get; set; }
        public Images images { get; set; }
        public Trailer trailer { get; set; }
        public string title { get; set; }
        public string title_english { get; set; }
        public string title_japanese { get; set; }
        public List<string> title_synonyms { get; set; }
        public int favorites { get; set; }
        public string synopsis { get; set; }
        public string rating { get; set; }
        public double? score { get; set; }
    }

    public class Trailer
    {
        public string url { get; set; }
    }

    public class CharacterWrapper
    {
        public CharactersResopnse data { get; set; }
    }

    public class AnimeWrapper
    {
        public AnimeResponse data { get; set; }
    }

    public class TopAnimeResponse
    {
        public List<AnimeResponse> data { get; set; }
    }

    public class CharacterAnimeResponse
    {
        public List<CharacterAnimeData> data { get; set; }
    }

    public class CharacterAnimeData
    {
        public AnimeResponse anime { get; set; }
    }

    public class TopMangaResponse
    {
        public List<MangaResponse> data { get; set; }
    }

    public class MangaResponse
    {
        public int mal_id { get; set; }
        public string url { get; set; }
        public Images images { get; set; }
        public string title { get; set; }
        public string title_english { get; set; }
        public string title_japanese { get; set; }
        public List<string> title_synonyms { get; set; }
        public int favorites { get; set; }
        public string synopsis { get; set; }
        public double? score { get; set; }
        public List<Genres> genres { get; set; }
    }

    public class Genres
    {
        public string mal_id { get; set; }
        public string name { get; set; }
    }

    public class PaginationWrapper
    {
        public Pagination pagination { get; set; }
    }

    public class Pagination
    {
        public int current_page { get; set; }
        public Items items { get; set; }
    }

    public class Items
    {
        public int count { get; set; }
        public int total { get; set; }
        public int per_page { get; set; }
    }

    public class Voices
    {
        public Person person { get; set; }
        public string language { get; set; }
    }

    public class Person
    {
        public int mal_id { get; set; }
        public string name { get; set; }
    }

    #region 代餐 v1 (jikan-edge.lucas-hdo.workers.dev/v1)

    // /v1/manga and /v1/anime shared data model
    public class MangaV1Response
    {
        public int malId { get; set; }
        public string url { get; set; }
        public string imageUrl { get; set; }
        public string title { get; set; }
        public string synopsis { get; set; }
        public double? score { get; set; }
        public string type { get; set; }
    }

    public class AnimeV1Response
    {
        public int malId { get; set; }
        public string url { get; set; }
        public string imageUrl { get; set; }
        public string title { get; set; }
        public string synopsis { get; set; }
        public double? score { get; set; }
        public string type { get; set; }
        public int? episodes { get; set; }
    }

    // Pagination inside meta
    public class PaginationV1
    {
        public int page { get; set; }
        public int limit { get; set; }
        public int count { get; set; }
        public int? total { get; set; }   // CAN be null — handle before dividing
        public bool hasNextPage { get; set; }
    }

    public class MetaV1
    {
        public PaginationV1 pagination { get; set; }  // JSON key is "pagination", NOT "pagination1V1"
    }

    public class JikanMangaDevV1
    {
        public List<MangaV1Response> data { get; set; }
        public MetaV1 meta { get; set; }
    }

    public class JikanAnimeDevV1
    {
        public List<AnimeV1Response> data { get; set; }
        public MetaV1 meta { get; set; }
    }

    // Characters v1
    public class AnimeographyItem
    {
        public int malId { get; set; }
        public string title { get; set; }
    }

    public class CharactersV1Resopnse
    {
        public int malId { get; set; }
        public string name { get; set; }
        public string nameKanji { get; set; }
        public string imageUrl { get; set; }
        public int favorites { get; set; }
        public List<AnimeographyItem> animeography { get; set; }
    }

    public class TopCharactersV1Response
    {
        public List<CharactersV1Resopnse> data { get; set; }
    }

    #endregion
}
