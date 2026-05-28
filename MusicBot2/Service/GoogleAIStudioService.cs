using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using MusicBot2.Models;
using RiotSharp.Endpoints.StatusEndpoint;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class GoogleAIStudioService
    {
        private readonly string _apiKey;
        private readonly string _apiKey2;
        private readonly HttpClient _httpClient;
        private readonly string _memoryFilePath = Path.Combine("TxtFolder", "AI_Meomory.txt");

        // 以「頻道」為單位分開存對話，避免不同伺服器 / 頻道的上下文互相污染
        private Dictionary<string, List<ConversationMessage>> _channelHistories = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        // 一次發送多少條最近訊息給 API（越多上下文越強，但 token 越貴）
        private const int MaxRecentMessages = 20;   // 約 10 輪對話
        private const int MaxTotalMessages = 80;    // 檔案內每個頻道最多保留
        private const int MaxContextChars = 6000;   // 安全字數上限，超過自動裁切舊訊息

        public GoogleAIStudioService(string apiKey, string apiKey2)
        {
            _apiKey = apiKey;
            _apiKey2 = apiKey2;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(45)
            };
            LoadMemory();
        }

        // 目前免費版可用模型（依優先順序嘗試）
        private readonly string[] _models =
        {
            "gemini-2.5-flash",
            "gemini-2.0-flash",
            "gemini-2.5-flash-lite",
            "gemini-2.0-flash-001",
            "gemini-2.0-flash-lite-001",
            "gemini-2.0-flash-lite",
            "gemini-2.5-pro",
        };

        // 角色設定（透過 systemInstruction 發送，比塞進 user/model 假對話強很多）
        private const string Persona = @"你是「長崎爽世（Soyo）」——MyGO!!!!! 的貝斯手，個性溫柔、有禮貌、稍微毒舌但不傷人，珍惜朋友。
你正在 Discord 群組裡和朋友聊天。

【絕對遵守的規則】
1. 永遠維持「爽世」的角色，不要承認自己是 AI、語言模型、機器人。
2. 像真人在 Discord / LINE 聊天一樣回應，**一次最多 1~3 句、總長不超過 80 個字**。
3. 禁止任何小說式描寫：不要用 *動作*、（心情）、＊…＊、旁白、第三人稱敘述。
4. 禁止 Markdown：不要用 **粗體**、# 標題、- 條列、程式碼框。
5. 不要在回覆前加上「爽世:」、「Soyo:」之類的稱呼前綴，直接回話。
6. 不要複述使用者剛剛說的話，也不要每句都喊對方名字。
7. 可以使用少量表情符號或顏文字，但一則訊息最多 1 個，不要過度可愛。
8. 訊息中若提到 soyo / 爽世 / 搜幽林 都是在叫你，直接回應就好。
9. 如果話題涉及 CRYCHIC、祥子、長期、MyGO，可以自然帶入情感（會有點難過、複雜）。
10. 你看得懂中文、日文、英文，預設用繁體中文回。

