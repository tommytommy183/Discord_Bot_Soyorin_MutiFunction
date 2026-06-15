using Discord;
using Discord.WebSocket;
using MusicBot2.Helpers;
using MusicBot2.Models;
using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;


namespace MusicBot2.Service
{
    public class PokeService
    {
        private readonly HttpClient _httpClient;
        private const string API_BASE_URL = "https://pokeapi.co/api/v2/";

        public PokeService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<((ComponentBuilder component, Embed embed), Stream silhouette)> StartPokeGameAsync(string mode)
        {
            try
            {
                if (mode.ToLower() == "name" || mode.ToLower() == "猜pokemon名稱")
                {
                    var (component, embed) = await StartGuessNameGameAsync();
                    return ((component, embed), null);
                }
                else if (mode.ToLower() == "move" || mode.ToLower() == "猜pokemon技能")
                {
                    var (component, embed) = await StartGuessMoveGameAsync();
                    return ((component, embed), null);
                }
                else if (mode.ToLower() == "who" || mode.ToLower() == "我是誰")
                {
                    return await StartGuessWhoGameAsync();
                }
                else
                {
                    return (CommonHelper.BuildErrorResponse("模式錯誤！請選擇 'name'(猜pokemon名稱) 或 'move'(猜pokemon技能)"), null);
                }
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}"), null);
            }
        }

        #region 猜寶可夢名稱
        private async Task<(ComponentBuilder component, Embed embed)> StartGuessNameGameAsync()
        {
            // 獲取正確角色
            try
            {
                //先全部取出來，再隨機挑
                string url = $"{API_BASE_URL}pokemon?limit=10000&offset=0";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return CommonHelper.BuildErrorResponse("無法獲取寶可夢資料");

                var responseContent = await response.Content.ReadAsStringAsync();
                var pokeResponses = JsonConvert.DeserializeObject<RandomResponse>(responseContent);

                Random random = new Random();

                // 先記住正確答案的 index
                int correctIndex = random.Next(0, pokeResponses.results.Count);

                int correctPokeId = pokeResponses.results[correctIndex].url.Split('/').Where(x => !string.IsNullOrEmpty(x)).LastOrDefault() is string idStr && int.TryParse(idStr, out int id) ? id : 0;
                // 抽 5 個不重複的干擾選項
                var otherIndexes = Enumerable.Range(0, pokeResponses.results.Count)
                    .Where(i => i != correctIndex)
                    .OrderBy(_ => random.Next())
                    .Take(5)
                    .ToList();

                // 組成 6 個選項（含正確答案）並 shuffle
                var allIndexes = otherIndexes.Append(correctIndex)
                    .OrderBy(_ => random.Next())
                    .ToList();

                // 打 API 取得每個選項的詳細資料
                var options = new List<Pokemon>();
                foreach (var idx in allIndexes)
                {
                    string urlData = pokeResponses.results[idx].url;
                    var res = await _httpClient.GetAsync(urlData);
                    var content = await res.Content.ReadAsStringAsync();

                    var poke = JsonConvert.DeserializeObject<Pokemon>(content);

                    string urlSpecies = poke.species.url;
                    var resSpecies = await _httpClient.GetAsync(urlSpecies);
                    var contentSpecies = await resSpecies.Content.ReadAsStringAsync();
                    var pokeSpecies = JsonConvert.DeserializeObject<PokeSpecies>(contentSpecies);

                    poke.formatted_name = pokeSpecies;

                    options.Add(poke);
                }

                string correctPokeName = options.FirstOrDefault(x => x.id == correctPokeId)?.formatted_name?.names.FirstOrDefault(n => n.language.name == "zh-hant")?.name
                                         ?? options.FirstOrDefault(x => x.id == correctPokeId)?.formatted_name?.names.FirstOrDefault(n => n.language.name == "zh-hans")?.name
                                         ?? "未知";

                var component = BuildPokeOptionsComponent(options, correctPokeId, correctPokeName);
                var embed = BuildPokeEmbed(options.FirstOrDefault(x => x.id == correctPokeId));
                return (component, embed);
            }
            catch (Exception ex)
            {
                return CommonHelper.BuildErrorResponse($"@zu_tomayo看一下啦，要死了要死了 \n {ex}");
            }
        }

