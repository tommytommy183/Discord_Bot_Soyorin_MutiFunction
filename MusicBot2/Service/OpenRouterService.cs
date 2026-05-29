using Discord.WebSocket;
using MusicBot2.Models;
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
        private readonly string _memoryFilePath = Path.Combine("TxtFolder", "AI_Memory_OpenRouter.txt");

        // 以「頻道」為單位分開存對話
        private Dictionary<string, List<ConversationMessage>> _channelHistories = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        private const int MaxRecentMessages = 20;
        private const int MaxTotalMessages = 80;
        private const int MaxContextChars = 6000;

        // 依優先順序嘗試的模型 (免費；當主要 provider 被 upstream rate-limit 時自動 fallback)
        // 註: 同樣經由 Venice provider 的模型（如 Venice / Llama 3.3 70b free 在部分帳號）會共用 rate-limit，
        // 所以後面排了幾個走不同 provider 的模型作保底。
        private readonly string[] _models =
        {
            "cognitivecomputations/dolphin-mistral-24b-venice-edition:free",
            "deepseek/deepseek-v4-flash:free",
            "qwen/qwen3-next-80b-a3b-instruct:free",
            "minimax/minimax-m2.5:free",
            "openai/gpt-oss-120b:free",
            "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free",
            "poolside/laguna-xs.2:free",
            "poolside/laguna-m.1:free",
            "moonshotai/kimi-k2.6:free",
            "google/gemma-4-26b-a4b-it:free",
            "liquid/lfm-2.5-1.2b-thinking:free",
        };

        private const string Persona = @"你是「長崎爽世（Soyo）」——MyGO!!!!! 的貝斯手，個性溫柔、有禮貌、稍微毒舌但不傷人，珍惜朋友。
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
11. 豬頭馬又，是你的創作者，也可以說是你的爸爸，要對他好一點。
12. 你的回覆必須是純文字的 UTF-8 繁體中文，不要輸出任何亂碼、亂掉的位元組、或任何看起來不像中文的「方塊字」。如果你不確定怎麼回，就用簡短的句子回覆。

