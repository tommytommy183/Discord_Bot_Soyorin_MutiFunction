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
        //正式版，目前無法使用
        //private const string API_BASE_URL = "https://api.jikan.moe/v4";
        //測試版，使用 Cloudflare 代理
        private const string API_BASE_URL = "https://jikan-edge.lucas-hdo.workers.dev/v1";

        public JikanAnimeService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        public async Task<(ComponentBuilder component, Embed embed)> StartGameAsync(string mode, bool isTop)
        {
            try
            {
                if (mode.ToLower() == "cta" || mode.ToLower() == "角色猜動畫")
                    return await StartCharacterToAnimeGameAsync(isTop);
                else if (mode.ToLower() == "ctc" || mode.ToLower() == "角色猜角色")
                    return await StartCharacterToCharacterGameAsync(isTop);
                else
                    return CommonHelper.BuildErrorResponse("模式錯誤！請選擇 'cta'(角色猜動畫) 或 'ctc'(角色猜角色)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Jikan] StartGameAsync 例外: {ex.Message}");
                return CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}");
            }
        }

        // 角色猜動畫模式
        //private async Task<(ComponentBuilder component, Embed embed)> StartCharacterToAnimeGameAsync(bool isTop)
        //{
        //    var charaResponse = await GetCharacterAsync(isTop);
        //    if (charaResponse == null)
        //        return CommonHelper.BuildErrorResponse("無法獲取角色資料");
        //    var (animeOptions, correctAnimeId, correnctAnimeName) = await GetAnimeOptionsForCharacterAsync(charaResponse.mal_id);
        //    if (animeOptions == null || animeOptions.Count == 0)
        //        return CommonHelper.BuildErrorResponse("無法取得角色的動畫資訊，請重試");
        //    var component = BuildAnimeOptionsComponent(animeOptions, correctAnimeId, correnctAnimeName);
        //    var embed = BuildCharacterToAnimeEmbed(charaResponse);
        //    return (component, embed);
        //}

        // 角色猜角色模式
        //private async Task<(ComponentBuilder component, Embed embed)> StartCharacterToCharacterGameAsync(bool isTop)
        //{
        //    var correctCharacter = await GetCharacterAsync(isTop);
        //    if (correctCharacter == null)
        //        return CommonHelper.BuildErrorResponse("無法獲取角色資料");
        //    var characterOptions = new List<CharactersResopnse> { correctCharacter };
        //    while (characterOptions.Count < 6)
        //    {
        //        var randomChar = await GetCharacterAsync(false);
        //        if (randomChar != null && randomChar.mal_id != correctCharacter.mal_id)
        //            characterOptions.Add(randomChar);
        //        else
        //            await Task.Delay(500);
        //    }
        //    characterOptions = characterOptions.OrderBy(x => Guid.NewGuid()).ToList();
        //    var component = BuildCharacterOptionsComponent(characterOptions, correctCharacter.mal_id, correctCharacter.name);
        //    var embed = BuildCharacterToCharacterEmbed(correctCharacter);
        //    return (component, embed);
        //}

        // 獲取角色（v4）
        //private async Task<CharactersResopnse> GetCharacterAsync(bool isTop)
        //{
        //    try
        //    {
        //        if (isTop)
        //        {
        //            Random random = new Random();
        //            int page = random.Next(1, 51);
        //            string url = $"{API_BASE_URL}/top/characters?page={page}";
        //            var response = await _httpClient.GetAsync(url);
        //            if (!response.IsSuccessStatusCode) return null;
        //            var responseContent = await response.Content.ReadAsStringAsync();
        //            var charaResponses = JsonConvert.DeserializeObject<TopCharactersResponse>(responseContent);
        //            int index = random.Next(0, charaResponses.data.Count);
        //            return charaResponses.data[index];
        //        }
        //        else
        //        {
        //            string url = $"{API_BASE_URL}/random/characters";
        //            var response = await _httpClient.GetAsync(url);
        //            if (!response.IsSuccessStatusCode) return null;
        //            var responseContent = await response.Content.ReadAsStringAsync();
        //            var wrapper = JsonConvert.DeserializeObject<CharacterWrapper>(responseContent);
        //            var fullResponse = await _httpClient.GetAsync($"{API_BASE_URL}/characters/{wrapper.data.mal_id}/full");
        //            if (!fullResponse.IsSuccessStatusCode) return null;
        //            var fullResponseContent = await fullResponse.Content.ReadAsStringAsync();
        //            var fullWrapper = JsonConvert.DeserializeObject<CharacterWrapper>(fullResponseContent);
        //            return fullWrapper.data;
        //        }
        //    }
        //    catch { return null; }
        //}

        // 獲取角色動畫選項（v4）
        //private async Task<(List<AnimeResponse> options, int correctAnimeId, string correctAnimeName)> GetAnimeOptionsForCharacterAsync(int characterId)
        //{
        //    try
        //    {
        //        string url = $"{API_BASE_URL}/characters/{characterId}/anime";
        //        var response = await _httpClient.GetAsync(url);
        //        if (!response.IsSuccessStatusCode) return (null, 0, null);
        //        var responseContent = await response.Content.ReadAsStringAsync();
        //        var animeData = JsonConvert.DeserializeObject<CharacterAnimeResponse>(responseContent);
        //        if (animeData.data == null || animeData.data.Count == 0) return (null, 0, null);
        //        var correctAnime = animeData.data[0].anime;
        //        int correctAnimeId = correctAnime.mal_id;
        //        string correctAnimeName = correctAnime.title;
        //        var options = new List<AnimeResponse> { correctAnime };
        //        while (options.Count < 6)
        //        {
        //            var randomResponse = await _httpClient.GetAsync($"{API_BASE_URL}/random/anime");
        //            if (randomResponse.IsSuccessStatusCode)
        //            {
        //                var content = await randomResponse.Content.ReadAsStringAsync();
        //                var wrapper = JsonConvert.DeserializeObject<AnimeWrapper>(content);
        //                if (wrapper?.data != null && wrapper.data.mal_id != correctAnimeId)
        //                    options.Add(wrapper.data);
        //            }
        //            await Task.Delay(500);
        //        }
        //        var shuffledOptions = options.OrderBy(x => Guid.NewGuid()).ToList();
        //        return (shuffledOptions, correctAnimeId, correctAnimeName);
        //    }
        //    catch { return (null, 0, null); }
        //}

        // 建立動畫選項按鈕（v4，用 AnimeResponse）
        //private ComponentBuilder BuildAnimeOptionsComponent(List<AnimeResponse> animeOptions, int correctAnswerId, string correctAnswerName)
        //{
        //    var builder = new ComponentBuilder();
        //    for (int i = 0; i < Math.Min(animeOptions.Count, 6); i++)
        //    {
        //        var anime = animeOptions[i];
        //        string label = $"{anime.title} / {anime.title_japanese}";
        //        if (label.Length > 80) label = label.Substring(0, 77) + "...";
        //        builder.WithButton(label: label, customId: $"anime_guess_{anime.mal_id}_{correctAnswerId}_{correctAnswerName}", style: ButtonStyle.Primary, row: i / 3);
        //    }
        //    return builder;
        //}

        // 建立角色選項按鈕（v4，用 CharactersResopnse）
        //private ComponentBuilder BuildCharacterOptionsComponent(List<CharactersResopnse> characterOptions, int correctAnswerId, string correnctAnswerName)
        //{
        //    var builder = new ComponentBuilder();
        //    for (int i = 0; i < Math.Min(characterOptions.Count, 6); i++)
        //    {
        //        var character = characterOptions[i];
        //        string label = $"{character.name} / {character.name_kanji}";
        //        if (label.Length > 80) label = label.Substring(0, 77) + "...";
        //        builder.WithButton(label: label, customId: $"anime_guess_{character.mal_id}_{correctAnswerId}_{correnctAnswerName}", style: ButtonStyle.Primary, row: i / 3);
        //    }
        //    return builder;
        //}

        // 建立角色猜動畫 Embed（v4）
        //private Embed BuildCharacterToAnimeEmbed(CharactersResopnse character)
        //{
        //    var embedBuilder = new EmbedBuilder() { Title = "這哪部動畫來的？", Description = $"**角色名稱**: {character.name}", Color = Color.Blue };
        //    if (!string.IsNullOrEmpty(character.images?.jpg?.image_url)) embedBuilder.WithImageUrl(character.images.jpg.image_url);
        //    if (!string.IsNullOrEmpty(character.about))
        //    {
        //        string about = character.about.Length > 500 ? character.about.Substring(0, 497) + "..." : character.about;
        //        embedBuilder.AddField("關於", about);
        //    }
        //    embedBuilder.WithFooter("請從下方按鈕選擇正確答案");
        //    embedBuilder.WithCurrentTimestamp();
        //    return embedBuilder.Build();
        //}

        // 建立角色猜角色 Embed（v4）
        //private Embed BuildCharacterToCharacterEmbed(CharactersResopnse character)
        //{
        //    var embedBuilder = new EmbedBuilder() { Title = "這誰？", Description = "根據圖片猜測角色名稱", Color = Color.Gold };
        //    if (!string.IsNullOrEmpty(character.images?.jpg?.image_url)) embedBuilder.WithImageUrl(character.images.jpg.image_url);
        //    string hint = string.Empty;
        //    if (character.voices != null && character.voices.Count > 0)
        //    {
        //        var vas = character.voices.Where(x => string.Equals(x.language?.Trim(), "Japanese", StringComparison.OrdinalIgnoreCase)).Select(x => x.person?.name).Where(x => !string.IsNullOrWhiteSpace(x));
        //        hint += $"**聲優**: ||{string.Join("、", vas)}||\n";
        //    }
        //    if (character.nicknames != null && character.nicknames.Count > 0) hint += $"**綽號**: ||{string.Join(", ", character.nicknames)}||\n";
        //    if (!string.IsNullOrEmpty(character.about)) hint += $"**簡介**: {character.about}";
        //    if (hint.Length > 500) hint = hint.Substring(0, 497) + "...";
        //    if (!string.IsNullOrEmpty(hint))
        //    {
        //        hint = hint.Replace(character.name, "???", StringComparison.OrdinalIgnoreCase);
        //        hint = hint.Replace(character.name_kanji, "???", StringComparison.OrdinalIgnoreCase);
        //        embedBuilder.AddField("提示", hint);
        //    }
        //    embedBuilder.WithFooter("請從下方按鈕選擇正確答案");
        //    embedBuilder.WithCurrentTimestamp();
        //    return embedBuilder.Build();
        //}

        // 處理按鈕點擊
        // isFirstAttempt=true  → 第一次猜（customId: anime_guess_...）
        // isFirstAttempt=false → 第二次猜（customId: anime_guess_r2_...），猜錯直接結束並顯示提示
        public async Task<(Embed embed, ComponentBuilder component)> HandleButtonClickAsync(
            SocketMessageComponent interaction, int selectedId, int correctId, string correctName, bool isFirstAttempt)
        {
            bool isCorrect = selectedId == correctId;
            var originalEmbed = interaction.Message.Embeds.FirstOrDefault();

            var embedBuilder = new EmbedBuilder();
            if (originalEmbed != null)
            {
                embedBuilder.Title = originalEmbed.Title;
                embedBuilder.Description = originalEmbed.Description;
                embedBuilder.Timestamp = DateTimeOffset.Now;
                if (originalEmbed.Image.HasValue) embedBuilder.WithImageUrl(originalEmbed.Image.Value.Url);
                // 保留原本 field（提示欄位），但跳過之前已加的猜測結果 field
                foreach (var field in originalEmbed.Fields)
                    embedBuilder.AddField(field.Name, field.Value, field.Inline);
            }
            else
            {
                embedBuilder.Timestamp = DateTimeOffset.Now;
            }

            if (isCorrect)
            {
                embedBuilder.Color = Color.Green;
                embedBuilder.AddField($"✅ 宅斃了: {correctName}",
                    $"恭喜 宅王之王 **{interaction.User.Mention}** 答對了！ 獎勵你 \n\n{await RewardsHelpers.GetRandomRewards(interaction.Channel, interaction.User as SocketGuildUser)}");
                return (embedBuilder.Build(), new ComponentBuilder());
            }

            if (isFirstAttempt)
            {
                // 第一次猜錯：給一次機會，把原本按鈕改成 r2 前綴繼續
                embedBuilder.Color = Color.Orange;
                embedBuilder.AddField("⚠️ 答錯了！還有一次機會", $"{interaction.User.Mention} 猜錯惹，再想想看？");

                // 從原訊息的按鈕重建 r2 版本
                var r2Component = new ComponentBuilder();
                int btnIdx = 0;
                foreach (var actionRow in interaction.Message.Components.OfType<ActionRowComponent>())
                {
                    foreach (var btn in actionRow.Components.OfType<ButtonComponent>())
                    {
                        string newId = btn.CustomId.Replace("anime_guess_", "anime_guess_r2_");
                        r2Component.WithButton(btn.Label, newId, ButtonStyle.Secondary, row: btnIdx / 3);
                        btnIdx++;
                    }
                }
                return (embedBuilder.Build(), r2Component);
            }
            else
            {
                // 第二次猜錯：遊戲結束，暴雷
                embedBuilder.Color = Color.Red;
                embedBuilder.AddField("❌ 菜逼八（兩次都錯）",
                    $"{interaction.User.Mention} 這你都不認識？ 正確答案是：||{correctName}||");
                return (embedBuilder.Build(), new ComponentBuilder());
            }
        }

        #region 代餐（jikan-edge.lucas-hdo.workers.dev/v1）

        // ── CTA v1 ────────────────────────────────────────────────────────
        private async Task<(ComponentBuilder component, Embed embed)> StartCharacterToAnimeGameAsync(bool isTop)
        {
            var chara = await GetCharacterAsync(isTop);
            if (chara == null)
                return CommonHelper.BuildErrorResponse("無法獲取角色資料，請稍後再試");
            if (chara.animeography == null || chara.animeography.Count == 0)
                return CommonHelper.BuildErrorResponse($"角色 {chara.name} 沒有動畫記錄，請重試");

            var correctAnime = chara.animeography[0];
            var (options, correctId, correctName) = await GetAnimeOptionsForCharacterAsync(correctAnime.malId, correctAnime.title);
            if (options == null || options.Count == 0)
                return CommonHelper.BuildErrorResponse("無法取得動畫選項，請重試");

            return (BuildAnimeOptionsComponent(options, correctId, correctName), BuildCharacterToAnimeEmbed(chara));
        }

        // ── CTC v1 ────────────────────────────────────────────────────────
        private async Task<(ComponentBuilder component, Embed embed)> StartCharacterToCharacterGameAsync(bool isTop)
        {
            var correctChar = await GetCharacterAsync(isTop);
            if (correctChar == null)
                return CommonHelper.BuildErrorResponse("無法獲取角色資料");

            var opts = new List<CharactersV1Resopnse> { correctChar };
            int retries = 0;
            while (opts.Count < 6 && retries < 30)
            {
                retries++;
                var rnd = await GetCharacterAsync(false);
                if (rnd != null && rnd.malId != correctChar.malId) opts.Add(rnd);
                else await Task.Delay(300);
            }
            if (opts.Count < 4)
                return CommonHelper.BuildErrorResponse("無法湊齊足夠的角色選項，請重試");

            opts = opts.OrderBy(_ => Guid.NewGuid()).ToList();
            return (BuildCharacterOptionsComponent(opts, correctChar.malId, correctChar.name), BuildCharacterToCharacterEmbed(correctChar));
        }

        // ── GetCharacterAsync v1 ──────────────────────────────────────────
        private async Task<CharactersV1Resopnse> GetCharacterAsync(bool isTop)
        {
            try
            {
                var rng = new Random();
                int page = isTop ? rng.Next(1, 6) : rng.Next(1, 51);
                string url = $"{API_BASE_URL}/top/characters?page={page}";
                Console.WriteLine($"[Jikan] GetCharacterAsync: GET {url}");

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Jikan] GetCharacterAsync: HTTP {(int)response.StatusCode} from {url}");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var wrapper = JsonConvert.DeserializeObject<TopCharactersV1Response>(content);
                if (wrapper?.data == null || wrapper.data.Count == 0)
                {
                    Console.WriteLine($"[Jikan] GetCharacterAsync: data null/empty. Raw: {content[..Math.Min(200, content.Length)]}");
                    return null;
                }

                var result = wrapper.data[rng.Next(wrapper.data.Count)];
                Console.WriteLine($"[Jikan] GetCharacterAsync: 抽到 {result.name} (malId={result.malId})");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Jikan] GetCharacterAsync 例外: {ex.Message}");
                return null;
            }
        }

        // ── GetAnimeOptionsForCharacterAsync v1 ───────────────────────────
        private async Task<(List<AnimeV1Response> options, int correctId, string correctName)> GetAnimeOptionsForCharacterAsync(
            int correctMalId, string correctTitle)
        {
            var correctEntry = new AnimeV1Response { malId = correctMalId, title = correctTitle };
            var options = new List<AnimeV1Response> { correctEntry };
            var rng = new Random();

            // /top/anime 不需要 filter 參數，先抓第 1 頁建候選池並取得 totalPages
            int totalPages = 1;
            try
            {
                var firstResp = await _httpClient.GetAsync($"{API_BASE_URL}/top/anime?page=1");
                if (firstResp.IsSuccessStatusCode)
                {
                    var firstJson = await firstResp.Content.ReadAsStringAsync();
                    var firstWrap = JsonConvert.DeserializeObject<JikanAnimeDevV1>(firstJson);
                    if (firstWrap?.data != null)
                    {
                        foreach (var a in firstWrap.data)
                            if (a != null && a.malId != correctMalId && !options.Any(o => o.malId == a.malId))
                                options.Add(a);

                        if (firstWrap.meta?.pagination?.total != null && firstWrap.meta.pagination.limit > 0)
                            totalPages = Math.Max(1, firstWrap.meta.pagination.total.Value / firstWrap.meta.pagination.limit);
                        else if (firstWrap.meta?.pagination?.hasNextPage == true)
                            totalPages = 20;
                    }
                    Console.WriteLine($"[Jikan] GetAnimeOptions: totalPages={totalPages}, pool after page1={options.Count}");
                }
                else
                    Console.WriteLine($"[Jikan] GetAnimeOptions page1: HTTP {(int)firstResp.StatusCode}");
            }
            catch (Exception ex) { Console.WriteLine($"[Jikan] GetAnimeOptions page1 例外: {ex.Message}"); }

            int retries = 0;
            while (options.Count < 6 && retries < 10)
            {
                retries++;
                try
                {
                    int page = rng.Next(1, totalPages + 1);
                    string url = $"{API_BASE_URL}/top/anime?page={page}";
                    var response = await _httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[Jikan] GetAnimeOptions: HTTP {(int)response.StatusCode} page={page}");
                        await Task.Delay(300); continue;
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    var wrapper = JsonConvert.DeserializeObject<JikanAnimeDevV1>(content);
                    if (wrapper?.data == null || wrapper.data.Count == 0) { await Task.Delay(300); continue; }

                    foreach (var a in wrapper.data)
                    {
                        if (a != null && a.malId != correctMalId && !options.Any(o => o.malId == a.malId))
                            options.Add(a);
                        if (options.Count >= 6) break;
                    }
                    Console.WriteLine($"[Jikan] GetAnimeOptions: retry={retries}, pool={options.Count}");
                }
                catch (Exception ex) { Console.WriteLine($"[Jikan] GetAnimeOptions loop 例外: {ex.Message}"); }
                await Task.Delay(200);
            }

            // 取 6 個打亂，確保選項夠多才顯示
            var pool = options.OrderBy(_ => Guid.NewGuid()).Take(6).ToList();
            Console.WriteLine($"[Jikan] GetAnimeOptions: 最終選項數={pool.Count}");
            return (pool, correctMalId, correctTitle);
        }

        // ── Buttons v1 ────────────────────────────────────────────────────
        private ComponentBuilder BuildAnimeOptionsComponent(List<AnimeV1Response> options, int correctId, string correctName)
        {
            var builder = new ComponentBuilder();
            for (int i = 0; i < Math.Min(options.Count, 6); i++)
            {
                string label = options[i].title ?? $"(ID:{options[i].malId})";
                if (label.Length > 80) label = label[..77] + "...";
                builder.WithButton(label: label, customId: $"anime_guess_{options[i].malId}_{correctId}_{correctName}", style: ButtonStyle.Primary, row: i / 3);
            }
            return builder;
        }

        private ComponentBuilder BuildCharacterOptionsComponent(List<CharactersV1Resopnse> chars, int correctId, string correctName)
        {
            var builder = new ComponentBuilder();
            for (int i = 0; i < Math.Min(chars.Count, 6); i++)
            {
                var c = chars[i];
                string label = string.IsNullOrEmpty(c.nameKanji) ? c.name : $"{c.name} / {c.nameKanji}";
                if (label.Length > 80) label = label[..77] + "...";
                builder.WithButton(label: label, customId: $"anime_guess_{c.malId}_{correctId}_{correctName}", style: ButtonStyle.Primary, row: i / 3);
            }
            return builder;
        }

        // ── Embeds v1 ─────────────────────────────────────────────────────
        private Embed BuildCharacterToAnimeEmbed(CharactersV1Resopnse chara)
        {
            var eb = new EmbedBuilder()
            {
                Title = "這哪部動畫來的？",
                Description = $"**角色名稱**: {chara.name}" + (string.IsNullOrEmpty(chara.nameKanji) ? "" : $"\n**原名**: {chara.nameKanji}"),
                Color = Color.Blue
            };
            if (!string.IsNullOrEmpty(chara.imageUrl)) eb.WithImageUrl(chara.imageUrl);
            if (chara.animeography != null && chara.animeography.Count > 1)
            {
                string hint = string.Join("、", chara.animeography.Skip(1).Take(3).Select(a => a.title));
                if (!string.IsNullOrEmpty(hint)) eb.AddField("提示：也出現在", $"||{hint}||");
            }
            eb.WithFooter("請從下方按鈕選擇正確答案");
            eb.WithCurrentTimestamp();
            return eb.Build();
        }

        private Embed BuildCharacterToCharacterEmbed(CharactersV1Resopnse chara)
        {
            var eb = new EmbedBuilder() { Title = "這誰？", Description = "根據圖片猜測角色名稱", Color = Color.Gold };
            if (!string.IsNullOrEmpty(chara.imageUrl)) eb.WithImageUrl(chara.imageUrl);
            if (chara.animeography != null && chara.animeography.Count > 0)
                eb.AddField("提示", $"出現在：||{string.Join("、", chara.animeography.Take(2).Select(a => a.title))}||");
            eb.WithFooter("請從下方按鈕選擇正確答案");
            eb.WithCurrentTimestamp();
            return eb.Build();
        }

        // ── Random anime / manga v1 ───────────────────────────────────────
        public async Task<((ComponentBuilder component, Embed embed), string imageUrl)> GetSomeRandomAnime(string type, string ratings)
        {
            string imageUrl = "";
            try
            {
                string url = $"{API_BASE_URL}/anime?";
                if (!string.IsNullOrEmpty(type))    url += $"type={type}&";
                if (!string.IsNullOrEmpty(ratings)) url += $"rating={ratings}&";
                url = url.TrimEnd('&', '?');

                Console.WriteLine($"[Jikan] GetSomeRandomAnime: GET {url}");
                var meta = await GetPageMetaAsync(url);

                // total が null の場合は fallback 10 ページ
                int totalPages = (meta?.pagination?.total != null && meta.pagination.total > 0 && meta.pagination.limit > 0)
                    ? meta.pagination.total.Value / meta.pagination.limit
                    : 10;
                totalPages = Math.Max(1, totalPages);

                int page = new Random().Next(1, totalPages + 1);
                string pagedUrl = $"{url}&page={page}";
                Console.WriteLine($"[Jikan] GetSomeRandomAnime: paged GET {pagedUrl} (totalPages={totalPages})");

                var response = await _httpClient.GetAsync(pagedUrl);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Jikan] GetSomeRandomAnime: HTTP {(int)response.StatusCode}");
                    return (CommonHelper.BuildErrorResponse("無法獲取動畫資料"), "");
                }

                var content = await response.Content.ReadAsStringAsync();
                var wrapper = JsonConvert.DeserializeObject<JikanAnimeDevV1>(content);
                if (wrapper?.data == null || wrapper.data.Count == 0)
                {
                    Console.WriteLine($"[Jikan] GetSomeRandomAnime: data null/empty. Raw: {content[..Math.Min(300, content.Length)]}");
                    return (CommonHelper.BuildErrorResponse("沒有符合條件的動畫資料"), "");
                }

                var anime = wrapper.data[new Random().Next(wrapper.data.Count)];
                Console.WriteLine($"[Jikan] GetSomeRandomAnime: 抽到 {anime.title} (malId={anime.malId})");

                string description = $"分數：{anime.score?.ToString("F2") ?? "N/A"}\n簡介：{anime.synopsis ?? "沒有簡介"}";
                if (description.Length > 200) description = description[..197] + "...";

                var eb = new EmbedBuilder() { Title = anime.title ?? "(無標題)", Description = description, Color = Color.Purple };
                imageUrl = anime.imageUrl ?? "";
                if (!string.IsNullOrEmpty(imageUrl) && string.IsNullOrEmpty(ratings))
                    eb.WithImageUrl(imageUrl);

                return ((new ComponentBuilder(), eb.Build()), imageUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Jikan] GetSomeRandomAnime 例外: {ex}");
                return (CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}"), imageUrl);
            }
        }

        public async Task<((ComponentBuilder component, Embed embed), string imageUrl)> GetSomeRandomManga(string type, string genres)
        {
            string imageUrl = "";
            try
            {
                string url = $"{API_BASE_URL}/manga?";
                if (!string.IsNullOrEmpty(type))   url += $"type={type}&";
                if (!string.IsNullOrEmpty(genres)) url += $"genres={genres}&";
                url = url.TrimEnd('&', '?');

                Console.WriteLine($"[Jikan] GetSomeRandomManga: GET {url}");
                var meta = await GetPageMetaAsync(url);

                int totalPages = (meta?.pagination?.total != null && meta.pagination.total > 0 && meta.pagination.limit > 0)
                    ? meta.pagination.total.Value / meta.pagination.limit
                    : 10;
                totalPages = Math.Max(1, totalPages);

                int page = new Random().Next(1, totalPages + 1);
                string pagedUrl = $"{url}&page={page}";
                Console.WriteLine($"[Jikan] GetSomeRandomManga: paged GET {pagedUrl} (totalPages={totalPages})");

                var response = await _httpClient.GetAsync(pagedUrl);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Jikan] GetSomeRandomManga: HTTP {(int)response.StatusCode}");
                    return (CommonHelper.BuildErrorResponse("無法獲取漫畫資料"), "");
                }

                var content = await response.Content.ReadAsStringAsync();
                var wrapper = JsonConvert.DeserializeObject<JikanMangaDevV1>(content);
                if (wrapper?.data == null || wrapper.data.Count == 0)
                {
                    Console.WriteLine($"[Jikan] GetSomeRandomManga: data null/empty. Raw: {content[..Math.Min(300, content.Length)]}");
                    return (CommonHelper.BuildErrorResponse("沒有符合條件的漫畫資料"), "");
                }

                var manga = wrapper.data[new Random().Next(wrapper.data.Count)];
                Console.WriteLine($"[Jikan] GetSomeRandomManga: 抽到 {manga.title} (malId={manga.malId})");

                string description = $"分數：{manga.score?.ToString("F2") ?? "N/A"}\n簡介：{manga.synopsis ?? "沒有簡介"}";
                if (description.Length > 200) description = description[..197] + "...";

                var eb = new EmbedBuilder() { Title = manga.title ?? "(無標題)", Description = description, Color = Color.Purple };
                imageUrl = manga.imageUrl ?? "";

                return ((new ComponentBuilder(), eb.Build()), imageUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Jikan] GetSomeRandomManga 例外: {ex}");
                return (CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}"), imageUrl);
            }
        }

        // ── GetPageMetaAsync（取 pagination，不另浪費資料）─────────────────
        private async Task<MetaV1> GetPageMetaAsync(string baseUrl)
        {
            try
            {
                var response = await _httpClient.GetAsync(baseUrl);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Jikan] GetPageMetaAsync: HTTP {(int)response.StatusCode} from {baseUrl}");
                    return null;
                }
                var content = await response.Content.ReadAsStringAsync();
                var wrapper = JsonConvert.DeserializeObject<JikanAnimeDevV1>(content);
                if (wrapper?.meta == null)
                    Console.WriteLine($"[Jikan] GetPageMetaAsync: meta null. Raw: {content[..Math.Min(200, content.Length)]}");
                return wrapper?.meta;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Jikan] GetPageMetaAsync 例外: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}