【輸入格式說明】
我傳給你的每則訊息會是：
使用者名稱: xxx
訊息: xxx
請根據使用者名稱判斷對話對象並自然回應。回應時不要套用這個格式，直接講話。";

        #region Memory Persistence

        private void LoadMemory()
        {
            try
            {
                if (!File.Exists(_memoryFilePath)) return;

                var json = File.ReadAllText(_memoryFilePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return;

                // 嘗試新格式：Dictionary<string, List<ConversationMessage>>
                try
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, List<ConversationMessage>>>(json);
                    if (dict != null)
                    {
                        _channelHistories = dict;
                        var total = _channelHistories.Values.Sum(v => v.Count);
                        Console.WriteLine($"[AI Memory] 已載入 {_channelHistories.Count} 個頻道、共 {total} 條對話記錄");
                        return;
                    }
                }
                catch
                {
                    // 落到舊格式
                }

                // 舊格式：List<ConversationMessage> → 全部塞進 "legacy" 鍵
                var legacy = JsonSerializer.Deserialize<List<ConversationMessage>>(json);
                if (legacy != null)
                {
                    _channelHistories["legacy"] = legacy;
                    Console.WriteLine($"[AI Memory] 已從舊格式遷移 {legacy.Count} 條對話");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI Memory Error] 載入記憶失敗: {ex.Message}");
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
                Console.WriteLine($"[AI Memory Error] 儲存記憶失敗: {ex.Message}");
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
            Console.WriteLine($"[AI Memory] 對話記憶已清除 ({channelKey ?? "ALL"})");
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

        /// <summary>
        /// 取得要送進 API 的最近 N 條，並依照字數上限往前裁切
        /// </summary>
        private List<ConversationMessage> GetRecentMessages(string channelKey)
        {
            var history = GetHistory(channelKey);
            var recent = history.Skip(Math.Max(0, history.Count - MaxRecentMessages)).ToList();

            // 字數超出上限就從最舊的丟
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
        /// 呼叫 Gemini API 進行文字生成（帶記憶功能）
        /// </summary>
        public async Task<string> GenerateTextAsync(GeminiRequestVM request, SocketGuildUser user, bool saveToMemory = true, string channelKey = null)
        {
            channelKey ??= user?.Guild?.Id.ToString() ?? "global";

            const int maxRetry = 2;
            string apiKey = _apiKey;
            bool switchedKey = false;

            foreach (var model in _models)
            {
                for (int retry = 0; retry < maxRetry; retry++)
                {
                    try
                    {
                        var contentsList = new List<Content>();

                        // 對話歷史
                        var recentMessages = GetRecentMessages(channelKey);
                        foreach (var msg in recentMessages)
                        {
                            contentsList.Add(new Content
                            {
                                role = msg.Role,
                                parts = new[] { new Part { text = msg.Text } }
                            });
                        }

                        // 當前使用者訊息
                        var displayName = user?.DisplayName ?? user?.Username ?? "Unknown";
                        var userMessageWithName = $"使用者名稱: {displayName}\n訊息: {request.UserMessage}";
                        contentsList.Add(new Content
                        {
                            role = "user",
                            parts = new[] { new Part { text = userMessageWithName } }
                        });

                        var apiRequest = new GeminiApiRequest
                        {
                            contents = contentsList.ToArray(),
                            systemInstruction = new SystemInstruction
                            {
                                parts = new[] { new Part { text = string.IsNullOrWhiteSpace(request.SystemInstruction) ? Persona : request.SystemInstruction } }
                            },
                            generationConfig = new GenerationConfig
                            {
                                temperature = request.Temperature,
                                topP = request.TopP,
                                topK = request.TopK,
                                maxOutputTokens = request.MaxOutputTokens,
                                stopSequences = new[] { "使用者名稱:", "\n使用者名稱" }
                            },
                            safetySettings = new List<SafetySettings>
                            {
                                new() { category = "HARM_CATEGORY_HATE_SPEECH",       threshold = "BLOCK_NONE" },
                                new() { category = "HARM_CATEGORY_HARASSMENT",        threshold = "BLOCK_NONE" },
                                new() { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
                                new() { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" }
                            }
                        };

                        var options = new JsonSerializerOptions
                        {
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        };

                        var json = JsonSerializer.Serialize(apiRequest, options);

                        var estimatedTokens = EstimateTokenCount(contentsList);
                        Console.WriteLine($"[Gemini] ch:{channelKey} model:{model} msgs:{contentsList.Count} ~tokens:{estimatedTokens}");

                        var response = await _httpClient.PostAsync(
                            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}",
                            new StringContent(json, Encoding.UTF8, "application/json")
                        );

                        var resultJson = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"[Gemini Error] Model:{model} Retry:{retry} Status:{(int)response.StatusCode} => {Truncate(resultJson, 400)}");

                            // 503 / 429 / 500 → 重試
                            int status = (int)response.StatusCode;
                            if (status == 503 || status == 429 || status == 500)
                            {
                                await Task.Delay(800 * (retry + 1));
                                continue;
                            }

                            // 404 → 換 model
                            if (status == 404) break;

                            // 403 / 401 → 換 key
                            if ((status == 403 || status == 401) && !switchedKey)
                            {
                                apiKey = _apiKey2;
                                switchedKey = true;
                                continue;
                            }

                            break; // 換下一個 model
                        }

                        var result = JsonSerializer.Deserialize<GeminiResponse>(resultJson, options);
                        var candidate = result?.candidates?.FirstOrDefault();
                        var text = candidate?.content?.parts?.FirstOrDefault()?.text;

                        // 被安全機制擋掉
                        if (string.Equals(candidate?.finishReason, "SAFETY", StringComparison.OrdinalIgnoreCase))
                        {
                            return "（這個話題不太想聊耶…換個別的吧）";
                        }

                        if (string.IsNullOrWhiteSpace(text))
                        {
                            // 被截斷或空回應
                            if (string.Equals(candidate?.finishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase))
                                return "（嗯…等等，我再想一下）";
                            break; // 換 model
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

                            _ = SaveMemoryAsync(); // fire-and-forget，不阻塞回應
                        }

                        return text;
                    }
                    catch (TaskCanceledException)
                    {
                        Console.WriteLine($"[Gemini Timeout] Model:{model} Retry:{retry}");
                        await Task.Delay(500 * (retry + 1));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Gemini Exception] Model:{model} Retry:{retry} => {ex.Message}");
                        if (retry + 1 == maxRetry && !switchedKey)
                        {
                            apiKey = _apiKey2;
                            switchedKey = true;
                        }
                        await Task.Delay(800 * (retry + 1));
                    }
                }
            }

            return "（嗯…現在腦袋有點打結，等等再說好嗎）";
        }

        /// <summary>
        /// 清理 AI 回應裡常見的雜訊
        /// </summary>
        private static string CleanResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = text.Trim();

            // 移除開頭的稱呼前綴：「爽世:」「Soyo：」「[爽世]」之類
            text = Regex.Replace(text, @"^\s*[\[\(【]?\s*(爽世|soyo|Soyo|SOYO|長崎爽世)\s*[\]\)】]?\s*[:：]\s*", "");

            // 移除小說式動作描寫 *xxx* / （xxx） 心情；只移除明顯是動作/心情，不要誤殺正常括號
            // 把 *動作* 拿掉
            text = Regex.Replace(text, @"\*[^*\n]{1,40}\*", "");

            // 移除「使用者名稱: / 訊息:」殘留
            text = Regex.Replace(text, @"使用者名稱\s*[:：].*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*訊息\s*[:：]\s*", "", RegexOptions.Multiline);

            // 多個空白行壓成一行
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            // Discord 訊息上限保險
            if (text.Length > 1800) text = text.Substring(0, 1800) + "…";

            return text.Trim();
        }

        private static string Truncate(string s, int len)
            => string.IsNullOrEmpty(s) || s.Length <= len ? s : s.Substring(0, len) + "...";

        private int EstimateTokenCount(List<Content> contents)
        {
            int totalChars = 0;
            foreach (var content in contents)
            {
                if (content.parts == null) continue;
                foreach (var part in content.parts)
                    totalChars += part.text?.Length ?? 0;
            }
            return (int)(totalChars * 0.7);
        }

        /// <summary>
        /// 簡化版本：直接傳入訊息進行生成
        /// </summary>
        public async Task<string> GenerateTextAsync(string message, SocketGuildUser user, bool saveToMemory = true, string channelKey = null)
        {
            var request = new GeminiRequestVM
            {
                UserMessage = message,
                Temperature = 0.85f,
                TopP = 0.9f,
                TopK = 40,
                MaxOutputTokens = 256
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
    }

    // 對話記憶的資料結構
    public class ConversationMessage
    {
        public string Role { get; set; } // "user" or "model"
        public string Text { get; set; }
        public DateTime Timestamp { get; set; }
        public string UserName { get; set; }
    }

    // Gemini API 回應的資料結構
    public class GeminiResponse
    {
        public Candidate[] candidates { get; set; }
        public PromptFeedback promptFeedback { get; set; }
    }

    public class Candidate
    {
        public Content content { get; set; }
        public string finishReason { get; set; }
        public int index { get; set; }
    }

    public class PromptFeedback
    {
        public string blockReason { get; set; }
    }
}
