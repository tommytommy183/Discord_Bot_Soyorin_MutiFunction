using Discord;
using MusicBot2.Helpers;
using MusicBot2.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    /// <summary>
    /// FGO 猜謎遊戲：
    ///   模式 1 – 剪影猜角色（6 按鈕選項）
    ///   模式 2 – 看角色猜寶具名稱（6 按鈕選項）
    /// 資料來源：Atlas Academy API（TW 繁中）
    /// </summary>
    public class FgoGuessService
    {
        private readonly HttpClient _http;
        private readonly Random _rng = new();
        private readonly SemaphoreSlim _initLock = new(1, 1);

        // 從 basic_servant 快取整份從者清單
        private List<FgoBasicServant> _servantPool = new();
        // collectionNo → NP 名（避免重複 fetch）
        private readonly Dictionary<int, string> _npCache = new();

        private bool _initialized = false;

        // 遊戲狀態（per channel）
        private readonly Dictionary<ulong, FgoGuessState> _games = new();

        private const string BasicServantUrl =
            "https://api.atlasacademy.io/export/TW/basic_servant.json";
        private const string NiceServantUrl =
            "https://api.atlasacademy.io/nice/TW/servant/{0}?lore=false";

        public FgoGuessService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        }

        // ─────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>模式 1：猜角色名稱。回傳 embed + component（直接顯示原圖）。</summary>
        public async Task<(ComponentBuilder component, Embed embed)> StartSilhouetteGameAsync(ulong channelId)
        {
            try
            {
                await EnsureInitAsync();
                if (_servantPool.Count == 0)
                    return CommonHelper.BuildErrorResponse("無法取得 FGO 從者資料");

                // 選答案 + 5 個錯誤選項
                var answer = _servantPool[_rng.Next(_servantPool.Count)];
                var options = PickOptions(answer.Name, FgoGuessMode.Silhouette);

                // 取全圖 URL
                var (charaUrl, _) = await FetchServantAssetsAsync(answer.CollectionNo);
                if (charaUrl == null)
                    return CommonHelper.BuildErrorResponse("找不到角色圖片");

                // 儲存遊戲狀態
                _games[channelId] = new FgoGuessState
                {
                    ChannelId = channelId,
                    Mode = FgoGuessMode.Silhouette,
                    AnswerCollectionNo = answer.CollectionNo,
                    AnswerName = answer.Name,
                    Options = options
                };

                var embed = new EmbedBuilder()
                    .WithTitle("⚔️ FGO 猜謎 — 這位從者是誰？")
                    .WithDescription("猜猜圖中是哪位從者！\n請點擊下方按鈕作答。")
                    .WithColor(new Discord.Color(0xC8A800))
                    .WithImageUrl(charaUrl)
                    .WithFooter($"共 {_servantPool.Count} 位從者可出題")
                    .WithCurrentTimestamp()
                    .Build();

                var component = BuildOptionButtons(channelId, options, answer.Name, isNpMode: false);
                return (component, embed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FgoGuess] StartSilhouette error: {ex.Message}");
                return CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}");
            }
        }

        /// <summary>模式 3：猜從者升階（顯示某一階段圖，猜是第幾階段）。</summary>
        public async Task<(ComponentBuilder component, Embed embed)> StartAscensionGameAsync(ulong channelId)
        {
            try
            {
                await EnsureInitAsync();
                if (_servantPool.Count == 0)
                    return CommonHelper.BuildErrorResponse("無法取得 FGO 從者資料");

                // 反覆抽，直到找到有至少 2 個升階圖的從者（只有 1 張猜不了）
                FgoBasicServant answer = null;
                Dictionary<string, string> ascImages = null;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    var candidate = _servantPool[_rng.Next(_servantPool.Count)];
                    var imgs = await FetchAscensionImagesAsync(candidate.CollectionNo);
                    if (imgs != null && imgs.Count >= 2)
                    {
                        answer = candidate;
                        ascImages = imgs;
                        break;
                    }
                }

                if (answer == null)
                    return CommonHelper.BuildErrorResponse("找不到有效的升階圖資料，請稍後再試");

                // 從可用階段中隨機挑一張
                var availableKeys = ascImages.Keys.OrderBy(k => k).ToList();
                var chosenKey = availableKeys[_rng.Next(availableKeys.Count)];
                int chosenStage = int.TryParse(chosenKey, out int s) ? s : 1;
                string imageUrl = ascImages[chosenKey];

                // 選項：只列出這位從者「實際有圖的」階段，不足 4 個也沒關係
                var options = availableKeys.Select(k =>
                    StageLabel(int.TryParse(k, out int n) ? n : 0)).ToList();

                _games[channelId] = new FgoGuessState
                {
                    ChannelId = channelId,
                    Mode = FgoGuessMode.Ascension,
                    AnswerCollectionNo = answer.CollectionNo,
                    AnswerName = answer.Name,
                    AnswerAscensionStage = chosenStage,
                    CharaImageUrl = imageUrl,
                    Options = options
                };

                string classEmoji = ClassEmoji(answer.ClassName);
                string rarityStars = new string('★', answer.Rarity);

                var embed = new EmbedBuilder()
                    .WithTitle("⚔️ FGO 猜謎 — 這是第幾階段？")
                    .WithDescription($"{classEmoji} **{answer.Name}**\n{rarityStars}\n\n這張圖是哪個升階階段？")
                    .WithColor(new Discord.Color(0x3A7BDD))
                    .WithImageUrl(imageUrl)
                    .WithFooter("請點擊下方按鈕作答")
                    .WithCurrentTimestamp()
                    .Build();

                var component = BuildAscensionButtons(channelId, options, chosenStage);
                return (component, embed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FgoGuess] Ascension game error: {ex.Message}");
                return CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}");
            }
        }

        /// <summary>模式 2：看角色猜寶具名稱。</summary>
        public async Task<(ComponentBuilder component, Embed embed)> StartNpGameAsync(ulong channelId)
        {
            try
            {
                await EnsureInitAsync();
                if (_servantPool.Count == 0)
                    return CommonHelper.BuildErrorResponse("無法取得 FGO 從者資料");

                // 找一位有 NP 名的從者當答案
                FgoBasicServant answer = null;
                string npName = null;
                string charaUrl = null;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    var candidate = _servantPool[_rng.Next(_servantPool.Count)];
                    var (url, np) = await FetchServantAssetsAsync(candidate.CollectionNo);
                    if (!string.IsNullOrWhiteSpace(np) && url != null)
                    {
                        answer = candidate;
                        npName = np;
                        charaUrl = url;
                        break;
                    }
                }

                if (answer == null)
                    return CommonHelper.BuildErrorResponse("找不到有效的寶具資料，請稍後再試");

                // 現抓 5 個其他從者的寶具名作錯誤選項（不依賴預載快取）
                var wrongNps = new List<string>();
                var tried = new HashSet<int> { answer.CollectionNo };
                int maxTries = 40;
                while (wrongNps.Count < 5 && maxTries-- > 0)
                {
                    var c = _servantPool[_rng.Next(_servantPool.Count)];
                    if (!tried.Add(c.CollectionNo)) continue;

                    // 先查快取，再 fetch
                    string wp;
                    if (_npCache.TryGetValue(c.CollectionNo, out var cached))
                        wp = cached;
                    else
                    {
                        var (_, wn) = await FetchServantAssetsAsync(c.CollectionNo);
                        wp = wn;
                    }

                    if (!string.IsNullOrWhiteSpace(wp) && wp != npName && !wrongNps.Contains(wp))
                        wrongNps.Add(wp);
                }

                // 若湊不夠 5 個就用現有的
                var options = wrongNps.Append(npName).OrderBy(_ => _rng.Next()).ToList();

                _games[channelId] = new FgoGuessState
                {
                    ChannelId = channelId,
                    Mode = FgoGuessMode.NoblePhantasm,
                    AnswerCollectionNo = answer.CollectionNo,
                    AnswerName = answer.Name,
                    AnswerNpName = npName,
                    CharaImageUrl = charaUrl,
                    Options = options
                };

                // 類別 emoji
                string classEmoji = ClassEmoji(answer.ClassName);
                string rarityStars = new string('★', answer.Rarity);

                var embed = new EmbedBuilder()
                    .WithTitle($"⚔️ FGO 猜謎 — {answer.Name}的寶具名稱是？")
                    .WithDescription($"{classEmoji} **{answer.Name}**\n{rarityStars}\n\n這位從者的寶具是？")
                    .WithColor(new Discord.Color(0xBF2020))
                    .WithImageUrl(charaUrl)
                    .WithFooter("請點擊下方按鈕選擇正確的寶具名")
                    .WithCurrentTimestamp()
                    .Build();

                var component = BuildOptionButtons(channelId, options, npName, isNpMode: true);
                return (component, embed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FgoGuess] NP game error: {ex.Message}");
                return CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}");
            }
        }

        /// <summary>處理玩家點按鈕作答（一次性，任何作答後立刻結束並揭曉答案）。</summary>
        public Task<(Embed embed, ComponentBuilder component)> HandleAnswerAsync(
            ulong channelId, ulong userId, string userName, int optionIndex)
        {
            if (!_games.TryGetValue(channelId, out var state))
                return Task.FromResult((CommonHelper.BuildErrorResponse("找不到進行中的遊戲").Item2, new ComponentBuilder()));

            if (optionIndex < 0 || optionIndex >= state.Options.Count)
                return Task.FromResult((CommonHelper.BuildErrorResponse("無效的選項").Item2, new ComponentBuilder()));

            // 任何回答都立刻結束遊戲
            _games.Remove(channelId);

            var selected = state.Options[optionIndex];
            string correctAnswer = state.Mode switch
            {
                FgoGuessMode.NoblePhantasm => state.AnswerNpName,
                FgoGuessMode.Ascension     => StageLabel(state.AnswerAscensionStage),
                _                          => state.AnswerName
            };
            bool isCorrect = selected == correctAnswer;

            string classEmoji = ClassEmoji(_servantPool.FirstOrDefault(s => s.CollectionNo == state.AnswerCollectionNo)?.ClassName ?? "");

            EmbedBuilder resultEmbed;
            if (isCorrect)
            {
                string desc = state.Mode switch
                {
                    FgoGuessMode.NoblePhantasm =>
                        $"**{userName}** 答對了！\n{classEmoji} **{state.AnswerName}** 的寶具是\n「**{state.AnswerNpName}**」",
                    FgoGuessMode.Ascension =>
                        $"**{userName}** 答對了！\n這張正是 {classEmoji} **{state.AnswerName}** 的{StageLabel(state.AnswerAscensionStage)}！",
                    _ =>
                        $"**{userName}** 答對了！\n這位從者正是 {classEmoji} **{state.AnswerName}**！"
                };
                resultEmbed = new EmbedBuilder()
                    .WithTitle("✅ 答對了！")
                    .WithDescription(desc)
                    .WithColor(Discord.Color.Green)
                    .WithImageUrl(state.CharaImageUrl)
                    .WithCurrentTimestamp();
            }
            else
            {
                string wrongDesc = state.Mode switch
                {
                    FgoGuessMode.NoblePhantasm =>
                        $"**{userName}** 答了「{selected}」❌\n正確答案是 {classEmoji} **{state.AnswerName}** 的寶具\n「**{state.AnswerNpName}**」",
                    FgoGuessMode.Ascension =>
                        $"**{userName}** 答了「{selected}」❌\n正確答案是 {classEmoji} **{state.AnswerName}** 的{StageLabel(state.AnswerAscensionStage)}",
                    _ =>
                        $"**{userName}** 答了「{selected}」❌\n正確答案是 {classEmoji} **{state.AnswerName}**！"
                };
                resultEmbed = new EmbedBuilder()
                    .WithTitle("❌ 答錯了！")
                    .WithDescription(wrongDesc)
                    .WithColor(Discord.Color.Red)
                    .WithImageUrl(state.CharaImageUrl)  // 揭曉正確圖片
                    .WithCurrentTimestamp();
            }

            return Task.FromResult((resultEmbed.Build(), new ComponentBuilder()));
        }

        /// <summary>強制結束當前頻道的猜謎遊戲，顯示答案。</summary>
        public (Embed embed, ComponentBuilder component) GiveUpGame(ulong channelId)
        {
            if (!_games.TryGetValue(channelId, out var state))
                return (CommonHelper.BuildErrorResponse("此頻道沒有進行中的猜謎遊戲").Item2, new ComponentBuilder());

            _games.Remove(channelId);
            string classEmoji = ClassEmoji(_servantPool.FirstOrDefault(s => s.CollectionNo == state.AnswerCollectionNo)?.ClassName ?? "");

            string desc = state.Mode switch
            {
                FgoGuessMode.NoblePhantasm =>
                    $"沒人猜出來～\n{classEmoji} **{state.AnswerName}** 的寶具是「**{state.AnswerNpName}**」",
                FgoGuessMode.Ascension =>
                    $"沒人猜出來～\n正確答案是 {classEmoji} **{state.AnswerName}** 的{StageLabel(state.AnswerAscensionStage)}！",
                _ =>
                    $"沒人猜出來～\n正確答案是 {classEmoji} **{state.AnswerName}**！"
            };

            var embed = new EmbedBuilder()
                .WithTitle("🏳️ 公布答案")
                .WithDescription(desc)
                .WithColor(Discord.Color.Gold)
                .WithImageUrl(state.CharaImageUrl)
                .WithCurrentTimestamp()
                .Build();

            return (embed, new ComponentBuilder());
        }

        // ─────────────────────────────────────────────────────────────────
        //  Private helpers
        // ─────────────────────────────────────────────────────────────────

        private async Task EnsureInitAsync()
        {
            if (_initialized) return;
            await _initLock.WaitAsync();
            try
            {
                if (_initialized) return;
                await LoadServantPoolAsync();
                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        // 預先快取一批 NP 名，供「猜寶具」模式產生錯誤選項用
        private const string NiceServantExportUrl =
            "https://api.atlasacademy.io/export/TW/nice_servant.json";

        private async Task LoadServantPoolAsync()
        {
            try
            {
                // 1. 載入基本從者清單
                var json = await _http.GetStringAsync(BasicServantUrl);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var all = JsonSerializer.Deserialize<List<FgoBasicServant>>(json, opts);
                _servantPool = all?
                    .Where(s => s.CollectionNo > 0 && s.Id < 1_000_000 && !string.IsNullOrEmpty(s.Name))
                    .OrderBy(s => s.CollectionNo)
                    .ToList() ?? new();

                Console.WriteLine($"[FgoGuess] 載入 {_servantPool.Count} 位從者");

                // 2. 異步預載 NP 名快取（從 nice export，不阻塞啟動）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Console.WriteLine("[FgoGuess] 開始預載 NP 快取...");
                        var niceJson = await _http.GetStringAsync(NiceServantExportUrl);
                        var niceOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var niceAll = JsonSerializer.Deserialize<List<FgoNiceServant>>(niceJson, niceOpts);
                        if (niceAll == null) return;

                        foreach (var svt in niceAll)
                        {
                            var np = svt.NoblePhantasms?
                                .Where(n => !string.IsNullOrWhiteSpace(n.Name))
                                .OrderByDescending(n => n.Num)
                                .Select(n => n.Name)
                                .FirstOrDefault();
                            if (np != null)
                                _npCache[svt.CollectionNo] = np;
                        }
                        Console.WriteLine($"[FgoGuess] NP 快取預載完成，共 {_npCache.Count} 筆");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FgoGuess] NP 快取預載失敗（不影響遊戲）: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FgoGuess] LoadPool 失敗: {ex.Message}");
            }
        }

        /// <summary>取得全圖 URL 與 NP 名（帶快取）。</summary>
        private async Task<(string charaUrl, string npName)> FetchServantAssetsAsync(int collectionNo)
        {
            try
            {
                var url = string.Format(NiceServantUrl, collectionNo);
                var json = await _http.GetStringAsync(url);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<FgoNiceServant>(json, opts);
                if (data == null) return (null, null);

                // 取最後升階的全身圖
                string charaUrl = data.ExtraAssets?.CharaGraph?.Ascension?
                    .OrderBy(kv => kv.Key)
                    .Select(kv => kv.Value)
                    .LastOrDefault();

                // 取最新版本的 NP（num 最大）
                string npName = data.NoblePhantasms?
                    .Where(np => !string.IsNullOrWhiteSpace(np.Name))
                    .OrderByDescending(np => np.Num)
                    .Select(np => np.Name)
                    .FirstOrDefault();

                // 快取 NP 名
                if (!string.IsNullOrWhiteSpace(npName))
                    _npCache[collectionNo] = npName;

                return (charaUrl, npName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FgoGuess] FetchAssets({collectionNo}) 失敗: {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>取得某從者所有升階全身圖（key = "1"~"4"）。</summary>
        private async Task<Dictionary<string, string>> FetchAscensionImagesAsync(int collectionNo)
        {
            try
            {
                var url = string.Format(NiceServantUrl, collectionNo);
                var json = await _http.GetStringAsync(url);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<FgoNiceServant>(json, opts);

                var asc = data?.ExtraAssets?.CharaGraph?.Ascension;
                if (asc == null || asc.Count == 0) return null;

                // 只保留 key 為純數字且有實際 URL 的項目
                return asc
                    .Where(kv => int.TryParse(kv.Key, out _) && !string.IsNullOrWhiteSpace(kv.Value))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }
            catch
            {
                return null;
            }
        }

        private static string StageLabel(int stage) => stage switch
        {
            1 => "第一階段",
            2 => "第二階段",
            3 => "第三階段",
            4 => "第四階段",
            _ => $"第{stage}階段"
        };

        private ComponentBuilder BuildAscensionButtons(ulong channelId, List<string> options, int correctStage)
        {
            var cb = new ComponentBuilder();
            for (int i = 0; i < options.Count && i < 4; i++)
            {
                cb.WithButton(
                    label: options[i],
                    customId: $"fgo_guess_{channelId}_{i}",
                    style: ButtonStyle.Primary,
                    row: i / 2  // 每行最多 2 個，擺 2 行
                );
            }
            return cb;
        }

        /// <summary>
        /// 選出 6 個選項（含正確答案）。
        /// Silhouette 模式：選項是角色名；NP 模式：選項是寶具名。
        /// </summary>
        private List<string> PickOptions(string correctAnswer, FgoGuessMode mode)
        {
            List<string> pool;
            if (mode == FgoGuessMode.Silhouette)
            {
                pool = _servantPool
                    .Select(s => s.Name)
                    .Where(n => n != correctAnswer)
                    .Distinct()
                    .ToList();
            }
            else
            {
                // NP 模式：從快取中拿其他人的寶具名作選項
                pool = _npCache.Values
                    .Where(n => n != correctAnswer)
                    .Distinct()
                    .ToList();

                // 快取不夠就用角色名湊數（不理想，但不會崩潰）
                if (pool.Count < 5)
                {
                    pool.AddRange(_servantPool
                        .Select(s => s.Name)
                        .Where(n => n != correctAnswer && !pool.Contains(n)));
                }
            }

            // 隨機取 5 個錯誤選項
            var wrong = pool.OrderBy(_ => _rng.Next()).Take(5).ToList();
            var all = wrong.Append(correctAnswer).OrderBy(_ => _rng.Next()).ToList();
            return all;
        }

        private ComponentBuilder BuildOptionButtons(ulong channelId, List<string> options, string correctAnswer, bool isNpMode)
        {
            var cb = new ComponentBuilder();
            for (int i = 0; i < options.Count && i < 6; i++)
            {
                string label = options[i];
                if (label.Length > 80) label = label[..77] + "…";

                cb.WithButton(
                    label: label,
                    customId: $"fgo_guess_{channelId}_{i}",
                    style: ButtonStyle.Primary,
                    row: i / 3  // 0~2 行 0，3~5 行 1
                );
            }
            return cb;
        }

        /// <summary>使用 SkiaSharp 將角色圖轉為黑色剪影（保留透明）。</summary>
        private async Task<Stream> MakeSilhouetteAsync(string imageUrl)
        {
            var bytes = await _http.GetByteArrayAsync(imageUrl);
            using var original = SKBitmap.Decode(bytes);
            using var result = new SKBitmap(original.Width, original.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

            for (int y = 0; y < original.Height; y++)
            {
                for (int x = 0; x < original.Width; x++)
                {
                    var p = original.GetPixel(x, y);
                    if (p.Alpha < 20)
                        result.SetPixel(x, y, SKColors.Transparent);
                    else
                        result.SetPixel(x, y, new SKColor(0, 0, 0, p.Alpha));
                }
            }

            using var image = SKImage.FromBitmap(result);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            var ms = new MemoryStream();
            encoded.SaveTo(ms);
            ms.Position = 0;
            return ms;
        }

        private static string ClassEmoji(string className) => className?.ToLower() switch
        {
            "saber"      => "⚔️",
            "archer"     => "🏹",
            "lancer"     => "🔱",
            "rider"      => "🐴",
            "caster"     => "🔮",
            "assassin"   => "🗡️",
            "berserker"  => "💢",
            "ruler"      => "⚖️",
            "avenger"    => "🔥",
            "moonCancer" => "🌙",
            "alterEgo"   => "🌀",
            "foreigner"  => "🌌",
            "pretender"  => "🎭",
            "shielder"   => "🛡️",
            _            => "✨"
        };
    }
}