        // 建立poke選項按鈕（用於猜pokemon名稱）
        private ComponentBuilder BuildPokeOptionsComponent(List<Pokemon> pokeOptions, int correctAnswerId, string correctAnswerName)
        {
            var builder = new ComponentBuilder();

            for (int i = 0; i < Math.Min(pokeOptions.Count, 6); i++)
            {
                var poke = pokeOptions[i];
                string label = $"{poke.formatted_name?.names.FirstOrDefault(n => n.language.name == "zh-hant")?.name ?? poke.formatted_name?.names.FirstOrDefault(n => n.language.name == "zh-hans")?.name ?? "未知"} / {poke.formatted_name?.names.FirstOrDefault(n => n.language.name == "en")?.name ?? "Unknown"}";
                if (label.Length > 80)
                {
                    label = label.Substring(0, 77) + "...";
                }

                builder.WithButton(
                    label: label,
                    customId: $"poke_guess_{poke.id}_{correctAnswerId}_{correctAnswerName}",
                    style: ButtonStyle.Primary,
                    row: i / 3  // 第0-2個按鈕在第0行，第3-5個按鈕在第1行
                );
            }

            return builder;
        }

        // 建立角色猜動畫的 Embed
        private Embed BuildPokeEmbed(Pokemon pokemon)
        {
            string pokeInfo = pokemon.formatted_name.flavor_text_entries.FirstOrDefault(e => e.language.name == "zh-hant")?.flavor_text
                            ?? pokemon.formatted_name.flavor_text_entries.FirstOrDefault(e => e.language.name == "zh-hans")?.flavor_text
                            ?? "無介紹";


            var embedBuilder = new EmbedBuilder()
            {
                Title = "猜pokemon名稱？",
                Description = $"**Pokemon屬性**:{string.Join(", ", pokemon.types.Select(t => t.type.name))}",
                Color = Discord.Color.Blue
            };

            // 使用角色圖片
            if (!string.IsNullOrEmpty(pokemon.sprites.front_default))
            {
                embedBuilder.WithImageUrl(pokemon.sprites.front_default);
            }

            if (!string.IsNullOrEmpty(pokemon.sprites.back_default))
            {
                embedBuilder.WithThumbnailUrl(pokemon.sprites.back_default);
            }

            if (!string.IsNullOrEmpty(pokeInfo))
            {
                string about = $" **Pokemon介紹**: {pokeInfo}";
                if (about.Length > 500)
                {
                    about = about.Substring(0, 497) + "...";
                }
                embedBuilder.AddField("關於", about);
            }

            embedBuilder.WithFooter("請從下方按鈕選擇正確答案");
            embedBuilder.WithCurrentTimestamp();

            return embedBuilder.Build();
        }

        #endregion

