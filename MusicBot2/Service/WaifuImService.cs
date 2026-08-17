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
        public async Task<Embed> GetRandomImageAsync(
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
                    queryParams.Add($"includedTags={Uri.EscapeDataString(includedTags)}");

                if (!string.IsNullOrEmpty(excludedTags))
                    queryParams.Add($"excludedTags={Uri.EscapeDataString(excludedTags)}");

                if (isNsfw.HasValue)
                    queryParams.Add($"IsNsfw={isNsfw.Value.ToString().ToLower()}");

                if (!string.IsNullOrEmpty(orientation))
                    queryParams.Add($"orientation={orientation}");

                if (!string.IsNullOrEmpty(orderBy))
                    queryParams.Add($"order_by={orderBy}");


                queryParams.Add($"IsAnimated={isAnimated.ToString().ToLower()}");

                var url = $"{BaseUrl}/images?" + string.Join("&", queryParams);
                Console.WriteLine($"[WaifuIm] Request URL: {url}");

                var response = await _httpClient.GetAsync(url);
                Console.WriteLine($"[WaifuIm] Status Code: {response.StatusCode}");

                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[WaifuIm] Response Length: {responseContent.Length}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[WaifuIm] Error Response: {responseContent}");
                    return CreateErrorEmbed($"無法取得圖片 (HTTP {response.StatusCode})");
                }

                var result = JsonConvert.DeserializeObject<WaifuImResponse>(responseContent);
                Console.WriteLine($"[WaifuIm] Images Count: {result?.items?.Count ?? 0}");

                if (result?.items != null && result.items.Count > 0)
                {
                    var image = result.items[0];
                    Console.WriteLine($"[WaifuIm] Image URL: {image.url}");
                    Console.WriteLine($"[WaifuIm] Is NSFW: {image.isNsfw}");

                    var embedBuilder = new EmbedBuilder()
                        .WithColor(ParseColor(image.dominantColor));

                    // Add tags to title
                    var tagNames = image.tags?.Select(t => t.name).ToList() ?? new List<string>();
                    var title = tagNames.Count > 0 ? string.Join(", ", tagNames.Take(3)) : "Waifu Image";
                    embedBuilder.WithTitle($"🖼️ {title}");

                    // Handle NSFW content
                    if (image.isNsfw)
                    {
                        embedBuilder.WithDescription($"⚠️ **NSFW 內容**\\n點擊查看: ||{image.url}||");
                    }
                    else
                    {
                        embedBuilder.WithImageUrl(image.url);
                    }

                    // Add footer with source
                    if (!string.IsNullOrEmpty(image.source))
                    {
                        embedBuilder.WithFooter($"來源: {image.source}");
                    }

                    return embedBuilder.Build();
                }

                return CreateErrorEmbed("沒有找到符合條件的圖片");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WaifuIm] Exception: {ex}");
                return CreateErrorEmbed($"發生錯誤: {ex.Message}");
            }
        }

        // Get image by specific tag
        public async Task<Embed> GetImageByTagAsync(string tag, bool isNsfw = false, bool isAnimated = false)
        {
            return await GetRandomImageAsync(includedTags: tag, isNsfw: isNsfw, isAnimated: isAnimated);
        }

        // Get multiple tags
        public async Task<Embed> GetImageByMultipleTagsAsync(string[] tags, bool isNsfw = false, bool isAnimated = false)
        {
            var tagsString = string.Join(",", tags);
            return await GetRandomImageAsync(includedTags: tagsString, isNsfw: isNsfw, isAnimated: isAnimated);
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
