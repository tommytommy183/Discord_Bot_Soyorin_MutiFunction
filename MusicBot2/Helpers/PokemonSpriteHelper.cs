using MusicBot2.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBot2.Helpers
{
    /// <summary>
    /// 統一 Pokemon 圖片來源：
    ///   - 抓到 / 圖鑑展示（ImageUrl）     → 官方立繪 official-artwork
    ///   - 對戰正面 / 背面（Front_GIF / Back_GIF）→ showdown 動態 GIF
    /// 以 HEAD 確認 URL 真的存在（結果快取），只有圖真的不存在時才 fallback。
    /// </summary>
    public static class PokemonSpriteHelper
    {
        /// <summary>
        /// 每次規則變更就 +1，舊資料（SpriteVersion 較小）會在讀取時自動重算圖片。
        /// </summary>
        public const int CurrentSpriteVersion = 1;

        private const string RawBase = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/";
        private const string ShowdownBase = "https://play.pokemonshowdown.com/sprites/";

        private static readonly HttpClient _http = CreateClient();
        private static readonly ConcurrentDictionary<string, bool> _existsCache = new();
        private static readonly ConcurrentDictionary<int, string> _apiNameCache = new();
        // 同時對外 HEAD 的數量上限，避免一次遷移幾百隻時被 GitHub 限流
        private static readonly SemaphoreSlim _throttle = new(8, 8);

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("SoyoBot/1.0");
            return c;
        }

        public static string ArtworkUrl(int id, bool shiny)
            => $"{RawBase}other/official-artwork/{(shiny ? "shiny/" : "")}{id}.png";

        public static string ShowdownGifUrl(int id, bool shiny, bool back)
            => $"{RawBase}other/showdown/{(back ? "back/" : "")}{(shiny ? "shiny/" : "")}{id}.gif";

        /// <summary>
        /// 確認圖片 URL 是否存在。成功/404 會快取；網路錯誤回傳 null（不快取，代表「不確定」）。
        /// </summary>
        public static async Task<bool?> ExistsAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (_existsCache.TryGetValue(url, out var cached)) return cached;

            await _throttle.WaitAsync();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, url);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (resp.StatusCode == HttpStatusCode.MethodNotAllowed)
                {
                    using var getResp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    return Cache(url, getResp);
                }
                return Cache(url, resp);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sprite] HEAD 失敗 {url}: {ex.Message}");
                return null;
            }
            finally
            {
                _throttle.Release();
            }
        }

        private static bool? Cache(string url, HttpResponseMessage resp)
        {
            if (resp.IsSuccessStatusCode)
            {
                _existsCache[url] = true;
                return true;
            }
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                _existsCache[url] = false;
                return false;
            }
            return null; // 429 / 5xx：不確定
        }

        /// <summary>PokeAPI 的英文 pokemon 名稱（例：charizard-mega-x），供 showdown 備援用</summary>
        private static async Task<string> GetApiNameAsync(int id)
        {
            if (_apiNameCache.TryGetValue(id, out var name)) return name;
            try
            {
                var json = await _http.GetStringAsync($"https://pokeapi.co/api/v2/pokemon/{id}");
                name = JObject.Parse(json)["name"]?.ToString();
                if (!string.IsNullOrEmpty(name)) _apiNameCache[id] = name;
                return name;
            }
            catch { return null; }
        }

        /// <summary>
        /// PokeAPI 名稱 → Pokemon Showdown 檔名候選。
        /// charizard-mega-x → charizard-megax；tapu-koko → tapukoko；deoxys-normal → deoxys
        /// </summary>
        private static IEnumerable<string> ShowdownNameCandidates(string apiName, int id)
        {
            if (string.IsNullOrWhiteSpace(apiName)) yield break;
            var n = apiName.ToLowerInvariant().Replace(".", "").Replace("'", "").Replace(":", "");
            var dash = n.IndexOf('-');
            if (dash > 0)
                yield return n[..(dash + 1)] + n[(dash + 1)..].Replace("-", "");
            yield return n.Replace("-", "");
            // 只有一般物種（非特殊型態 id 10000+）才允許退回基本名，避免型態圖對不上
            if (dash > 0 && id <= 1025)
                yield return n[..dash];
        }

        /// <summary>官方立繪；不存在時回傳 null</summary>
        public static async Task<string> ResolveArtworkAsync(int id, bool shiny)
        {
            if (id <= 0) return null;
            var url = ArtworkUrl(id, shiny);
            var ok = await ExistsAsync(url);
            return ok == false ? null : url; // 不確定時先相信它存在
        }

        /// <summary>對戰用 GIF；PokeAPI repo 沒有時改找 Pokemon Showdown 官方 sprite；都不存在才回 null</summary>
        public static async Task<string> ResolveBattleGifAsync(int id, bool shiny, bool back)
        {
            if (id <= 0) return null;
            var primary = ShowdownGifUrl(id, shiny, back);
            var ok = await ExistsAsync(primary);
            if (ok != false) return primary;

            var apiName = await GetApiNameAsync(id);
            var folder = "ani" + (back ? "-back" : "") + (shiny ? "-shiny" : "");
            foreach (var cand in ShowdownNameCandidates(apiName, id))
            {
                var url = $"{ShowdownBase}{folder}/{cand}.gif";
                if (await ExistsAsync(url) == true) return url;
            }
            return null;
        }

        /// <summary>
        /// 依統一規則重算一隻 Pokemon 的圖片欄位。圖真的不存在時保留原值。
        /// 回傳是否有任何欄位變更。
        /// </summary>
        public static async Task<bool> NormalizeAsync(PokeGamePokemon p)
        {
            if (p == null || p.Id <= 0 || p.IsBattleFormChanged) return false;

            var artTask = ResolveArtworkAsync(p.Id, p.isShiny);
            var frontTask = ResolveBattleGifAsync(p.Id, p.isShiny, back: false);
            var backTask = ResolveBattleGifAsync(p.Id, p.isShiny, back: true);
            await Task.WhenAll(artTask, frontTask, backTask);

            bool changed = false;
            void Set(Func<string> get, Action<string> set, string value)
            {
                if (!string.IsNullOrEmpty(value) && get() != value) { set(value); changed = true; }
            }

            Set(() => p.ImageUrl, v => p.ImageUrl = v, artTask.Result);
            Set(() => p.Front_GIF, v => p.Front_GIF = v, frontTask.Result);
            Set(() => p.Back_GIF, v => p.Back_GIF = v, backTask.Result);

            // 舊資料可能存了不存在的 GIF（壞圖）→ 清掉，讓對戰 fallback 到存在的圖
            if (string.IsNullOrEmpty(frontTask.Result) && !string.IsNullOrEmpty(p.Front_GIF) && await ExistsAsync(p.Front_GIF) == false)
            { p.Front_GIF = null; changed = true; }
            if (string.IsNullOrEmpty(backTask.Result) && !string.IsNullOrEmpty(p.Back_GIF) && await ExistsAsync(p.Back_GIF) == false)
            { p.Back_GIF = null; changed = true; }

            if (p.SpriteVersion != CurrentSpriteVersion) { p.SpriteVersion = CurrentSpriteVersion; changed = true; }
            return changed;
        }

        /// <summary>只處理還沒套用目前規則的 Pokemon（SpriteVersion 較舊）</summary>
        public static async Task<bool> NormalizeIfOutdatedAsync(IEnumerable<PokeGamePokemon> list)
        {
            if (list == null) return false;
            var outdated = list.Where(p => p != null && p.SpriteVersion < CurrentSpriteVersion).ToList();
            if (outdated.Count == 0) return false;
            var results = await Task.WhenAll(outdated.Select(NormalizeAsync));
            return results.Any(r => r);
        }

        /// <summary>對戰正面圖：GIF 優先，沒有才用立繪</summary>
        public static string BattleFront(PokeGamePokemon p)
            => p == null ? null : (!string.IsNullOrEmpty(p.Front_GIF) ? p.Front_GIF : p.ImageUrl);

        /// <summary>
        /// 對戰背面圖：背面 GIF → 正面 GIF → 立繪 → 背面像素圖（最後手段）。
        /// 沒有 GIF 時寧可用立繪也不混像素圖，畫風才一致。
        /// </summary>
        public static string BattleBack(PokeGamePokemon p)
        {
            if (p == null) return null;
            if (!string.IsNullOrEmpty(p.Back_GIF)) return p.Back_GIF;
            if (!string.IsNullOrEmpty(p.Front_GIF)) return p.Front_GIF;
            if (!string.IsNullOrEmpty(p.ImageUrl)) return p.ImageUrl;
            return p.Back_ImageUrl;
        }
    }
}
