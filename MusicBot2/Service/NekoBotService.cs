using Discord;
using Discord.WebSocket;
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
    public class NekoBotService
    {
        private readonly HttpClient _httpClient;

        public NekoBotService()
        {
            _httpClient = new HttpClient();
        }

        // Image Generation - Ship
        public async Task<Embed> GetShipImageAsync(string user1Url, string user2Url)
        {
            try
            {
                var url = $"https://nekobot.xyz/api/imagegen?type=ship&user1={Uri.EscapeDataString(user1Url)}&user2={Uri.EscapeDataString(user2Url)}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return CreateErrorEmbed("無法取得 Ship 圖片");

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<NekoBotImageGenResponse>(responseContent);

                if (result?.success == true && !string.IsNullOrWhiteSpace(result.message))
                {
                    return new EmbedBuilder()
                        .WithTitle("💕 Ship Result")
                        .WithImageUrl(result.message)
                        .WithColor(Color.Purple)
                        .Build();
                }

                return CreateErrorEmbed("無法取得 Ship 圖片");
            }
            catch (Exception ex)
            {
                return CreateErrorEmbed($"發生錯誤: {ex.Message}");
            }
        }
        
        // Image Generation - Who Would Win
        public async Task<Embed> GetWhoWouldWinImageAsync(string user1Url, string user2Url)
        {
            try
            {
                var url = $"https://nekobot.xyz/api/imagegen?type=whowouldwin&user1={Uri.EscapeDataString(user1Url)}&user2={Uri.EscapeDataString(user2Url)}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return CreateErrorEmbed("無法取得 Who Would Win 圖片");

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<NekoBotImageGenResponse>(responseContent);

                if (result?.success == true && !string.IsNullOrWhiteSpace(result.message))
                {
                    return new EmbedBuilder()
                        .WithTitle("⚔️ Who Would Win?")
                        .WithImageUrl(result.message)
                        .WithColor(Color.Gold)
                        .Build();
                }

                return CreateErrorEmbed("無法取得 Who Would Win 圖片");
            }
            catch (Exception ex)
            {
                return CreateErrorEmbed($"發生錯誤: {ex.Message}");
            }
        }

        // Image API - All Types
        public async Task<Embed> GetImageAsync(string type)
        {
            try
            {
                var url = $"https://nekobot.xyz/api/image?type={type}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return CreateErrorEmbed($"無法取得 {type} 圖片");

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<NekoBotImageResponse>(responseContent);

                if (result?.success == true && !string.IsNullOrWhiteSpace(result.message))
                {
                    var isNsfw = IsNsfwType(type);

                    var embedBuilder = new EmbedBuilder()
                        .WithTitle($"{GetTypeEmoji(type)} {GetTypeDisplayName(type)}")
                        .WithColor(new Color((uint)result.color));

                    // Add spoiler for NSFW content
                    if (isNsfw)
                    {
                        embedBuilder.WithDescription($"⚠️ **NSFW 內容** - 請點擊下方連結查看\n||{result.message}||");
                    }
                    else
                    {
                        embedBuilder.WithImageUrl(result.message);
                    }

                    return embedBuilder.Build();
                }

                return CreateErrorEmbed($"無法取得 {type} 圖片");
            }
            catch (Exception ex)
            {
                return CreateErrorEmbed($"發生錯誤: {ex.Message}");
            }
        }

        private bool IsNsfwType(string type)
        {
            var nsfwTypes = new HashSet<string>
            {
                "hass", "hmidriff", "pgif", "4k", "hentai", "holo", 
                "hkitsune", "kemonomimi", "hanal", "gonewild", "kanna", 
                "ass", "pussy", "thigh", "hthigh", "paizuri", "tentacle", 
                "boobs", "hboobs"
            };
            return nsfwTypes.Contains(type.ToLower());
        }

        private string GetTypeEmoji(string type)
        {
            return type.ToLower() switch
            {
                "neko" => "🐱",
                "kitsune" => "🦊",
                "waifu" => "💖",
                "husbando" => "💙",
                "gecg" => "🎮",
                "avatar" => "👤",
                "wallpaper" => "🖼️",
                "foxgirl" => "🦊",
                "lizard" => "🦎",
                "goose" => "🪿",
                "coffee" => "☕",
                "food" => "🍔",
                _ => "🖼️"
            };
        }

        private string GetTypeDisplayName(string type)
        {
            return type.ToLower() switch
            {
                "neko" => "Neko (貓娘)",
                "kitsune" => "Kitsune (狐娘)",
                "waifu" => "Waifu (老婆)",
                "husbando" => "Husbando (老公)",
                "gecg" => "Game Character",
                "avatar" => "Avatar (頭像)",
                "wallpaper" => "Wallpaper (桌布)",
                "foxgirl" => "Fox Girl (狐娘)",
                "lizard" => "Lizard (蜥蜴)",
                "goose" => "Goose (鵝)",
                "coffee" => "Coffee (咖啡)",
                "food" => "Food (食物)",
                "hass" => "NSFW - Ass",
                "hmidriff" => "NSFW - Midriff",
                "pgif" => "NSFW - GIF",
                "4k" => "NSFW - 4K",
                "hentai" => "NSFW - Hentai",
                "holo" => "NSFW - Holo",
                "hkitsune" => "NSFW - Kitsune",
                "kemonomimi" => "NSFW - Kemonomimi",
                "hanal" => "NSFW - Anal",
                "gonewild" => "NSFW - Gonewild",
                "kanna" => "NSFW - Kanna",
                "ass" => "NSFW - Ass",
                "pussy" => "NSFW - Pussy",
                "thigh" => "NSFW - Thigh",
                "hthigh" => "NSFW - Thigh",
                "paizuri" => "NSFW - Paizuri",
                "tentacle" => "NSFW - Tentacle",
                "boobs" => "NSFW - Boobs",
                "hboobs" => "NSFW - Boobs",
                _ => type
            };
        }

        private Embed CreateErrorEmbed(string message)
        {
            return new EmbedBuilder()
                .WithTitle("❌ 錯誤")
                .WithDescription(message)
                .WithColor(Color.Red)
                .Build();
        }
    }
}
