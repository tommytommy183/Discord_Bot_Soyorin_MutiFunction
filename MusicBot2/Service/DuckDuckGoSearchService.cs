using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace MusicBot2.Service
{
    /// <summary>
    /// 使用 DuckDuckGo HTML 搜尋取得關鍵資訊，供 AI 參考。
    /// </summary>
    public class DuckDuckGoSearchService
    {
        private readonly HttpClient _httpClient;

        // 觸發搜尋意圖的關鍵字
        private static readonly string[] SearchTriggerKeywords =
        {
            "查詢", "搜尋", "搜索", "上網查", "上網搜", "搜一下", "查一下", "搜搜",
            "查查", "查看", "找找", "幫我查", "幫我搜", "找資料", "查資料", "找一下",
            "是什麼", "是誰", "什麼東西", "幫我找", "告訴我", "查清楚", "查看一下",
            "最新", "新聞", "最新的", "現在"
        };

        public DuckDuckGoSearchService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        /// <summary>
        /// 判斷訊息是否含搜尋意圖，有的話回傳搜尋關鍵字，否則回傳 null
        /// </summary>
        public string DetectSearchIntent(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;
            if (message.Length < 4) return null;

            // 檢查是否包含觸發關鍵字
            bool hasTrigger = SearchTriggerKeywords.Any(k => message.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (!hasTrigger) return null;

            // 優先抓括號/引號內的內容
            var bracketMatch = Regex.Match(message, @"[【「『\[""'](.+?)[】」』\]""']");
            if (bracketMatch.Success)
            {
                var q = bracketMatch.Groups[1].Value.Trim();
                if (q.Length >= 2) return q;
            }

            // 去掉觸發詞和常見語氣詞，剩餘的當作查詢
            var cleaned = message;
            foreach (var kw in SearchTriggerKeywords.OrderByDescending(k => k.Length))
                cleaned = cleaned.Replace(kw, " ", StringComparison.OrdinalIgnoreCase);

            cleaned = Regex.Replace(cleaned, @"(幫我|告訴我?|一下|查查|找找|確認|告訴|soyo|需要|大概|幫我找|可以|請問|嗎|呢|吧|啊|喔|耶|哦|ㄟ|誒|欸)", "", RegexOptions.IgnoreCase);
            cleaned = cleaned.Trim(' ', '?', '？', '!', '！', '。', ',', '，', '、');
            // 移除多餘空白
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();

            return cleaned.Length >= 2 ? cleaned : null;
        }

        /// <summary>
        /// 使用 DuckDuckGo Instant Answer API 搜尋
        /// </summary>
        public async Task<string> SearchAsync(string query)
        {
            try
            {
                Console.WriteLine($"[DuckDuckGo] 搜尋: {query}");

                // 使用 DuckDuckGo Instant Answer API
                var encodedQuery = HttpUtility.UrlEncode(query);
                var url = $"https://api.duckduckgo.com/?q={encodedQuery}&format=json&no_html=1&skip_disambig=1";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[DuckDuckGo] API 回應錯誤: {response.StatusCode}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var results = new List<string>();

                // Abstract (主要摘要)
                var abstractText = root.TryGetProperty("Abstract", out var abs) ? abs.GetString() : null;
                if (!string.IsNullOrWhiteSpace(abstractText))
                {
                    var source = root.TryGetProperty("AbstractSource", out var src) ? src.GetString() : "";
                    results.Add($"[{source}] {abstractText}");
                }

                // Answer (直接答案)
                var answer = root.TryGetProperty("Answer", out var ans) ? ans.GetString() : null;
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    results.Add($"[答案] {answer}");
                }

                // RelatedTopics (相關主題，最多 3 個)
                if (root.TryGetProperty("RelatedTopics", out var topics) && topics.ValueKind == JsonValueKind.Array)
                {
                    int count = 0;
                    foreach (var topic in topics.EnumerateArray())
                    {
                        if (count >= 3) break;
                        var text = topic.TryGetProperty("Text", out var t) ? t.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            results.Add(text);
                            count++;
                        }
                    }
                }

                if (results.Count == 0)
                {
                    Console.WriteLine($"[DuckDuckGo] Instant Answer API 無結果，改用 Lite");
                    return await SearchLiteAsync(query);
                }

                var combined = string.Join("\n", results);
                // 截斷過長
                if (combined.Length > 800) combined = combined.Substring(0, 800) + "...";

                Console.WriteLine($"[DuckDuckGo] 找到 {results.Count} 筆結果");
                return combined;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DuckDuckGo Error] {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 備援：使用 DuckDuckGo Lite HTML 爬搜尋結果
        /// </summary>
        private async Task<string> SearchLiteAsync(string query)
        {
            try
            {
                var encodedQuery = HttpUtility.UrlEncode(query);
                var url = $"https://lite.duckduckgo.com/lite/?q={encodedQuery}";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var html = await response.Content.ReadAsStringAsync();

                // 解析：嘗試多個 selector
                var snippets = new List<string>();

                // 嘗試 class="result-snippet"
                var matches = Regex.Matches(html, @"<td[^>]*class=""result-snippet""[^>]*>(.*?)</td>", RegexOptions.Singleline);
                foreach (Match match in matches)
                {
                    if (snippets.Count >= 3) break;
                    var text = Regex.Replace(match.Groups[1].Value, @"<[^>]+>", "").Trim();
                    text = HttpUtility.HtmlDecode(text);
                    if (!string.IsNullOrWhiteSpace(text) && text.Length > 20)
                        snippets.Add(text);
                }

                // fallback：抓任何 <td> 內超過 50 字的文字
                if (snippets.Count == 0)
                {
                    var tdMatches = Regex.Matches(html, @"<td[^>]*>(.*?)</td>", RegexOptions.Singleline);
                    foreach (Match match in tdMatches)
                    {
                        if (snippets.Count >= 3) break;
                        var text = Regex.Replace(match.Groups[1].Value, @"<[^>]+>", "").Trim();
                        text = HttpUtility.HtmlDecode(text);
                        if (!string.IsNullOrWhiteSpace(text) && text.Length > 50 && text.Length < 500)
                            snippets.Add(text);
                    }
                }

                if (snippets.Count == 0)
                {
                    Console.WriteLine($"[DuckDuckGo Lite] 無結果（HTML 長度: {html.Length}）");
                    return null;
                }

                var combined = string.Join("\n", snippets);
                if (combined.Length > 800) combined = combined.Substring(0, 800) + "...";

                Console.WriteLine($"[DuckDuckGo Lite] 找到 {snippets.Count} 筆結果");
                return combined;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DuckDuckGo Lite Error] {ex.Message}");
                return null;
            }
        }
    }
}
