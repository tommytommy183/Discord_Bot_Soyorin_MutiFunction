using Discord;
using Discord.WebSocket;
using MusicBot2.Helpers;
using MusicBot2.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class JikanAnimeService
    {
        private readonly HttpClient _httpClient;
        private const string API_BASE_URL = "https://api.jikan.moe/v4";

        public JikanAnimeService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<(ComponentBuilder component, Embed embed)> StartGameAsync(string mode, bool isTop)
        {
            try
            {
                if (mode.ToLower() == "cta" || mode.ToLower() == "角色猜動畫")
                {
                    return await StartCharacterToAnimeGameAsync(isTop);
                }
                else if (mode.ToLower() == "ctc" || mode.ToLower() == "角色猜角色")
                {
                    return await StartCharacterToCharacterGameAsync(isTop);
                }
                else
                {
                    return CommonHelper.BuildErrorResponse("模式錯誤！請選擇 'cta'(角色猜動畫) 或 'ctc'(角色猜角色)");
                }
            }
            catch (Exception ex)
            {
                return CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}");
            }
        }

        // 角色猜動畫模式：顯示角色，猜測來自哪部動畫
        private async Task<(ComponentBuilder component, Embed embed)> StartCharacterToAnimeGameAsync(bool isTop)
        {
            // 獲取角色資料
            var charaResponse = await GetCharacterAsync(isTop);
            if (charaResponse == null)
            {
                return CommonHelper.BuildErrorResponse("無法獲取角色資料");
            }

            // 獲取角色的動畫資訊（1個正確 + 3個隨機）
            var animeOptions = await GetAnimeOptionsForCharacterAsync(charaResponse.mal_id);
            if (animeOptions == null || animeOptions.Count == 0)
            {
                return CommonHelper.BuildErrorResponse("無法取得角色的動畫資訊，請重試");
            }

            var correctAnime = animeOptions[0]; // 第一個是正確答案
            var component = BuildAnimeOptionsComponent(animeOptions, correctAnime.mal_id);
            var embed = BuildCharacterToAnimeEmbed(charaResponse, correctAnime);

            return (component, embed);
        }

        // 角色猜角色模式：顯示角色圖片，猜測角色名字
        private async Task<(ComponentBuilder component, Embed embed)> StartCharacterToCharacterGameAsync(bool isTop)
        {
            // 獲取正確答案角色
            var correctCharacter = await GetCharacterAsync(isTop);
            if (correctCharacter == null)
            {
                return CommonHelper.BuildErrorResponse("無法獲取角色資料");
            }

            // 獲取其他3個隨機角色作為選項
            var characterOptions = new List<CharactersResopnse> { correctCharacter };

            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(1000); // API 速率限制
                var randomChar = await GetCharacterAsync(false); // 隨機角色
                if (randomChar != null)
                {
                    characterOptions.Add(randomChar);
                }
            }

            // 打亂順序
            characterOptions = characterOptions.OrderBy(x => Guid.NewGuid()).ToList();

            var component = BuildCharacterOptionsComponent(characterOptions, correctCharacter.mal_id);
            var embed = BuildCharacterToCharacterEmbed(correctCharacter);

            return (component, embed);
        }

        // 獲取角色（支援熱門或隨機）
        private async Task<CharactersResopnse> GetCharacterAsync(bool isTop)
        {
            try
            {
                if (isTop)
                {
                    Random random = new Random();
                    int page = random.Next(1, 51);
                    string url = $"{API_BASE_URL}/top/characters?page={page}";
                    var response = await _httpClient.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                        return null;

                    var responseContent = await response.Content.ReadAsStringAsync();
                    var charaResponses = JsonConvert.DeserializeObject<TopCharactersResponse>(responseContent);

                    int index = random.Next(0, charaResponses.data.Count);
                    return charaResponses.data[index];
                }
                else
                {
                    string url = $"{API_BASE_URL}/random/characters";
                    var response = await _httpClient.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                        return null;

                    var responseContent = await response.Content.ReadAsStringAsync();
                    var wrapper = JsonConvert.DeserializeObject<CharacterWrapper>(responseContent);
                    return wrapper.data;
                }
            }
            catch
            {
                return null;
            }
        }

        // 獲取角色的動畫選項（1個正確 + 3個隨機）
        private async Task<List<AnimeResponse>> GetAnimeOptionsForCharacterAsync(int characterId)
        {
            try
            {
                // 獲取角色出現的動畫
                string url = $"{API_BASE_URL}/characters/{characterId}/anime";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var animeData = JsonConvert.DeserializeObject<CharacterAnimeResponse>(responseContent);

                if (animeData.data == null || animeData.data.Count == 0)
                {
                    return null;
                }

                // 取正確答案（角色的第一個動畫）
                var correctAnime = animeData.data[0].anime;
                var options = new List<AnimeResponse> { correctAnime };

                // 獲取3個隨機動畫作為錯誤選項
                for (int i = 0; i < 3; i++)
                {
                    await Task.Delay(1000); // API 速率限制
                    var randomResponse = await _httpClient.GetAsync($"{API_BASE_URL}/random/anime");
                    if (randomResponse.IsSuccessStatusCode)
                    {
                        var content = await randomResponse.Content.ReadAsStringAsync();
                        var wrapper = JsonConvert.DeserializeObject<AnimeWrapper>(content);
                        if (wrapper?.data != null)
                        {
                            options.Add(wrapper.data);
                        }
                    }
                }

                // 打亂順序
                return options.OrderBy(x => Guid.NewGuid()).ToList();
            }
            catch
            {
                return null;
            }
        }

        // 建立動畫選項按鈕（用於角色猜動畫）
        private ComponentBuilder BuildAnimeOptionsComponent(List<AnimeResponse> animeOptions, int correctAnswerId)
        {
            var builder = new ComponentBuilder();

            for (int i = 0; i < Math.Min(animeOptions.Count, 4); i++)
            {
                var anime = animeOptions[i];
                string label = anime.title;
                if (label.Length > 80)
                {
                    label = label.Substring(0, 77) + "...";
                }

                builder.WithButton(
                    label: label,
                    customId: $"anime_guess_{anime.mal_id}_{correctAnswerId}",
                    style: ButtonStyle.Primary
                );
            }

            return builder;
        }

        // 建立角色選項按鈕（用於角色猜角色）
        private ComponentBuilder BuildCharacterOptionsComponent(List<CharactersResopnse> characterOptions, int correctAnswerId)
        {
            var builder = new ComponentBuilder();

            for (int i = 0; i < Math.Min(characterOptions.Count, 4); i++)
            {
                var character = characterOptions[i];
                string label = character.name;
                if (label.Length > 80)
                {
                    label = label.Substring(0, 77) + "...";
                }

                builder.WithButton(
                    label: label,
                    customId: $"anime_guess_{character.mal_id}_{correctAnswerId}",
                    style: ButtonStyle.Primary
                );
            }

            return builder;
        }

        // 建立角色猜動畫的 Embed
        private Embed BuildCharacterToAnimeEmbed(CharactersResopnse character, AnimeResponse correctAnime)
        {
            var embedBuilder = new EmbedBuilder()
            {
                Title = "?? 猜猜這個角色來自哪部動畫？",
                Description = $"**角色名稱**: {character.name}",
                Color = Color.Blue
            };

            // 優先使用動畫預告片
            if (!string.IsNullOrEmpty(correctAnime.trailer?.url))
            {
                embedBuilder.Description += $"\n\n[?? 觀看影片]({correctAnime.trailer.url})";
            }

            // 使用角色圖片
            if (!string.IsNullOrEmpty(character.images?.jpg?.image_url))
            {
                embedBuilder.WithImageUrl(character.images.jpg.image_url);
            }

            if (!string.IsNullOrEmpty(character.about))
            {
                string about = character.about;
                if (about.Length > 200)
                {
                    about = about.Substring(0, 197) + "...";
                }
                embedBuilder.AddField("關於", about);
            }

            embedBuilder.WithFooter("請從下方按鈕選擇正確答案");
            embedBuilder.WithCurrentTimestamp();

            return embedBuilder.Build();
        }

        // 建立角色猜角色的 Embed
        private Embed BuildCharacterToCharacterEmbed(CharactersResopnse character)
        {
            var embedBuilder = new EmbedBuilder()
            {
                Title = "?? 猜猜這是哪個角色？",
                Description = "根據圖片猜測角色名稱",
                Color = Color.Gold
            };

            // 使用角色圖片
            if (!string.IsNullOrEmpty(character.images?.jpg?.image_url))
            {
                embedBuilder.WithImageUrl(character.images.jpg.image_url);
            }

            // 提供部分提示（如果有）
            if (!string.IsNullOrEmpty(character.about))
            {
                string hint = character.about;
                if (hint.Length > 150)
                {
                    hint = hint.Substring(0, 147) + "...";
                }
                embedBuilder.AddField("提示", hint);
            }

            embedBuilder.WithFooter("請從下方按鈕選擇正確答案");
            embedBuilder.WithCurrentTimestamp();

            return embedBuilder.Build();
        }

        // 處理按鈕點擊
        public async Task<(Embed embed, ComponentBuilder component)> HandleButtonClickAsync(SocketMessageComponent interaction, int selectedId, int correctId)
        {
            bool isCorrect = selectedId == correctId;

            var embedBuilder = new EmbedBuilder()
            {
                Color = isCorrect ? Color.Green : Color.Red,
                Timestamp = DateTimeOffset.Now
            };

            if (isCorrect)
            {
                embedBuilder.Title = "?? 答對了！";
                embedBuilder.Description = $"恭喜 {interaction.User.Mention} 答對了！\n\n{RewardsHelpers.GetRandomRewards()}";
            }
            else
            {
                embedBuilder.Title = "? 答錯了！";
                embedBuilder.Description = $"{interaction.User.Mention} 答錯了！再接再厲！";
            }

            // 禁用所有按鈕
            var disabledComponent = new ComponentBuilder();

            return (embedBuilder.Build(), disabledComponent);
        }
    }
}
