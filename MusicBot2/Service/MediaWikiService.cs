using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace MusicBot2.Service
{
    public class MediaWikiService
    {
        private readonly HttpClient _httpClient;

        // 依語言嘗試順序：繁中 → 日文 → 英文
        private static readonly (string lang, string baseUrl)[] WikiEndpoints =
        {
            ("zh", "https://zh.wikipedia.org/w/api.php"),
            ("ja", "https://ja.wikipedia.org/w/api.php"),
            ("en", "https://en.wikipedia.org/w/api.php"),
        };

        private const int MaxExtractChars = 600;

        public MediaWikiService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SoyoBot/1.0 (Discord bot; contact: github.com/tommytommy183/Soyorin_Tense)");
        }

        /// <summary>
        /// 搜尋維基百科，回傳最相關結果的摘要文字。
        /// 依序嘗試繁中、日文、英文維基，找到非空摘要就回傳。
        /// </summary>
        public async Task<WikiSearchResult> SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return WikiSearchResult.Empty("查詢字串是空的");

            foreach (var (lang, baseUrl) in WikiEndpoints)
            {
                try
                {
                    // Step 1：search API 找最相關頁面標題
                    var searchTitle = await SearchTitleAsync(baseUrl, query);
                    if (string.IsNullOrEmpty(searchTitle)) continue;

                    // Step 2：用 extracts 取得摘要
                    var extract = await GetExtractAsync(baseUrl, searchTitle);
                    if (string.IsNullOrWhiteSpace(extract)) continue;

                    var pageUrl = $"https://{lang}.wikipedia.org/wiki/{Uri.EscapeDataString(searchTitle.Replace(' ', '_'))}";

                    return new WikiSearchResult
                    {
                        Found = true,
                        Title = searchTitle,
                        Extract = Truncate(extract, MaxExtractChars),
                        Lang = lang,
                        Url = pageUrl
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MediaWiki] {lang} 查詢失敗: {ex.Message}");
                }
            }

            return WikiSearchResult.Empty($"沒有找到「{query}」的相關資料");
        }

        private async Task<string> SearchTitleAsync(string baseUrl, string query)
        {
            var url = $"{baseUrl}?action=query&list=search&srsearch={HttpUtility.UrlEncode(query)}&srlimit=1&format=json&utf8=1";
            var json = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("query", out var q) &&
                q.TryGetProperty("search", out var results) &&
                results.GetArrayLength() > 0)
            {
                return results[0].GetProperty("title").GetString();
            }
            return null;
        }

        private async Task<string> GetExtractAsync(string baseUrl, string title)
        {
            var url = $"{baseUrl}?action=query&titles={HttpUtility.UrlEncode(title)}&prop=extracts&exintro=1&explaintext=1&exsectionformat=plain&format=json&utf8=1";
            var json = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("query", out var q) &&
                q.TryGetProperty("pages", out var pages))
            {
                foreach (var page in pages.EnumerateObject())
                {
                    if (page.Value.TryGetProperty("extract", out var extract))
                        return extract.GetString();
                }
            }
            return null;
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s;
            var cut = s.Substring(0, maxLen);
            var lastNewline = cut.LastIndexOf('\n');
            if (lastNewline > maxLen / 2) cut = cut.Substring(0, lastNewline);
            return cut.TrimEnd() + "…";
        }
    }

    public class WikiSearchResult
    {
        public bool Found { get; set; }
        public string Title { get; set; }
        public string Extract { get; set; }
        public string Lang { get; set; }
        public string Url { get; set; }
        public string ErrorMessage { get; set; }

        public static WikiSearchResult Empty(string reason) => new()
        {
            Found = false,
            ErrorMessage = reason
        };
    }
}
