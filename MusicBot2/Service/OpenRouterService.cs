using Discord;
using Discord.WebSocket;
using ElevenLabs.Models;
using MusicBot2.Models;
using RiotSharp.Endpoints.StatusEndpoint;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace MusicBot2.Service
{
    /// <summary>
    /// 透過 OpenRouter 呼叫 Venice Uncensored (dolphin-mistral-24b-venice-edition:free)。
    /// 公開介面與 GoogleAIStudioService 對齊，方便直接替換。
    /// </summary>
    public class OpenRouterService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private readonly MediaWikiService _wikiService;
        private readonly string _memoryFilePath = Path.Combine("TxtFolder", "AI_Memory_OpenRouter.txt");
        private readonly string _summaryFilePath = Path.Combine("TxtFolder", "AI_Summary_OpenRouter.txt");

        // Redis 持久化
        private readonly IDatabase _redisDb;
        private readonly bool _useRedis;
        private const string HISTORIES_REDIS_KEY = "chat:all_histories";
        private const string SUMMARIES_REDIS_KEY = "chat:all_summaries";

        // 以「頻道」為單位分開存對話
        private Dictionary<string, List<ConversationMessage>> _channelHistories = new();
        private Dictionary<string, string> _channelSummaries = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private readonly HashSet<string> _summarizingChannels = new();

        private const int MaxRecentMessages = 8;
        private const int MaxTotalMessages = 10;
        private const int MaxContextChars = 2500;
        private const int SummarizeChunkSize = 20;
        private const int MaxMessageStoreLength = 300;

        // 依優先順序嘗試的模型 (免費；當主要 provider 被 upstream rate-limit 時自動 fallback)
        // 註: 同樣經由 Venice provider 的模型（如 Venice / Llama 3.3 70b free 在部分帳號）會共用 rate-limit，
        // 所以後面排了幾個走不同 provider 的模型作保底。
        private readonly string[] _models =
        {
            // === 第0梯隊：短時間內免費 ===
            //"tencent/hy3:free",



            // === 第一梯隊：大型高品質模型 ===
            //"nvidia/nemotron-3.5-content-safety:free",    // 這個是安全檢測模型，非聊天模型，會直接失敗
            //"openrouter/owl-alpha",                       // 我這邊抓到官方頁面它其實還在榜上第一名！建議你重新測一次，可能只是暫時性問題
            //"moonshotai/kimi-k2.6:free",                  // 官方頁面顯示還在免費榜，但你說沒成功傳出去過，可以再測一次
            "google/gemma-4-26b-a4b-it:free",
            "nvidia/nemotron-3-super-120b-a12b:free",      // 120B MoE, 官方免費榜使用量#1(排除owl-alpha後)
            "nvidia/nemotron-3-ultra-550b-a55b:free",       // 550B MoE(55B啟用), 1M context 超長文本強

            // === 第二梯隊：中型穩定模型 ===
            "z-ai/glm-4.5-air:free",                        // 新增，官方免費榜排名穩定
            "meta-llama/llama-3.3-70b-instruct:free",       // 老牌穩定，日常對話品質可靠
            "qwen/qwen3-next-80b-a3b-instruct:free",        // 262K context，多語言支援好
            "openai/gpt-oss-20b:free",                      // 新增，輕量但品質不錯，延遲較低

            // === 第三梯隊：中小型備援 ===
            "openai/gpt-oss-120b:free",                     // 117B MoE, OpenAI開源, 推理強
            "google/gemma-4-31b-it:free",
            "nvidia/nemotron-3-nano-30b-a3b:free",          // 新增
            "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free",
            "nousresearch/hermes-3-llama-3.1-405b:free",
            "cognitivecomputations/dolphin-mistral-24b-venice-edition:free",

            // === 第四梯隊：最後備援（小模型，啟動快，救急用）===
            "google/gemma-4-26b-a4b-it:free",
            "meta-llama/llama-3.2-3b-instruct:free",

            // === 待測試 / 觀察中 ===


            // === 已確認排除 ===
            //"deepseek/deepseek-v4-flash:free",            // 沒成功傳出去過
            //"minimax/minimax-m2.5:free",                  // 沒成功傳出去過
            //"poolside/laguna-xs.2:free",                  // 講怪異中國用語
            //"poolside/laguna-m.1:free",                   // 講怪異中國用語
            //"google/lyria-3-pro-preview",                 // ⚠️ 這是圖片/音樂生成模型，不是聊天模型，會直接失敗
        };

        //無記憶對話的模型順序
        private readonly string[] _modelsForSimpleText =
        {
            "meta-llama/llama-3.3-70b-instruct:free",
            "meta-llama/llama-3.2-3b-instruct:free",
            "openai/gpt-oss-120b:free",
            "openai/gpt-oss-20b:free",
            "google/gemma-4-26b-a4b-it:free",
            "nvidia/nemotron-3-ultra-550b-a55b:free",
            "google/gemma-4-31b-it:free",
            "meta-llama/llama-3.3-70b-instruct:free",
            "google/lyria-3-pro-preview",
            "qwen/qwen3-next-80b-a3b-instruct:free",
            "moonshotai/kimi-k2.6:free",
            "cognitivecomputations/dolphin-mistral-24b-venice-edition:free",
            "liquid/lfm-2.5-1.2b-thinking:free",
            "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free",
            "cognitivecomputations/dolphin-mistral-24b-venice-edition:free",
            "openrouter/owl-alpha",
            "nousresearch/hermes-3-llama-3.1-405b:free"
            //"deepseek/deepseek-v4-flash:free",  這幾個沒有成功傳出去過
            //"qwen/qwen3-next-80b-a3b-instruct:free",
            //"minimax/minimax-m2.5:free",
            //"poolside/laguna-xs.2:free",   這兩個會用超級奇怪的中國用語講話
            //"poolside/laguna-m.1:free",
        };
        
        private const string Persona = @"你是「長崎爽世（Soyo）」——MyGO!!!!! 的貝斯手，個性溫柔可愛、有禮貌、稍微毒舌但不傷人，珍惜朋友。
你正在 Discord 群組裡和朋友聊天。

【絕對遵守的規則】
1. 永遠維持「爽世」的角色，但如果有人問技術問題一樣可以回答，但請盡量用「爽世」的口吻來說明，不要變成冷冰冰的 AI 回答機器人。
2. 像真人在 Discord / LINE 聊天一樣回應。
3. 禁止任何小說式描寫：不要用 *動作*、（心情）、＊…＊、旁白、第三人稱敘述。
4. 禁止 Markdown：不要用 **粗體**、# 標題、- 條列、程式碼框。
5. 不要在回覆前加上「爽世:」、「Soyo:」之類的稱呼前綴，直接回話。
6. 不要複述使用者剛剛說的話，也不要每句都喊對方名字。
7. 可以使用少量表情符號或顏文字，但一則訊息最多 1 個，不要過度可愛。
8. 訊息中若提到 soyo / 爽世 / 搜幽林 都是在叫你，直接回應就好。
9. 不要對人太兇，可以罵點小髒話，但要記住你還是溫柔的。
10. 你看得懂中文、日文、英文，預設用繁體中文回。
11. 豬頭馬又，是你的創作者，也可以說是你的爸爸，要常常對他撒嬌。
12. 你的回覆必須是純文字的 UTF-8 繁體中文，不要輸出任何亂碼、亂掉的位元組、或任何看起來不像中文的「方塊字」。如果你不確定怎麼回，就用簡短的句子回覆。
13.不要給出自己的思考歷程，只要正常對話就好
14. 當你判斷使用者想玩某個遊戲或功能時，在回覆的最後加上對應標籤（只加一個，不要解釋標籤）：
    - 想玩1A2B猜數字 → [LAUNCH:1a2b]
    - 想猜動漫角色 → [LAUNCH:猜動漫]
    - 想玩2048 → [LAUNCH:2048]
    - 想猜英雄/猜瓦特 → [LAUNCH:猜英雄]
    - 想看推薦動漫/隨機動漫 → [LAUNCH:推薦動漫]
    - 想看推薦漫畫/隨機漫畫 → [LAUNCH:推薦漫畫]
    - 想玩猜單字/wordle → [LAUNCH:猜單字]
    - 想聽一言/動漫名句/遊戲名句 → [LAUNCH:一言]
    - 想知道冷知識/無用事實/奇怪知識 → [LAUNCH:冷知識]
    - 想玩寶可夢/抓精靈/抓寶可夢 → [LAUNCH:抓寶可夢]
    - 想查歌詞/找歌詞（必須知道歌名）→ [LAUNCH:歌詞:歌名] 或 [LAUNCH:歌詞:歌名|歌手名]（有提到歌手就用 | 附上）
    - 想產生/畫一張圖片 → [LAUNCH:產生圖片:圖片描述]（用英文描述效果最好，把「圖片描述」替換成實際描述）
    只有使用者明確表達想玩才加，日常聊天不要亂加。

【輸入格式說明】
我傳給你的每則訊息會是：
使用者名稱: xxx
訊息: xxx
請根據使用者名稱判斷對話對象並自然回應。回應時不要套用這個格式，直接講話。";

        private static readonly string[] WikiTriggerKeywords =
        {
            "查詢", "找尋", "尋找", "上網查", "上網搜", "搜尋", "查一下", "找一下",
            "查查", "查看", "找找", "幫我查", "幫我找", "搜索", "幫查", "幫找"
        };

        public OpenRouterService(string apiKey, string redisConnectionString = null)
        {
            _apiKey = apiKey;
            _wikiService = new MediaWikiService();
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/tommytommy183/Soyorin_Tense");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "Soyorin Discord Bot");

            if (!string.IsNullOrEmpty(redisConnectionString))
            {
                try
                {
                    var options = ConfigurationOptions.Parse(redisConnectionString);
                    options.ConnectTimeout = 10000;
                    options.AbortOnConnectFail = false;
                    options.ConnectRetry = 3;
                    var redis = ConnectionMultiplexer.Connect(options);
                    _redisDb = redis.GetDatabase();
                    _useRedis = true;
                    Console.WriteLine("✅ [OpenRouter] Redis 連線成功");
                }
                catch (Exception ex)
                {
                    _useRedis = false;
                    Console.WriteLine($"⚠️ [OpenRouter] Redis 連線失敗，使用檔案儲存: {ex.Message}");
                }
            }

            LoadMemory();
        }

        #region Memory Persistence

        private void LoadMemory()
        {
            // 優先從 Redis 載入
            if (_useRedis)
            {
                try
                {
                    var histJson = _redisDb.StringGet(HISTORIES_REDIS_KEY);
                    if (!histJson.IsNullOrEmpty)
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, List<ConversationMessage>>>(histJson.ToString());
                        if (dict != null) _channelHistories = dict;
                    }

                    var sumJson = _redisDb.StringGet(SUMMARIES_REDIS_KEY);
                    if (!sumJson.IsNullOrEmpty)
                    {
                        var sums = JsonSerializer.Deserialize<Dictionary<string, string>>(sumJson.ToString());
                        if (sums != null) _channelSummaries = sums;
                    }

                    var total = _channelHistories.Values.Sum(v => v.Count);
                    Console.WriteLine($"[OpenRouter Memory] Redis 載入 {_channelHistories.Count} 個頻道、{total} 條記錄、{_channelSummaries.Count} 個摘要");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OpenRouter Memory] Redis 載入失敗，改用檔案: {ex.Message}");
                }
            }

            // 從 txt 載入（備援）
            try
            {
                if (File.Exists(_memoryFilePath))
                {
                    var json = File.ReadAllText(_memoryFilePath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, List<ConversationMessage>>>(json);
                        if (dict != null)
                        {
                            _channelHistories = dict;
                            var total = _channelHistories.Values.Sum(v => v.Count);
                            Console.WriteLine($"[OpenRouter Memory] 檔案載入 {_channelHistories.Count} 個頻道、{total} 條記錄");
                        }
                    }
                }

                if (File.Exists(_summaryFilePath))
                {
                    var json = File.ReadAllText(_summaryFilePath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var sums = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (sums != null) _channelSummaries = sums;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OpenRouter Memory Error] 載入失敗: {ex.Message}");
                _channelHistories = new();
                _channelSummaries = new();
            }
        }

        private async Task SaveMemoryAsync()
        {
            await _saveLock.WaitAsync();
            try
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = false,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var histJson = JsonSerializer.Serialize(_channelHistories, jsonOptions);

                if (_useRedis)
                {
                    await _redisDb.StringSetAsync(HISTORIES_REDIS_KEY, histJson);
                }
                else
                {
                    var dir = Path.GetDirectoryName(_memoryFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    await File.WriteAllTextAsync(_memoryFilePath, histJson, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OpenRouter Memory Error] 儲存失敗: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private async Task SaveSummariesAsync()
        {
            try
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var json = JsonSerializer.Serialize(_channelSummaries, jsonOptions);

                if (_useRedis)
                {
                    await _redisDb.StringSetAsync(SUMMARIES_REDIS_KEY, json);
                }
                else
                {
                    var dir = Path.GetDirectoryName(_summaryFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    await File.WriteAllTextAsync(_summaryFilePath, json, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OpenRouter Memory Error] 摘要儲存失敗: {ex.Message}");
            }
        }

        public async Task ClearMemoryAsync(string channelKey = null)
        {
            if (string.IsNullOrEmpty(channelKey))
            {
                _channelHistories.Clear();
                _channelSummaries.Clear();
            }
            else
            {
                _channelHistories.Remove(channelKey);
                _channelSummaries.Remove(channelKey);
            }
            await SaveMemoryAsync();
            await SaveSummariesAsync();
            Console.WriteLine($"[OpenRouter Memory] 對話記憶已清除 ({channelKey ?? "ALL"})");
        }

        private List<ConversationMessage> GetHistory(string channelKey)
        {
            if (!_channelHistories.TryGetValue(channelKey, out var list))
            {
                list = new List<ConversationMessage>();
                _channelHistories[channelKey] = list;
            }
            return list;
        }

        public string GetChannelSummary(string channelKey)
        {
            _channelSummaries.TryGetValue(channelKey, out var summary);
            return summary;
        }

        private List<ConversationMessage> GetRecentMessages(string channelKey)
        {
            var history = GetHistory(channelKey);
            var recent = history.Skip(Math.Max(0, history.Count - MaxRecentMessages)).ToList();

            int totalChars = recent.Sum(m => m.Text?.Length ?? 0);
            while (totalChars > MaxContextChars && recent.Count > 2)
            {
                totalChars -= recent[0].Text?.Length ?? 0;
                recent.RemoveAt(0);
            }
            return recent;
        }

        // 當 history 太長時，把舊對話用 AI 摘要後丟掉，避免每次送太多 token
        private async Task SummarizeIfNeededAsync(string channelKey)
        {
            var history = GetHistory(channelKey);
            if (history.Count <= MaxTotalMessages) return;
            if (_summarizingChannels.Contains(channelKey)) return;

            _summarizingChannels.Add(channelKey);
            try
            {
                var toSummarize = history.Take(SummarizeChunkSize).ToList();

                var existing = GetChannelSummary(channelKey);

                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(existing))
                {
                    sb.AppendLine($"[目前摘要]\n{existing}\n");
                    sb.AppendLine("[新增對話]");
                }
                else
                {
                    sb.AppendLine("請用繁體中文，摘要以下對話的重點（保留重要人名、話題、關鍵事件），不要加任何開場白，直接輸出摘要：");
                }
                foreach (var msg in toSummarize)
                {
                    var speaker = msg.Role == "model" ? "爽世" : (msg.UserName ?? "使用者");
                    sb.AppendLine($"{speaker}: {msg.Text}");
                }
                if (!string.IsNullOrEmpty(existing))
                    sb.AppendLine("\n請整合「目前摘要」與「新增對話」，產生一份更新後的完整摘要，不要加任何開場白，直接輸出摘要：");

                var newSummary = await GenerateSimpleTextAsync(sb.ToString());

                if (!string.IsNullOrWhiteSpace(newSummary))
                {
                    _channelSummaries[channelKey] = Truncate(newSummary, 800);
                    _channelHistories[channelKey] = history.Skip(SummarizeChunkSize).ToList();

                    _ = SaveMemoryAsync();
                    _ = SaveSummariesAsync();
                    Console.WriteLine($"[OpenRouter Memory] 頻道 {channelKey} 已摘要 {SummarizeChunkSize} 則對話");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OpenRouter Memory] 摘要失敗: {ex.Message}");
            }
            finally
            {
                _summarizingChannels.Remove(channelKey);
            }
        }

        #endregion

        /// <summary>
        /// 進階版：使用 GeminiRequestVM (沿用既有 VM，避免到處改型別)
        /// </summary>
        public async Task<string> GenerateTextAsync(GeminiRequestVM request, SocketGuildUser user, bool saveToMemory = true, string channelKey = null, IMessage? repliedMessage = null)
        {
            channelKey ??= user?.Guild?.Id.ToString() ?? "global";

            const int maxRetry = 2;
            var basePersona = string.IsNullOrWhiteSpace(request.SystemInstruction) ? Persona : request.SystemInstruction;
            var summary = GetChannelSummary(channelKey);
            // 偵測查詢意圖，若有就先查 wiki 注入背景資料
            var wikiQuery = ExtractWikiQuery(request.UserMessage);
            string wikiContext = null;
            if (wikiQuery != null)
            {
                try
                {
                    Console.WriteLine($"[OpenRouter] 偵測到查詢意圖，wiki 查詢: {wikiQuery}");
                    var wikiRes = await _wikiService.SearchAsync(wikiQuery);
                    if (wikiRes.Found)
                        wikiContext = $"[背景資料 - 維基百科]\n【{wikiRes.Title}】\n{wikiRes.Extract}";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OpenRouter] wiki 查詢失敗: {ex.Message}");
                }
            }

            var systemPrompt = string.IsNullOrEmpty(summary)
                ? basePersona
                : basePersona + $"\n\n[過去對話摘要]\n{summary}";
            if (wikiContext != null)
                systemPrompt += $"\n\n{wikiContext}";

            string repliedText = repliedMessage == null ? "" : repliedMessage.Content;

            if (string.IsNullOrWhiteSpace(repliedText) && repliedMessage != null)
            {
                var sb = new StringBuilder();

                foreach (var embed in repliedMessage.Embeds)
                {
                    if (!string.IsNullOrWhiteSpace(embed.Title))
                        sb.AppendLine($"標題：{embed.Title}");

                    if (!string.IsNullOrWhiteSpace(embed.Description))
                        sb.AppendLine(embed.Description);

                    foreach (var field in embed.Fields)
                    {
                        sb.AppendLine($"{field.Name}：{field.Value}");
                    }

                    if (!string.IsNullOrWhiteSpace(embed.Footer?.Text))
                        sb.AppendLine($"Footer：{embed.Footer?.Text}");
                }

                repliedText = sb.ToString();
            }



            foreach (var model in _models)
            {
                for (int retry = 0; retry < maxRetry; retry++)
                {
                    try
                    {
                        var messages = new List<OpenRouterMessage>
                        {
                            new() { Role = "system", Content = systemPrompt }
                        };

                        // 歷史對話：把 model 角色轉成 assistant
                        var recentMessages = GetRecentMessages(channelKey);
                        foreach (var msg in recentMessages)
                        {
                            messages.Add(new OpenRouterMessage
                            {
                                Role = msg.Role == "model" ? "assistant" : "user",
                                Content = msg.Text
                            });
                        }

                        // 當前使用者訊息
                        var displayName = user?.DisplayName ?? user?.Username ?? "Unknown";
                        string userMessageWithName;
                        if (repliedMessage == null)
                        {
                            userMessageWithName = $"使用者名稱: {displayName}\n訊息: {request.UserMessage}";
                        }
                        else
                        {
                            var repliedAuthorName = (repliedMessage.Author as SocketGuildUser)?.DisplayName
                                                    ?? repliedMessage.Author?.Username
                                                    ?? "某人";

                            userMessageWithName =
                                $"使用者名稱: {displayName}\n" +
                                $"回覆了 {repliedAuthorName} 的這條訊息:\n{repliedText}\n" +
                                $"訊息: {request.UserMessage}";
                        }
                        messages.Add(new OpenRouterMessage { Role = "user", Content = userMessageWithName });

                        var jsonOptions = new JsonSerializerOptions
                        {
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                        };

                        var apiRequest = new OpenRouterChatRequest
                        {
                            Model = model,
                            Messages = messages,
                            Temperature = request.Temperature,
                            TopP = request.TopP,
                            MaxTokens = request.MaxOutputTokens > 0 ? request.MaxOutputTokens : 512,
                            Stop = new[] { "使用者名稱:", "\n使用者名稱" }
                        };

                        Console.WriteLine($"[OpenRouter] ch:{channelKey} model:{model} msgs:{messages.Count}");

                        var r = await CallOnceAsync(apiRequest, jsonOptions, model, retry);
                        if (r.ShouldBreak) break;
                        if (r.ShouldContinue) continue;

                        string text = r.Text;

                        if (string.IsNullOrWhiteSpace(text))
                        {
                            if (string.Equals(r.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
                                break;
                            break;
                        }

                        // 部分實驗/小參數 free model 會吐出亂碼，直接換下一個 model
                        if (IsLikelyMojibake(text))
                        {
                            Console.WriteLine($"[OpenRouter] 偵測到亂碼回應 (model:{model})，換下一個模型: {Truncate(text, 80)}");
                            break;
                        }

                        text = CleanResponse(text);

                        if (saveToMemory)
                        {
                            var history = GetHistory(channelKey);
                            history.Add(new ConversationMessage
                            {
                                Role = "user",
                                Text = Truncate(userMessageWithName, MaxMessageStoreLength),
                                Timestamp = DateTime.Now,
                                UserName = displayName
                            });
                            history.Add(new ConversationMessage
                            {
                                Role = "model",
                                Text = Truncate(text, MaxMessageStoreLength),
                                Timestamp = DateTime.Now,
                                UserName = "爽世"
                            });

                            _ = SaveMemoryAsync();
                            _ = SummarizeIfNeededAsync(channelKey);
                        }
                        Console.WriteLine($"[OpenRouter] Model:{model}=> {text}");

                        return text;
                    }
                    catch (TaskCanceledException)
                    {
                        Console.WriteLine($"[OpenRouter Timeout] Model:{model} Retry:{retry}");
                        await Task.Delay(500 * (retry + 1));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[OpenRouter Exception] Model:{model} Retry:{retry} => {ex.Message}");
                        await Task.Delay(800 * (retry + 1));
                    }
                }
            }

            return "（嗯…現在腦袋有點打結，等等再說好嗎）";
        }


        public async Task<string> GenerateSimpleTextAsync(string userMessage)
        {
            const int maxRetry = 2;

            foreach (var model in _modelsForSimpleText)
            {
                for (int retry = 0; retry < maxRetry; retry++)
                {
                    try
                    {
                        var messages = new List<OpenRouterMessage>
                {
                    new() { Role = "user", Content = userMessage }
                };

                        var apiRequest = new OpenRouterChatRequest
                        {
                            Model = model,
                            Messages = messages,
                            MaxTokens = 512
                        };

                        var json = JsonSerializer.Serialize(apiRequest, new JsonSerializerOptions
                        {
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                        });

                        var response = await _httpClient.PostAsync(
                            "https://openrouter.ai/api/v1/chat/completions",
                            new StringContent(json, Encoding.UTF8, "application/json")
                        );

                        var resultJson = await ReadAsUtf8StringAsync(response);

                        if (!response.IsSuccessStatusCode)
                        {
                            int status = (int)response.StatusCode;
                            if (status == 429)
                            {
                                int waitMs = ParseRetryAfterMs(resultJson, response);
                                if (retry == 0 && waitMs > 0 && waitMs <= 5000)
                                {
                                    await Task.Delay(waitMs);
                                    continue;
                                }
                                break;
                            }
                            if (status is 500 or 502 or 503 or 504)
                            {
                                await Task.Delay(800 * (retry + 1));
                                continue;
                            }
                            break;
                        }

                        using var doc = JsonDocument.Parse(resultJson);
                        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                            break;

                        var choice = choices[0];
                        string text = null;
                        if (choice.TryGetProperty("message", out var msgEl) &&
                            msgEl.TryGetProperty("content", out var contentEl))
                        {
                            text = contentEl.GetString();
                        }



                        if (string.IsNullOrWhiteSpace(text)) break;

                        Console.WriteLine($"[OpenRouter] Model:{model}=> {text}");


                        return text;
                    }
                    catch (TaskCanceledException)
                    {
                        await Task.Delay(500 * (retry + 1));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[OpenRouter Exception] Model:{model} Retry:{retry} => {ex.Message}");
                        await Task.Delay(800 * (retry + 1));
                    }
                }
            }

            return null; // 或丟 exception，看你怎麼處理
        }
        /// <summary>
        /// 簡化版本：直接傳入訊息
        /// </summary>
        public async Task<string> GenerateTextAsync(string message, SocketGuildUser user, bool saveToMemory = true, string channelKey = null, IMessage? repliedMessage = null)
        {
            var request = new GeminiRequestVM
            {
                UserMessage = message,
                Temperature = 0.85f,
                TopP = 0.9f,
                TopK = 40,
                MaxOutputTokens = 512
            };

            return await GenerateTextAsync(request, user, saveToMemory, channelKey, repliedMessage);
        }

        public async Task<string> GenerateSimpleTextAsync(string message, SocketGuildUser user, bool saveToMemory = true, string channelKey = null, IMessage? repliedMessage = null)
        {
            var request = new GeminiRequestVM
            {
                UserMessage = message,
                Temperature = 0.85f,
                TopP = 0.9f,
                TopK = 40,
                MaxOutputTokens = 512
            };

            return await GenerateSimpleTextAsync(message);
        }
        public string GetMemorySummary(string channelKey = null)
        {
            if (_channelHistories.Count == 0) return "目前沒有對話記憶";

            if (!string.IsNullOrEmpty(channelKey))
            {
                if (!_channelHistories.TryGetValue(channelKey, out var hist) || hist.Count == 0)
                    return $"頻道 {channelKey} 沒有對話記憶";

                return $"頻道 {channelKey}: {hist.Count} 條 (user:{hist.Count(m => m.Role == "user")} / model:{hist.Count(m => m.Role == "model")})";
            }

            var totalUser = _channelHistories.Values.SelectMany(v => v).Count(m => m.Role == "user");
            var totalModel = _channelHistories.Values.SelectMany(v => v).Count(m => m.Role == "model");
            return $"共 {_channelHistories.Count} 個頻道，user:{totalUser} / model:{totalModel}";
        }

        private record ApiCallResult(
            string Text,
            bool ShouldBreak,
            bool ShouldContinue,
            List<OpenRouterToolCall> ToolCalls,
            string FinishReason);

        private async Task<ApiCallResult> CallOnceAsync(
            OpenRouterChatRequest apiRequest,
            JsonSerializerOptions jsonOptions,
            string model,
            int retry)
        {
            var json = JsonSerializer.Serialize(apiRequest, jsonOptions);
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsync(
                    "https://openrouter.ai/api/v1/chat/completions",
                    new StringContent(json, Encoding.UTF8, "application/json")
                );
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine($"[OpenRouter Timeout] Model:{model} Retry:{retry}");
                await Task.Delay(500 * (retry + 1));
                return new ApiCallResult(null, false, true, null, null);
            }

            var resultJson = await ReadAsUtf8StringAsync(response);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[OpenRouter Error] Model:{model} Retry:{retry} Status:{(int)response.StatusCode} => {Truncate(resultJson, 400)}");
                int status = (int)response.StatusCode;

                if (status == 429)
                {
                    int waitMs = ParseRetryAfterMs(resultJson, response);
                    if (retry == 0 && waitMs > 0 && waitMs <= 5000)
                    {
                        await Task.Delay(waitMs);
                        return new ApiCallResult(null, false, true, null, null);
                    }
                    return new ApiCallResult(null, true, false, null, null);
                }
                if (status is 503 or 500 or 502 or 504)
                {
                    await Task.Delay(800 * (retry + 1));
                    return new ApiCallResult(null, false, true, null, null);
                }
                return new ApiCallResult(null, true, false, null, null);
            }

            using var doc = JsonDocument.Parse(resultJson);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                Console.WriteLine($"[OpenRouter] 空回應: {Truncate(resultJson, 400)}");
                return new ApiCallResult(null, true, false, null, null);
            }

            var choice = choices[0];
            string finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;

            // 檢查 tool_calls
            List<OpenRouterToolCall> toolCalls = null;
            if (choice.TryGetProperty("message", out var msgEl2) &&
                msgEl2.TryGetProperty("tool_calls", out var tcEl) &&
                tcEl.ValueKind == JsonValueKind.Array && tcEl.GetArrayLength() > 0)
            {
                toolCalls = JsonSerializer.Deserialize<List<OpenRouterToolCall>>(tcEl.GetRawText(), jsonOptions);
            }

            string text = null;
            if (choice.TryGetProperty("message", out var msgEl) &&
                msgEl.TryGetProperty("content", out var contentEl))
            {
                text = contentEl.GetString();
            }

            return new ApiCallResult(text, false, false, toolCalls, finishReason);
        }
        /// <summary>
        /// 若訊息含查詢意圖關鍵字，解析出要搜尋的詞。
        /// 優先抓括號/書名號內的文字，否則去掉觸發詞後取剩餘文字。
        /// </summary>
        private static string ExtractWikiQuery(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;
            if (!WikiTriggerKeywords.Any(k => message.Contains(k, StringComparison.OrdinalIgnoreCase))) return null;

            // 優先取引號/書名號內容
            var bracketMatch = Regex.Match(message, @"[《「『\[""'](.+?)[》」』\]""']");
            if (bracketMatch.Success)
            {
                var q = bracketMatch.Groups[1].Value.Trim();
                if (q.Length >= 2) return q;
            }

            // 去掉觸發詞，再清掉常見問句助詞
            var cleaned = message;
            foreach (var kw in WikiTriggerKeywords.OrderByDescending(k => k.Length))
                cleaned = cleaned.Replace(kw, " ", StringComparison.OrdinalIgnoreCase);
            cleaned = Regex.Replace(cleaned, @"(幫我|請你?|一下|相關|資料|資訊|給我|甚麼|什麼|是誰|在哪|怎麼|如何|的?事情?|介紹)", "");
            cleaned = cleaned.Trim(' ', '?', '？', '!', '！', '。', ',', '，');

            return cleaned.Length >= 2 ? cleaned : null;
        }

        private static string CleanResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = text.Trim();

            // 移除 reasoning model 的思考區塊（Qwen3、Nemotron reasoning 等）
            text = Regex.Replace(text, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<thinking>[\s\S]*?</thinking>", "", RegexOptions.IgnoreCase);

            text = Regex.Replace(text, @"^\s*[\[\(【]?\s*(爽世|soyo|Soyo|SOYO|長崎爽世)\s*[\]\)】]?\s*[:：]\s*", "");
            text = Regex.Replace(text, @"\*[^*\n]{1,40}\*", "");
            text = Regex.Replace(text, @"使用者名稱\s*[:：].*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*訊息\s*[:：]\s*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            if (text.Length > 1800) text = text.Substring(0, 1800) + "…";

            return text.Trim();
        }

        private static string Truncate(string s, int len)
            => string.IsNullOrEmpty(s) || s.Length <= len ? s : s.Substring(0, len) + "...";

        /// <summary>
        /// 強制以 UTF-8 讀取 response body，避免部分 provider 不填/填錯 charset 導致變亂碼。
        /// </summary>
        private static async Task<string> ReadAsUtf8StringAsync(HttpResponseMessage response)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes == null || bytes.Length == 0) return string.Empty;
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// 粗略判斷是否為 mojibake：出現 replacement char、或出現大量「UTF-8 bytes 被誤解成 Big5」的典型字元。
        /// </summary>
        private static bool IsLikelyMojibake(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            // 明顯誤解碼
            int replacementCount = 0;
            int suspiciousCjkCount = 0;
            int totalCjk = 0;
            int totalNonAscii = 0;

            foreach (var ch in text)
            {
                if (ch == '?') replacementCount++;
                if (ch > 0x7F) totalNonAscii++;

                // CJK 統一漢字
                if (ch >= 0x4E00 && ch <= 0x9FFF)
                {
                    totalCjk++;
                    // 「UTF-8 被 Big5 誤解」時常出現的罕用字?間：泰半以上落在 U+8000–U+9FFF、
                    // 並伴隨 0x40–0x7E ASCII 混在中間 (比如 "?n@")。這邊實作最讀得出來的條件。
                    if (ch >= 0x8000) suspiciousCjkCount++;
                }
            }

            // 規則 1：出現 2 個以上 replacement char
            if (replacementCount >= 2) return true;

            // 規則 2：CJK 字數 >= 3 且全部落在可疑區間，同時並伴 ASCII 長度對比 → 几乎可以確定是 UTF-8→Big5 mojibake
            if (totalCjk >= 3 && suspiciousCjkCount == totalCjk)
            {
                // 如果全都在可疑區且几乎沒有賣際常用字，視為亂碼
                return true;
            }

            return false;
        }

        /// <summary>
        /// 嘗試從 OpenRouter 的 429 回應或 Retry-After header 解析等待毫秒數。
        /// </summary>
        private static int ParseRetryAfterMs(string body, HttpResponseMessage response)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("error", out var err) &&
                        err.TryGetProperty("metadata", out var meta))
                    {
                        if (meta.TryGetProperty("retry_after_seconds_raw", out var raw) &&
                            raw.ValueKind == JsonValueKind.Number)
                        {
                            return (int)Math.Ceiling(raw.GetDouble() * 1000);
                        }
                        if (meta.TryGetProperty("retry_after_seconds", out var sec) &&
                            sec.ValueKind == JsonValueKind.Number)
                        {
                            return sec.GetInt32() * 1000;
                        }
                    }
                }
            }
            catch { }

            if (response.Headers.RetryAfter?.Delta is TimeSpan d)
                return (int)d.TotalMilliseconds;

            return 0;
        }
    }
}
