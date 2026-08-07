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

        /// <summary>模式 1：剪影猜角色。回傳 embed + component + 剪影圖片 Stream。</summary>
        public async Task<((ComponentBuilder component, Embed embed), Stream silhouette)> StartSilhouetteGameAsync(ulong channelId)
        {
            try
            {
                await EnsureInitAsync();
                if (_servantPool.Count == 0)
                    return (CommonHelper.BuildErrorResponse("無法取得 FGO 從者資料"), null);

                // 選答案 + 5 個錯誤選項
                var answer = _servantPool[_rng.Next(_servantPool.Count)];
                var options = PickOptions(answer.Name, FgoGuessMode.Silhouette);

                // 取全圖 URL
                var (charaUrl, _) = await FetchServantAssetsAsync(answer.CollectionNo);
                if (charaUrl == null)
                    return (CommonHelper.BuildErrorResponse("找不到角色圖片"), null);

                // 儲存遊戲狀態
                _games[channelId] = new FgoGuessState
                {
                    ChannelId = channelId,
                    Mode = FgoGuessMode.Silhouette,
                    AnswerCollectionNo = answer.CollectionNo,
                    AnswerName = answer.Name,
                    Options = options
                };

                // 剪影圖
                Stream silhouette;
                try
                {
                    silhouette = await MakeSilhouetteAsync(charaUrl);
                }
                catch
                {
                    silhouette = null;
                }

                var embed = new EmbedBuilder()
                    .WithTitle("⚔️ FGO 猜謎 — 這位從者是誰？")
                    .WithDescription("看看這個剪影，猜猜是哪位從者！\n請點擊下方按鈕作答。")
                    .WithColor(new Discord.Color(0xC8A800))
                    .WithImageUrl(silhouette != null ? "attachment://servant.png" : charaUrl)
                    .WithFooter($"共 {_servantPool.Count} 位從者可出題")
                    .WithCurrentTimestamp()
                    .Build();

                var component = BuildOptionButtons(channelId, options, answer.Name, isNpMode: false);
                return ((component, embed), silhouette);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FgoGuess] Silhouette error: {ex.Message}");
                return (CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}"), null);
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

                // 反覆抽，直到找到有 NP 名的從者
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

                // 6 個寶具名選項
                var options = PickOptions(npName, FgoGuessMode.NoblePhantasm);

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
                    .WithTitle($"⚔️ FGO 猜謎 — 這位從者的寶具名稱是？")
                    .WithDescription($"{classEmoji} **{answer.Name}**\n{rarityStars}\n\n猜猜這位從者的寶具叫什麼名字！")
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

        /// <summary>處理玩家點按鈕作答。</summary>
        public async Task<(Embed embed, ComponentBuilder component)> HandleAnswerAsync(
            ulong channelId, ulong userId, string userName, int optionIndex)
        {
            if (!_games.TryGetValue(channelId, out var state))
                return (CommonHelper.BuildErrorResponse("找不到進行中的遊戲").Item2, new ComponentBuilder());

            if (state.IsAnswered)
            {
                var alreadyEmbed = new EmbedBuilder()
                    .WithTitle("⚔️ FGO 猜謎")
                    .WithDescription("這題已經被答對了！使用指令開始下一題。")
                    .WithColor(Discord.Color.LightGrey)
                    .Build();
                return (alreadyEmbed, new ComponentBuilder());
            }

            if (optionIndex < 0 || optionIndex >= state.Options.Count)
                return (CommonHelper.BuildErrorResponse("無效的選項").Item2, new ComponentBuilder());

            var selected = state.Options[optionIndex];
            bool isNpMode = state.Mode == FgoGuessMode.NoblePhantasm;
            string correctAnswer = isNpMode ? state.AnswerNpName : state.AnswerName;
            bool isCorrect = selected == correctAnswer;

            if (isCorrect)
                state.IsAnswered = true;

            string classEmoji = ClassEmoji(_servantPool.FirstOrDefault(s => s.CollectionNo == state.AnswerCollectionNo)?.ClassName ?? "");

            EmbedBuilder resultEmbed;
            if (isCorrect)
            {
                resultEmbed = new EmbedBuilder()
                    .WithTitle("✅ 答對了！")
                    .WithDescription(isNpMode
                        ? $"**{userName}** 答對了！\n{classEmoji} **{state.AnswerName}** 的寶具是\n「**{state.AnswerNpName}**」"
                        : $"**{userName}** 答對了！\n這位從者正是 {classEmoji} **{state.AnswerName}**！")
                    .WithColor(Discord.Color.Green)
                    .WithImageUrl(state.CharaImageUrl)
                    .WithCurrentTimestamp();
            }
            else
            {
                resultEmbed = new EmbedBuilder()
                    .WithTitle("❌ 答錯了！")
                    .WithDescription($"**{userName}** 答了「{selected}」，但不正確！\n繼續猜猜看～")
                    .WithColor(Discord.Color.Red)
                    .WithCurrentTimestamp();
            }

            // 答對後移除遊戲狀態；答錯保留讓他人繼續
            if (isCorrect)
                _games.Remove(channelId);

            return (resultEmbed.Build(), new ComponentBuilder());
        }

        /// <summary>強制結束當前頻道的猜謎遊戲，顯示答案。</summary>
        public (Embed embed, ComponentBuilder component) GiveUpGame(ulong channelId)
        {
            if (!_games.TryGetValue(channelId, out var state))
                return (CommonHelper.BuildErrorResponse("此頻道沒有進行中的猜謎遊戲").Item2, new ComponentBuilder());

            _games.Remove(channelId);
            bool isNpMode = state.Mode == FgoGuessMode.NoblePhantasm;
            string classEmoji = ClassEmoji(_servantPool.FirstOrDefault(s => s.CollectionNo == state.AnswerCollectionNo)?.ClassName ?? "");

            var embed = new EmbedBuilder()
                .WithTitle("🏳️ 公布答案")
                .WithDescription(isNpMode
                    ? $"沒人猜出來～\n{classEmoji} **{state.AnswerName}** 的寶具是「**{state.AnswerNpName}**」"
                    : $"沒人猜出來～\n正確答案是 {classEmoji} **{state.AnswerName}**！")
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

        private async Task LoadServantPoolAsync()
        {
            try
            {
                var json = await _http.GetStringAsync(BasicServantUrl);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var all = JsonSerializer.Deserialize<List<FgoBasicServant>>(json, opts);
                // 只保留正式從者（collectionNo > 0，不含隱藏/測試角色 id > 1000000）
                _servantPool = all?
                    .Where(s => s.CollectionNo > 0 && s.Id < 1_000_000 && !string.IsNullOrEmpty(s.Name))
                    .OrderBy(s => s.CollectionNo)
                    .ToList() ?? new();

                Console.WriteLine($"[FgoGuess] 載入 {_servantPool.Count} 位從者");
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

                // 取升階 1 的全身圖
                string charaUrl = data.ExtraAssets?.CharaGraph?.Ascension?
                    .OrderBy(kv => kv.Key)
                    .Select(kv => kv.Value)
                    .FirstOrDefault();

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
