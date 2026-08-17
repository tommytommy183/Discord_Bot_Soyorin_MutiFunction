using Discord;
using MusicBot2.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class WaifuPicsService
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://api.waifu.pics";

        public WaifuPicsService()
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

        // Get SFW image
        public async Task<Embed> GetSfwImageAsync(string category)
        {
            return await GetImageAsync("sfw", category);
        }

        // Get NSFW image
        public async Task<Embed> GetNsfwImageAsync(string category)
        {
            return await GetImageAsync("nsfw", category);
        }

        private async Task<Embed> GetImageAsync(string type, string category)
        {
            try
            {
                var url = $"{BaseUrl}/{type}/{category}";
                Console.WriteLine($"[WaifuPics] Request URL: {url}");
                Console.WriteLine($"[WaifuPics] Type: {type}, Category: {category}");

                var response = await _httpClient.GetAsync(url);
                Console.WriteLine($"[WaifuPics] Status Code: {response.StatusCode}");

                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[WaifuPics] Response: {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[WaifuPics] Error Response: {responseContent}");
                    return CreateErrorEmbed($"無法取得圖片 (HTTP {response.StatusCode})");
                }

                var result = JsonConvert.DeserializeObject<WaifuPicsResponse>(responseContent);
                Console.WriteLine($"[WaifuPics] Image URL: {result?.url}");

                if (result != null && !string.IsNullOrWhiteSpace(result.url))
                {
                    var isNsfw = type.Equals("nsfw", StringComparison.OrdinalIgnoreCase);

                    var embedBuilder = new EmbedBuilder()
                        .WithTitle($"{GetCategoryEmoji(category)} {GetCategoryDisplayName(category)}")
                        .WithColor(GetCategoryColor(category));

                    // Handle NSFW content
                    if (isNsfw)
                    {
                        embedBuilder.WithDescription($"?? **NSFW 內容**\n點擊查看: ||{result.url}||");
                    }
                    else
                    {
                        embedBuilder.WithImageUrl(result.url);
                    }

                    return embedBuilder.Build();
                }

                return CreateErrorEmbed("沒有找到圖片");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WaifuPics] Exception: {ex}");
                return CreateErrorEmbed($"發生錯誤: {ex.Message}");
            }
        }

        private string GetCategoryEmoji(string category)
        {
            return category.ToLower() switch
            {
                "waifu" => "??",
                "neko" => "??",
                "shinobu" => "??",
                "megumin" => "??",
                "bully" => "??",
                "cuddle" => "??",
                "cry" => "??",
                "hug" => "??",
                "awoo" => "??",
                "kiss" => "??",
                "lick" => "??",
                "pat" => "?",
                "smug" => "??",
                "bonk" => "??",
                "yeet" => "??",
                "blush" => "??",
                "smile" => "??",
                "wave" => "??",
                "highfive" => "??",
                "handhold" => "??",
                "nom" => "??",
                "bite" => "??",
                "glomp" => "??",
                "slap" => "??",
                "kill" => "??",
                "kick" => "??",
                "happy" => "??",
                "wink" => "??",
                "poke" => "??",
                "dance" => "??",
                "cringe" => "??",
                // NSFW
                "trap" => "??",
                "blowjob" => "??",
                _ => "???"
            };
        }

        private string GetCategoryDisplayName(string category)
        {
            return category.ToLower() switch
            {
                "waifu" => "Waifu (老婆)",
                "neko" => "Neko (貓娘)",
                "shinobu" => "Shinobu",
                "megumin" => "Megumin (惠惠)",
                "bully" => "Bully (霸凌)",
                "cuddle" => "Cuddle (擁抱)",
                "cry" => "Cry (哭泣)",
                "hug" => "Hug (抱抱)",
                "awoo" => "Awoo (狼嚎)",
                "kiss" => "Kiss (親親)",
                "lick" => "Lick (舔)",
                "pat" => "Pat (摸頭)",
                "smug" => "Smug (得意)",
                "bonk" => "Bonk (敲頭)",
                "yeet" => "Yeet (丟飛)",
                "blush" => "Blush (臉紅)",
                "smile" => "Smile (微笑)",
                "wave" => "Wave (揮手)",
                "highfive" => "High Five (擊掌)",
                "handhold" => "Hand Hold (牽手)",
                "nom" => "Nom (吃東西)",
                "bite" => "Bite (咬)",
                "glomp" => "Glomp (撲抱)",
                "slap" => "Slap (巴掌)",
                "kill" => "Kill (殺)",
                "kick" => "Kick (踢)",
                "happy" => "Happy (開心)",
                "wink" => "Wink (眨眼)",
                "poke" => "Poke (戳)",
                "dance" => "Dance (跳舞)",
                "cringe" => "Cringe (尷尬)",
                // NSFW
                "trap" => "NSFW - Trap",
                "blowjob" => "NSFW - Blowjob",
                _ => category
            };
        }

        private Color GetCategoryColor(string category)
        {
            return category.ToLower() switch
            {
                "waifu" => Color.Purple,
                "neko" => Color.Orange,
                "shinobu" => new Color(138, 43, 226),
                "megumin" => Color.Red,
                "bully" => Color.DarkRed,
                "cuddle" => new Color(255, 192, 203),
                "cry" => Color.Blue,
                "hug" => new Color(255, 182, 193),
                "kiss" => new Color(255, 20, 147),
                "pat" => new Color(255, 218, 185),
                "smug" => new Color(255, 215, 0),
                "happy" => Color.Gold,
                "smile" => new Color(255, 255, 0),
                _ => Color.Purple
            };
        }

        private Embed CreateErrorEmbed(string message)
        {
            if (message.Length > 4000)
            {
                message = message.Substring(0, 3997) + "...";
            }

            return new EmbedBuilder()
                .WithTitle("? 錯誤")
                .WithDescription(message)
                .WithColor(Color.Red)
                .Build();
        }
    }
}
