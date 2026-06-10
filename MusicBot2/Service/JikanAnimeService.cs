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

            // 獲取角色的動畫資訊（返回 (選項列表, 正確答案ID)）
            var (animeOptions, correctAnimeId) = await GetAnimeOptionsForCharacterAsync(charaResponse.mal_id);
            if (animeOptions == null || animeOptions.Count == 0)
            {
                return CommonHelper.BuildErrorResponse("無法取得角色的動畫資訊，請重試");
            }

            var component = BuildAnimeOptionsComponent(animeOptions, correctAnimeId);
            var embed = BuildCharacterToAnimeEmbed(charaResponse);

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

            // 獲取其他5個隨機角色作為選項
            var characterOptions = new List<CharactersResopnse> { correctCharacter };

            while (characterOptions.Count < 6)
            {
                var randomChar = await GetCharacterAsync(false);
                if (randomChar != null && randomChar.mal_id != correctCharacter.mal_id)
                {
                    characterOptions.Add(randomChar);
                }
                else
                {
                    await Task.Delay(500);
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
        private async Task<(List<AnimeResponse> options, int correctAnimeId)> GetAnimeOptionsForCharacterAsync(int characterId)
        {
            try
            {
                // 獲取角色出現的動畫
                string url = $"{API_BASE_URL}/characters/{characterId}/anime";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return (null, 0);
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var animeData = JsonConvert.DeserializeObject<CharacterAnimeResponse>(responseContent);

                if (animeData.data == null || animeData.data.Count == 0)
                {
                    return (null, 0);
                }

                // 取正確答案（角色的第一個動畫）
                var correctAnime = animeData.data[0].anime;
                int correctAnimeId = correctAnime.mal_id; // 記錄正確答案的 ID
                var options = new List<AnimeResponse> { correctAnime };

                // 獲取5個隨機動畫作為錯誤選項
                for (int i = 0; i < 5; i++)
                {
                    var randomResponse = await _httpClient.GetAsync($"{API_BASE_URL}/random/anime");
                    if (randomResponse.IsSuccessStatusCode)
                    {
                        var content = await randomResponse.Content.ReadAsStringAsync();
                        var wrapper = JsonConvert.DeserializeObject<AnimeWrapper>(content);
                        if (wrapper?.data != null && wrapper.data.mal_id != correctAnimeId)
                        {
                            options.Add(wrapper.data);
                        }
                    }
                }

                while (options.Count < 6)
                {
                    var randomResponse = await _httpClient.GetAsync($"{API_BASE_URL}/random/anime");
                    if (randomResponse.IsSuccessStatusCode)
                    {
                        var content = await randomResponse.Content.ReadAsStringAsync();
                        var wrapper = JsonConvert.DeserializeObject<AnimeWrapper>(content);
                        if (wrapper?.data != null && wrapper.data.mal_id != correctAnimeId)
                        {
                            options.Add(wrapper.data);
                        }
                    }
                    else
                    {
                        Console.WriteLine("取得 null，重試...");
                    }
                    await Task.Delay(500);
                }

                // 打亂順序
                var shuffledOptions = options.OrderBy(x => Guid.NewGuid()).ToList();
                return (shuffledOptions, correctAnimeId);
            }
            catch
            {
                return (null, 0);
            }
        }

        // 建立動畫選項按鈕（用於角色猜動畫）
        private ComponentBuilder BuildAnimeOptionsComponent(List<AnimeResponse> animeOptions, int correctAnswerId)
        {
            var builder = new ComponentBuilder();

            for (int i = 0; i < Math.Min(animeOptions.Count, 6); i++)
            {
                var anime = animeOptions[i];
                string label = $"{anime.title} / {anime.title_japanese}";
                if (label.Length > 80)
                {
                    label = label.Substring(0, 77) + "...";
                }

                builder.WithButton(
                    label: label,
                    customId: $"anime_guess_{anime.mal_id}_{correctAnswerId}",
                    style: ButtonStyle.Primary,
                    row: i / 3  // 第0-2個按鈕在第0行，第3-5個按鈕在第1行
                );
            }

            return builder;
        }

        // 建立角色選項按鈕（用於角色猜角色）
        private ComponentBuilder BuildCharacterOptionsComponent(List<CharactersResopnse> characterOptions, int correctAnswerId)
        {
            var builder = new ComponentBuilder();

            for (int i = 0; i < Math.Min(characterOptions.Count, 6); i++)
            {
                var character = characterOptions[i];
                string label = $"{character.name} / {character.name_kanji}";
                if (label.Length > 80)
                {
                    label = label.Substring(0, 77) + "...";
                }

                builder.WithButton(
                    label: label,
                    customId: $"anime_guess_{character.mal_id}_{correctAnswerId}",
                    style: ButtonStyle.Primary,
                    row: i / 3  // 第0-2個按鈕在第0行，第3-5個按鈕在第1行
                );
            }

            return builder;
        }

        // 建立角色猜動畫的 Embed
        private Embed BuildCharacterToAnimeEmbed(CharactersResopnse character)
        {
            var embedBuilder = new EmbedBuilder()
            {
                Title = "這哪部動畫來的？",
                Description = $"**角色名稱**: {character.name}",
                Color = Color.Blue
            };

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
                Title = "這誰？",
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

            // 獲取原始 Embed 資訊
            var originalEmbed = interaction.Message.Embeds.FirstOrDefault();
            var embedBuilder = new EmbedBuilder();

            // 保留原始資訊
            if (originalEmbed != null)
            {
                embedBuilder.Title = originalEmbed.Title;
                embedBuilder.Description = originalEmbed.Description;
                embedBuilder.Color = isCorrect ? Color.Green : Color.Red;
                embedBuilder.Timestamp = DateTimeOffset.Now;

                // 保留原始圖片
                if (originalEmbed.Image.HasValue)
                {
                    embedBuilder.WithImageUrl(originalEmbed.Image.Value.Url);
                }

                // 保留原始欄位
                if (originalEmbed.Fields.Length > 0)
                {
                    foreach (var field in originalEmbed.Fields)
                    {
                        embedBuilder.AddField(field.Name, field.Value, field.Inline);
                    }
                }
            }
            else
            {
                embedBuilder.Color = isCorrect ? Color.Green : Color.Red;
                embedBuilder.Timestamp = DateTimeOffset.Now;
            }

            // 添加結果訊息
            if (isCorrect)
            {
                embedBuilder.AddField("✅ 宅斃了", $"恭喜 宅王之王 **{interaction.User.Mention}** 答對了！ 獎勵你 \n\n{RewardsHelpers.GetRandomRewards()}");
            }
            else
            {
                embedBuilder.AddField("❌ 菜逼八", $"{interaction.User.Mention} so 菜！這你都不認識？");
            }

            // 禁用所有按鈕
            var disabledComponent = new ComponentBuilder();

            return (embedBuilder.Build(), disabledComponent);
        }


        public async Task<((ComponentBuilder component,Embed embed),string imageUrl)> GetSomeRandomAnime(string type, string ratings)
        {
            try
            {
                string url = $"{API_BASE_URL}/top/anime?";

                if(!string.IsNullOrEmpty(type))
                {
                    url += $"&type={type}";
                }
                if(!string.IsNullOrEmpty(ratings))
                {
                    url += $"&rating={ratings}";
                }

                Random random = new Random();
                int page = random.Next(1, 51);
                url += $"&page={page}";
                Console.WriteLine(url);
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return (CommonHelper.BuildErrorResponse("無法獲取動畫資料"),"");
                }
                var content = await response.Content.ReadAsStringAsync();
                var wrapper = JsonConvert.DeserializeObject<TopAnimeResponse>(content);

                Random random2 = new Random();
                var anime = wrapper.data[random2.Next(wrapper.data.Count)];

                var embedBuilder = new EmbedBuilder()
                {
                    Title = $"{anime.title} / {anime.title_japanese}",
                    Description = anime.synopsis ?? "沒有動畫簡介",
                    Color = Color.Purple
                };
                if (!string.IsNullOrEmpty(anime.images?.jpg?.image_url) && !anime.rating.ToLower().StartsWith("rx"))
                {
                    embedBuilder.WithImageUrl(anime.images.jpg.image_url);
                }
                return ((new ComponentBuilder(),embedBuilder.Build()), anime.images.jpg.image_url);
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}"), "");
            }
        }
    }
}