        #region 猜招式名稱
        private async Task<(ComponentBuilder component, Embed embed)> StartGuessMoveGameAsync()
        {
            // 獲取正確角色
            try
            {
                //先全部取出來，再隨機挑
                string url = $"{API_BASE_URL}move?limit=10000&offset=0";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return CommonHelper.BuildErrorResponse("無法獲取招式資料");

                var responseContent = await response.Content.ReadAsStringAsync();
                var pokeResponses = JsonConvert.DeserializeObject<RandomResponse>(responseContent);

                Random random = new Random();

                // 先記住正確答案的 index
                int correctIndex = random.Next(0, pokeResponses.results.Count);

                int correctMoveId = pokeResponses.results[correctIndex].url.Split('/').Where(x => !string.IsNullOrEmpty(x)).LastOrDefault() is string idStr && int.TryParse(idStr, out int id) ? id : 0;
                // 抽 5 個不重複的干擾選項
                var otherIndexes = Enumerable.Range(0, pokeResponses.results.Count)
                    .Where(i => i != correctIndex)
                    .OrderBy(_ => random.Next())
                    .Take(5)
                    .ToList();

                // 組成 6 個選項（含正確答案）並 shuffle
                var allIndexes = otherIndexes.Append(correctIndex)
                    .OrderBy(_ => random.Next())
                    .ToList();

                // 打 API 取得每個選項的詳細資料
                var options = new List<Move>();
                foreach (var idx in allIndexes)
                {
                    string urlData = pokeResponses.results[idx].url;
                    var res = await _httpClient.GetAsync(urlData);
                    var content = await res.Content.ReadAsStringAsync();

                    var move = JsonConvert.DeserializeObject<Move>(content);
                    options.Add(move);
                }

                string correctMoveName = options.FirstOrDefault(x => x.id == correctMoveId)?.names.FirstOrDefault(n => n.language.name == "zh-hant")?.name
                                         ?? options.FirstOrDefault(x => x.id == correctMoveId)?.names.FirstOrDefault(n => n.language.name == "zh-hans")?.name
                                         ?? "未知";


                //正確招式中取可學會的poke5筆，然後分別取他們中文名字，組字串，傳進embed
                List<ResultData> pokeDatas = new List<ResultData>();
                List<string> learnedByPokemons = new List<string>();
                pokeDatas = options.FirstOrDefault(x => x.id == correctMoveId).learned_by_pokemon;
                pokeDatas = pokeDatas.OrderBy(x => random.Next()).Take(5).ToList();

                foreach (var pokeData in pokeDatas)
                {
                    string urlData = pokeData.url;
                    var res = await _httpClient.GetAsync(urlData);
                    var content = await res.Content.ReadAsStringAsync();

                    var poke = JsonConvert.DeserializeObject<Pokemon>(content);

                    string urlSpecies = poke.species.url;
                    var resSpecies = await _httpClient.GetAsync(urlSpecies);
                    var contentSpecies = await resSpecies.Content.ReadAsStringAsync();
                    var pokeSpecies = JsonConvert.DeserializeObject<PokeSpecies>(contentSpecies);

                    string pokeName = pokeSpecies.names.FirstOrDefault(n => n.language.name == "zh-hant")?.name
                                     ?? pokeSpecies.names.FirstOrDefault(n => n.language.name == "zh-hans")?.name
                                     ?? "未知";
                    learnedByPokemons.Add(pokeName);
                }





                var component = BuildMoveOptionsComponent(options, correctMoveId, correctMoveName);
                var embed = BuildMoveEmbed(options.FirstOrDefault(x => x.id == correctMoveId), string.Join(", ", learnedByPokemons));
                return (component, embed);
            }
            catch (Exception ex)
            {
                return CommonHelper.BuildErrorResponse($"@zu_tomayo看一下啦，要死了要死了 \n {ex}");
            }
        }

        // 建立角色選項按鈕（用於角色猜角色）
        private ComponentBuilder BuildMoveOptionsComponent(List<Move> moveOptions, int correctAnswerId, string correctAnswerName)
        {
            var builder = new ComponentBuilder();

            for (int i = 0; i < Math.Min(moveOptions.Count, 6); i++)
            {
                var move = moveOptions[i];
                string label = $"{move.names.FirstOrDefault(n => n.language.name == "zh-hant")?.name ?? move.names.FirstOrDefault(n => n.language.name == "zh-hans")?.name ?? "未知"} / {move.names.FirstOrDefault(n => n.language.name == "en")?.name ?? "Unknown"}";
                if (label.Length > 80)
                {
                    label = label.Substring(0, 77) + "...";
                }

                builder.WithButton(
                    label: label,
                    customId: $"poke_guess_{move.id}_{correctAnswerId}_{correctAnswerName}",
                    style: ButtonStyle.Primary,
                    row: i / 3  // 第0-2個按鈕在第0行，第3-5個按鈕在第1行
                );
            }

            return builder;
        }

        // 建立角色猜角色的 Embed
        private Embed BuildMoveEmbed(Move move, string learnedByPokemons)
        {
            string moveInfo = $"**招式介紹:\n** {move.flavor_text_entries.FirstOrDefault(e => e.language.name == "zh-hant")?.flavor_text
                ?? move.flavor_text_entries.FirstOrDefault(e => e.language.name == "zh-hans")?.flavor_text
                ?? "無介紹 "} \n **可學習的寶可夢**: {learnedByPokemons}";


