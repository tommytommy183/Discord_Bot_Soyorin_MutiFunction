using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class TavilySearchService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string API_URL = "https://api.tavily.com/search";

        public TavilySearchService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public async Task<string> SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            Console.WriteLine($"[Tavily] 搜尋: {query}");
            try
            {
                var body = new
                {
                    api_key = _apiKey,
                    query = query,
                    max_results = 4,
                    search_depth = "basic",
                    include_answer = true
                };

                var json = JsonSerializer.Serialize(body);
                var response = await _httpClient.PostAsync(
                    API_URL,
                    new StringContent(json, Encoding.UTF8, "application/json")
                );

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Tavily] API 錯誤 {response.StatusCode}: {err}");
                    return null;
                }

                var resultJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(resultJson);
                var root = doc.RootElement;

                var parts = new List<string>();

                // Tavily 的 answer 欄位（AI 整理過的直接答案）
                if (root.TryGetProperty("answer", out var ansEl))
                {
                    var ans = ansEl.GetString();
                    if (!string.IsNullOrWhiteSpace(ans))
                        parts.Add($"[摘要] {ans}");
                }

                // 各筆搜尋結果
                if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                {
                    int count = 0;
                    foreach (var r in results.EnumerateArray())
                    {
                        if (count >= 3) break;
                        var title = r.TryGetProperty("title", out var t) ? t.GetString() : null;
                        var content = r.TryGetProperty("content", out var c) ? c.GetString() : null;
                        if (string.IsNullOrWhiteSpace(content)) continue;

                        var snippet = content.Length > 300 ? content[..300] + "..." : content;
                        parts.Add(title != null ? $"【{title}】{snippet}" : snippet);
                        count++;
                    }
                }

                if (parts.Count == 0)
                {
                    Console.WriteLine($"[Tavily] 無結果");
                    return null;
                }

                var combined = string.Join("\n\n", parts);
                if (combined.Length > 1200) combined = combined[..1200] + "...";

                Console.WriteLine($"[Tavily] 找到 {parts.Count} 筆結果，字數: {combined.Length}");
                Console.WriteLine($"[Tavily 內容預覽]\n{combined[..Math.Min(600, combined.Length)]}");
                return combined;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tavily Error] {ex.Message}");
                return null;
            }
        }
    }
}
