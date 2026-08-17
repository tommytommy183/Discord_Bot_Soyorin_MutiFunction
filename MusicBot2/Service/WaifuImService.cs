using Discord;
using MusicBot2.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace MusicBot2.Service
{
    public class WaifuImService
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://api.waifu.im";

        public WaifuImService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        // Get random image with optional filters
        public async Task<string> GetRandomImageAsync(
            string includedTags = null,
            string excludedTags = null,
            bool? isNsfw = null,
            string orientation = null,
            string orderBy = null,
            bool isAnimated = false
            )
        {
            try
            {
                var queryParams = new List<string>();

                if (!string.IsNullOrEmpty(includedTags))
                    queryParams.Add($"included_tags={Uri.EscapeDataString(includedTags)}");

                if (!string.IsNullOrEmpty(excludedTags))
                    queryParams.Add($"excluded_tags={Uri.EscapeDataString(excludedTags)}");

                if (isNsfw.HasValue)
                    queryParams.Add($"is_nsfw={isNsfw.Value.ToString().ToLower()}");

                if (!string.IsNullOrEmpty(orientation))
                    queryParams.Add($"orientation={orientation}");

                if (!string.IsNullOrEmpty(orderBy))
                    queryParams.Add($"order_by={orderBy}");

                queryParams.Add($"is_animated={isAnimated.ToString().ToLower()}");

                var url = $"{BaseUrl}/search?" + string.Join("&", queryParams);
                Console.WriteLine($"[WaifuIm] Request URL: {url}");

                var response = await _httpClient.GetAsync(url);
                Console.WriteLine($"[WaifuIm] Status Code: {response.StatusCode}");

                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[WaifuIm] Response Length: {responseContent.Length}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[WaifuIm] Error Response: {responseContent}");
                    return null;
                }

                var result = JsonConvert.DeserializeObject<WaifuImResponse>(responseContent);
                Console.WriteLine($"[WaifuIm] Images Count: {result?.items?.Count ?? 0}");

                if (result?.items != null && result.items.Count > 0)
                {
                    var image = result.items[0];
                    Console.WriteLine($"[WaifuIm] Image URL: {image.url}");
                    Console.WriteLine($"[WaifuIm] Is NSFW: {image.isNsfw}");

                    return image.url;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WaifuIm] Exception: {ex}");
                return null;
            }
        }

        // Get image by specific tag
        public async Task<string> GetImageByTagAsync(string tag, bool isNsfw = false, bool isAnimated = false)
        {
            return await GetRandomImageAsync(includedTags: tag, isNsfw: isNsfw, isAnimated: isAnimated);
        }

        // Get multiple tags
        public async Task<string> GetImageByMultipleTagsAsync(string[] tags, bool isNsfw = false, bool isAnimated = false)
        {
            var tagsString = string.Join(",", tags);
            return await GetRandomImageAsync(includedTags: tagsString, isNsfw: isNsfw, isAnimated: isAnimated);
        }

        // Get all available tags
        public async Task<Embed> GetAllTagsAsync()
        {
            try
            {
                var url = $"{BaseUrl}/tags";
                Console.WriteLine($"[WaifuIm] Getting tags from: {url}");

                var response = await _httpClient.GetAsync(url);
                Console.WriteLine($"[WaifuIm] Status Code: {response.StatusCode}");

                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[WaifuIm] Error Response: {responseContent}");
                    return CreateErrorEmbed($"無法取得標籤列表 (HTTP {response.StatusCode})");
                }

                var result = JsonConvert.DeserializeObject<WaifuImTagsResponse>(responseContent);

                if (result?.versatile == null)
                {
                    return CreateErrorEmbed("無法解析標籤資料");
                }

                var embedBuilder = new EmbedBuilder()
                    .WithTitle("🏷️ Waifu.im 可用標籤")
                    .WithColor(Color.Purple)
                    .WithDescription("使用 `/waifu-自訂` 指令時可以輸入以下標籤名稱：");

                // 分類顯示標籤
                foreach (var category in result.versatile)
                {
                    var sfwTags = category.Value.Where(t => !t.is_nsfw).Select(t => t.name).ToList();
                    var nsfwTags = category.Value.Where(t => t.is_nsfw).Select(t => t.name).ToList();

                    if (sfwTags.Count > 0)
                    {
                        var tagList = string.Join(", ", sfwTags.Take(20));
                        if (sfwTags.Count > 20)
                            tagList += $" ... (+{sfwTags.Count - 20} more)";

                        embedBuilder.AddField($"✅ {category.Key} (SFW)", tagList, false);
                    }

                    if (nsfwTags.Count > 0)
                    {
                        var tagList = string.Join(", ", nsfwTags.Take(20));
                        if (nsfwTags.Count > 20)
                            tagList += $" ... (+{nsfwTags.Count - 20} more)";

                        embedBuilder.AddField($"🔞 {category.Key} (NSFW)", tagList, false);
                    }
                }

                embedBuilder.WithFooter($"共 {result.versatile.Values.SelectMany(v => v).Count()} 個標籤");

                return embedBuilder.Build();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WaifuIm] Exception: {ex}");
                return CreateErrorEmbed($"發生錯誤: {ex.Message}");
            }
        }

        private Color ParseColor(string hexColor)
        {
            try
            {
                if (string.IsNullOrEmpty(hexColor))
                    return Color.Purple;

                // Remove # if present
                hexColor = hexColor.TrimStart('#');

                if (hexColor.Length == 6)
                {
                    var r = Convert.ToInt32(hexColor.Substring(0, 2), 16);
                    var g = Convert.ToInt32(hexColor.Substring(2, 2), 16);
                    var b = Convert.ToInt32(hexColor.Substring(4, 2), 16);
                    return new Color(r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WaifuIm] Color parse error: {ex.Message}");
            }

            return Color.Purple;
        }

        private Embed CreateErrorEmbed(string message)
        {
            if (message.Length > 4000)
            {
                message = message.Substring(0, 3997) + "...";
            }

            return new EmbedBuilder()
                .WithTitle("❌ 錯誤")
                .WithDescription(message)
                .WithColor(Color.Red)
                .Build();
        }
    }
}