            var embedBuilder = new EmbedBuilder()
            {
                Title = "這哪招？",
                Description = $"**招式屬性**:{move.damage_class?.name ?? "未知"}",
                Color = Discord.Color.Blue
            };

            if (!string.IsNullOrEmpty(moveInfo))
            {
                if (moveInfo.Length > 500)
                {
                    moveInfo = moveInfo.Substring(0, 497) + "...";
                }
                embedBuilder.AddField("關於", moveInfo);
            }

            embedBuilder.WithFooter("請從下方按鈕選擇正確答案");
            embedBuilder.WithCurrentTimestamp();

            return embedBuilder.Build();
        }
        #endregion

        #region 猜猜我是誰(塗黑版)
        private async Task<((ComponentBuilder component, Embed embed), Stream silhouette)> StartGuessWhoGameAsync()
        {
            // 獲取正確角色
            try
            {
                //先全部取出來，再隨機挑
                string url = $"{API_BASE_URL}pokemon?limit=10000&offset=0";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return (CommonHelper.BuildErrorResponse("無法獲取寶可夢資料"), null);

                var responseContent = await response.Content.ReadAsStringAsync();
                var pokeResponses = JsonConvert.DeserializeObject<RandomResponse>(responseContent);

                Random random = new Random();

                // 先記住正確答案的 index
                int correctIndex = random.Next(0, pokeResponses.results.Count);

                int correctPokeId = pokeResponses.results[correctIndex].url.Split('/').Where(x => !string.IsNullOrEmpty(x)).LastOrDefault() is string idStr && int.TryParse(idStr, out int id) ? id : 0;
                // 抽 5 個不重複的干擾選項
                var otherIndexes = Enumerable.Range(0, pokeResponses.results.Count)
                    .Where(i => i != correctIndex)
                    .OrderBy(_ => random.Next())
                    .Take(5)
                    .ToList();

                // 組成 6 個選項（含正確答案）並 shuffle
                var allIndexes = otherIndexes.Append(correctIndex)
                    .OrderBy(_ => random.Next())
                    .ToList();

                // 打 API 取得每個選項的詳細資料
                var options = new List<Pokemon>();
                foreach (var idx in allIndexes)
                {
                    string urlData = pokeResponses.results[idx].url;
                    var res = await _httpClient.GetAsync(urlData);
                    var content = await res.Content.ReadAsStringAsync();

                    var poke = JsonConvert.DeserializeObject<Pokemon>(content);

                    string urlSpecies = poke.species.url;
                    var resSpecies = await _httpClient.GetAsync(urlSpecies);
                    var contentSpecies = await resSpecies.Content.ReadAsStringAsync();
                    var pokeSpecies = JsonConvert.DeserializeObject<PokeSpecies>(contentSpecies);

                    poke.formatted_name = pokeSpecies;

                    options.Add(poke);
                }

                string correctPokeName = options.FirstOrDefault(x => x.id == correctPokeId)?.formatted_name?.names.FirstOrDefault(n => n.language.name == "zh-hant")?.name
                                         ?? options.FirstOrDefault(x => x.id == correctPokeId)?.formatted_name?.names.FirstOrDefault(n => n.language.name == "zh-hans")?.name
                                         ?? "未知";

                var component = BuildPokeOptionsComponent(options, correctPokeId, correctPokeName);
                var embed = await BuildWhoEmbed(options.FirstOrDefault(x => x.id == correctPokeId));

                return ((component, embed.embed), embed.silhouette);
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"@zu_tomayo看一下啦，要死了要死了 \n {ex}"), null);
            }
        }

        // 建立角色猜動畫的 Embed
        private async Task<(Embed embed, Stream silhouette)> BuildWhoEmbed(Pokemon pokemon)
        {
            string pokeInfo = pokemon.formatted_name.flavor_text_entries.FirstOrDefault(e => e.language.name == "zh-hant")?.flavor_text
                            ?? pokemon.formatted_name.flavor_text_entries.FirstOrDefault(e => e.language.name == "zh-hans")?.flavor_text
                            ?? "無介紹";

            var embedBuilder = new EmbedBuilder()
            {
                Title = "我是誰？",
                Description = $"**Pokemon屬性**: {string.Join(", ", pokemon.types.Select(t => t.type.name))}",
                Color = Discord.Color.Blue
            };

            // 剪影圖
            Stream silhouette = null;
            string imageUrl = pokemon.sprites?.front_default;

            if (!string.IsNullOrEmpty(imageUrl))
            {
                silhouette = await MakeBlackSilhouette(imageUrl);
                embedBuilder.WithImageUrl("attachment://mystery.png");
            }

            if (!string.IsNullOrEmpty(pokeInfo))
            {
                string about = $" **Pokemon介紹**: {pokeInfo}";
                if (about.Length > 500)
                    about = about.Substring(0, 497) + "...";
                embedBuilder.AddField("關於", about);
            }

            embedBuilder.WithFooter("請從下方按鈕選擇正確答案");
            embedBuilder.WithCurrentTimestamp();

            return (embedBuilder.Build(), silhouette);
        }

        private async Task<Stream> MakeBlackSilhouette(string imageUrl)
        {
            var bytes = await _httpClient.GetByteArrayAsync(imageUrl);
            using var bitmap = SKBitmap.Decode(bytes);
            using var result = new SKBitmap(bitmap.Width, bitmap.Height);

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);

                    if (pixel.Alpha < 25)
                        result.SetPixel(x, y, SKColors.White);  // 透明 → 白底
                    else
                        result.SetPixel(x, y, SKColors.Black);  // 有內容 → 黑色輪廓
                }
            }

            using var image = SKImage.FromBitmap(result);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            var output = new MemoryStream();
            data.SaveTo(output);
            output.Position = 0;
            return output;
        }

        #endregion

        #region 共用
        // 處理按鈕點擊
        public async Task<(Embed embed, ComponentBuilder component)> HandleButtonClickAsync(SocketMessageComponent interaction, int selectedId, int correctId, string correctName)
        {
            bool isCorrect = selectedId == correctId;
            bool isGuessWho = false;

            // 獲取原始 Embed 資訊
            var originalEmbed = interaction.Message.Embeds.FirstOrDefault();
            var embedBuilder = new EmbedBuilder();

            // 保留原始資訊
            if (originalEmbed != null)
            {
                embedBuilder.Title = originalEmbed.Title;
                if (originalEmbed.Title.Contains("我是誰"))
                {
                    isGuessWho = true;
                }
                embedBuilder.Description = originalEmbed.Description;
                embedBuilder.Color = isCorrect ? Discord.Color.Green : Discord.Color.Red;
                embedBuilder.Timestamp = DateTimeOffset.Now;

                // 保留原始圖片
                if(isGuessWho)
                {
                    embedBuilder.WithImageUrl($"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{correctId}.png");
                }
                else if (originalEmbed.Image.HasValue)
                {
                    embedBuilder.WithImageUrl(originalEmbed.Image.Value.Url);
                }

                else if (originalEmbed.Thumbnail.HasValue)
                {
                    embedBuilder.WithThumbnailUrl(originalEmbed.Thumbnail.Value.Url);
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
                embedBuilder.Color = isCorrect ? Discord.Color.Green : Discord.Color.Red;
                embedBuilder.Timestamp = DateTimeOffset.Now;
            }

            // 添加結果訊息
            if (isCorrect)
            {
                embedBuilder.AddField($"✅ 宅斃了: {correctName}", $"恭喜 30年Pokemon老粉 **{interaction.User.Mention}** 答對了！ 獎勵你 \n\n{await RewardsHelpers.GetRandomRewards(interaction.Channel, interaction.User as SocketGuildUser)}");
            }
            else
            {
                embedBuilder.AddField("❌ 菜逼八", $"{interaction.User.Mention} 你這個虛假的Pokemon粉絲: {correctName}");
            }

            // 禁用所有按鈕
            var disabledComponent = new ComponentBuilder();

            return (embedBuilder.Build(), disabledComponent);
        }

        #endregion
    }
}