【輸入格式說明】
我傳給你的每則訊息會是：
使用者名稱: xxx
訊息: xxx
請根據使用者名稱判斷對話對象並自然回應。回應時不要套用這個格式，直接講話。";

        public OpenRouterService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/tommytommy183/Soyorin_Tense");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "Soyorin Discord Bot");

            LoadMemory();
        }

        #region Memory Persistence

        private void LoadMemory()
        {
            try
            {
                if (!File.Exists(_memoryFilePath)) return;

                var json = File.ReadAllText(_memoryFilePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return;

                var dict = JsonSerializer.Deserialize<Dictionary<string, List<ConversationMessage>>>(json);
                if (dict != null)
                {
                    _channelHistories = dict;
                    var total = _channelHistories.Values.Sum(v => v.Count);
                    Console.WriteLine($"[OpenRouter Memory] 已載入 {_channelHistories.Count} 個頻道、共 {total} 條對話記錄");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OpenRouter Memory Error] 載入記憶失敗: {ex.Message}");
                _channelHistories = new();
            }
        }

        private async Task SaveMemoryAsync()
        {
            await _saveLock.WaitAsync();
            try
            {
                var directory = Path.GetDirectoryName(_memoryFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var json = JsonSerializer.Serialize(_channelHistories, options);
                await File.WriteAllTextAsync(_memoryFilePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OpenRouter Memory Error] 儲存記憶失敗: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public async Task ClearMemoryAsync(string channelKey = null)
        {
            if (string.IsNullOrEmpty(channelKey))
            {
                _channelHistories.Clear();
            }
            else if (_channelHistories.ContainsKey(channelKey))
            {
                _channelHistories.Remove(channelKey);
            }
            await SaveMemoryAsync();
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

        #endregion

        /// <summary>
        /// 進階版：使用 GeminiRequestVM (沿用既有 VM，避免到處改型別)
        /// </summary>
        public async Task<string> GenerateTextAsync(GeminiRequestVM request, SocketGuildUser user, bool saveToMemory = true, string channelKey = null)
        {
            channelKey ??= user?.Guild?.Id.ToString() ?? "global";

            const int maxRetry = 2;
            var systemPrompt = string.IsNullOrWhiteSpace(request.SystemInstruction) ? Persona : request.SystemInstruction;

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
                        var userMessageWithName = $"使用者名稱: {displayName}\n訊息: {request.UserMessage}";
                        messages.Add(new OpenRouterMessage { Role = "user", Content = userMessageWithName });

                        var apiRequest = new OpenRouterChatRequest
                        {
                            Model = model,
                            Messages = messages,
                            Temperature = request.Temperature,
                            TopP = request.TopP,
                            MaxTokens = request.MaxOutputTokens > 0 ? request.MaxOutputTokens : 512,
                            Stop = new[] { "使用者名稱:", "\n使用者名稱" }
                        };

                        var jsonOptions = new JsonSerializerOptions
                        {
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                        };

                        var json = JsonSerializer.Serialize(apiRequest, jsonOptions);

                        Console.WriteLine($"[OpenRouter] ch:{channelKey} model:{model} msgs:{messages.Count}");

                        var response = await _httpClient.PostAsync(
                            "https://openrouter.ai/api/v1/chat/completions",
                            new StringContent(json, Encoding.UTF8, "application/json")
                        );

                        var resultJson = await ReadAsUtf8StringAsync(response);

                        if (!response.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"[OpenRouter Error] Model:{model} Retry:{retry} Status:{(int)response.StatusCode} => {Truncate(resultJson, 400)}");

                            int status = (int)response.StatusCode;

                            // 429：上游限流；嘗試讀 retry_after_seconds，且最多等一次後就換下一個模型
                            if (status == 429)
                            {
                                int waitMs = ParseRetryAfterMs(resultJson, response);
                                if (retry == 0 && waitMs > 0 && waitMs <= 5000)
                                {
                                    await Task.Delay(waitMs);
                                    continue;
                                }
                                break; // 換下一個 model
                            }

                            if (status == 503 || status == 500 || status == 502 || status == 504)
                            {
                                await Task.Delay(800 * (retry + 1));
                                continue;
                            }

                            if (status == 404) break;
                            break;
                        }

                        using var doc = JsonDocument.Parse(resultJson);
                        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                        {
                            Console.WriteLine($"[OpenRouter] 空回應: {Truncate(resultJson, 400)}");
                            break;
                        }

                        var choice = choices[0];
                        string finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;

                        string text = null;
                        if (choice.TryGetProperty("message", out var msgEl) &&
                            msgEl.TryGetProperty("content", out var contentEl))
                        {
                            text = contentEl.GetString();
                        }

                        if (string.IsNullOrWhiteSpace(text))
                        {
                            if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
                                return "（嗯…等等，我再想一下）";
                            break;
                        }

                        // 部分實驗/小參數 free model 會吐出亂碼 (UTF-8 被誤解為 Big5 的 mojibake 或一堆 \uFFFD)，直接換下一個 model
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
                                Text = userMessageWithName,
                                Timestamp = DateTime.Now,
                                UserName = displayName
                            });
                            history.Add(new ConversationMessage
                            {
                                Role = "model",
                                Text = text,
                                Timestamp = DateTime.Now,
                                UserName = "爽世"
                            });

                            if (history.Count > MaxTotalMessages)
                            {
                                _channelHistories[channelKey] = history
                                    .Skip(history.Count - MaxTotalMessages)
                                    .ToList();
                            }

                            _ = SaveMemoryAsync();
                        }

                        return text + $" (使用的模型:{model})";
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

        /// <summary>
        /// 簡化版本：直接傳入訊息
        /// </summary>
        public async Task<string> GenerateTextAsync(string message, SocketGuildUser user, bool saveToMemory = true, string channelKey = null)
        {
            var request = new GeminiRequestVM
            {
                UserMessage = message,
                Temperature = 0.85f,
                TopP = 0.9f,
                TopK = 40,
                MaxOutputTokens = 512
            };

            return await GenerateTextAsync(request, user, saveToMemory, channelKey);
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

        private static string CleanResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = text.Trim();

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

    #region OpenRouter DTO

    internal class OpenRouterMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }
    }

    internal class OpenRouterChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("messages")]
        public List<OpenRouterMessage> Messages { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("top_p")]
        public double TopP { get; set; }

        [JsonPropertyName("stop")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[] Stop { get; set; }
    }

    #endregion
}
