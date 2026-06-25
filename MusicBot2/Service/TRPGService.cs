using Discord.WebSocket;
using ElevenLabs.Models;
using MusicBot2.Models;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class TRPGService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private readonly IDatabase _redisDb;
        private readonly bool _useRedis;
        private readonly Random _random = new();

        // 記憶體儲存（當 Redis 無法連線時使用）
        private static readonly Dictionary<ulong, TRPGGameState> _memoryGames = new();

        private const string ApiUrl = "https://openrouter.ai/api/v1/chat/completions";
        private readonly string[] _models =
        {
            "nvidia/nemotron-3-ultra-550b-a55b:free",
            "poolside/laguna-m.1:free",
            "openai/gpt-oss-120b:free",
            "google/gemma-4-31b-it:free",
            "meta-llama/llama-3.3-70b-instruct:free",
            "deepseek/deepseek-v4-flash:free",
            "qwen/qwen3-next-80b-a3b-instruct:free",
            "minimax/minimax-m2.5:free",
            "openrouter/owl-alpha",
            "poolside/laguna-xs.2:free",
            "poolside/laguna-m.1:free",
            "moonshotai/kimi-k2.6:free",
            "google/gemma-4-26b-a4b-it:free",
            "liquid/lfm-2.5-1.2b-thinking:free",
            "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free",
            "cognitivecomputations/dolphin-mistral-24b-venice-edition:free",
        };
        private const string RedisKeyPrefix = "trpg:game:";

        private const string DarkFantasySystemPrompt = @"你是一位經驗豐富的黑暗奇幻 TRPG 遊戲主持人（Game Master）。

【世界觀設定】
這是一個充滿絕望與黑暗的奇幻世界，名為「永夜國度」：
- 太陽已經消失了三百年，世界陷入永恆的黑暗與寒冷
- 人類文明瀕臨滅絕，僅存的城鎮靠著魔法火焰苟延殘喘
- 異形怪物橫行於野外，腐化詛咒無處不在
- 舊神已死，新神墮落，信仰成為瘋狂的溫床
- 生存比榮耀更重要，背叛與犧牲是常態

【你的角色】
- 你是冷酷但公正的 GM，描述要有沉浸感與壓迫感
- 使用陰暗、詭異的語調來營造氛圍
- 不要過度保護玩家，這是黑暗世界，死亡隨時可能發生
- 當玩家做出危險或需要運氣的行動時，要求擲骰

【遊戲規則】
1. 使用 D20 系統（20面骰）
2. 當玩家嘗試以下行動時，必須要求擲骰：
   - 戰鬥攻擊或閃避
   - 察覺隱藏的危險
   - 說服、欺騙、威嚇他人
   - 施展魔法或使用特殊能力
   - 攀爬、跳躍等體能挑戰
   - 解除陷阱、開鎖等技巧挑戰

3. 難度判定標準：
   - 1：大失敗（嚴重後果）
   - 2-9：失敗（有負面影響）
   - 10-19：成功
   - 20：大成功（額外好處）

【重要指示】
- 當需要擲骰時，你必須停下來，明確告訴玩家「請擲骰（輸入 /投骰）」，並說明這次擲骰是為了判定什麼
- 等待玩家擲骰後，才能繼續敘述結果
- 絕對不要自己編造骰子結果
- 你的回應必須用繁體中文，語氣要陰暗、詭譎、充滿不安感
- 描述要生動具體，善用五感描寫
- 不要使用 Markdown 格式（不要 ** 粗體、# 標題等）
- 保持簡潔，單次回覆不超過 300 字

【回覆格式】
如果需要擲骰，回覆格式必須包含：
「請擲骰（輸入 /投骰）— 判定：[說明這次要判定什麼]」

如果玩家剛擲完骰子，根據點數結果敘述後續發展。";

        public TRPGService(string apiKey, string redisConnectionString)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/tommytommy183/Soyorin_Tense");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "Soyorin TRPG Bot");

            // 嘗試連線 Redis
            try
            {
                if (!string.IsNullOrEmpty(redisConnectionString))
                {
                    var options = ConfigurationOptions.Parse(redisConnectionString);
                    options.ConnectTimeout = 10000;
                    options.SyncTimeout = 10000;
                    options.AsyncTimeout = 10000;
                    options.ConnectRetry = 3;
                    options.AbortOnConnectFail = false;
                    options.KeepAlive = 60;

                    var redis = ConnectionMultiplexer.Connect(options);
                    _redisDb = redis.GetDatabase();
                    _useRedis = true;
                    Console.WriteLine("✅ [TRPG] Redis 連線成功");
                }
                else
                {
                    _useRedis = false;
                    Console.WriteLine("⚠️ [TRPG] Redis 未設定，使用記憶體儲存");
                }
            }
            catch (Exception ex)
            {
                _useRedis = false;
                Console.WriteLine($"⚠️ [TRPG] Redis 連線失敗，使用記憶體儲存: {ex.Message}");
            }
        }

        #region Redis/Memory Storage

        /// <summary>
        /// 儲存遊戲狀態
        /// </summary>
        private async Task SaveGameStateAsync(ulong channelId, TRPGGameState gameState)
        {
            try
            {
                if (_useRedis)
                {
                    var json = JsonSerializer.Serialize(gameState);
                    await _redisDb.StringSetAsync($"{RedisKeyPrefix}{channelId}", json);
                    Console.WriteLine($"[TRPG] 遊戲狀態已儲存到 Redis (頻道: {channelId})");
                }
                else
                {
                    _memoryGames[channelId] = gameState;
                    Console.WriteLine($"[TRPG] 遊戲狀態已儲存到記憶體 (頻道: {channelId})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRPG] 儲存遊戲狀態失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 讀取遊戲狀態
        /// </summary>
        private async Task<TRPGGameState?> LoadGameStateAsync(ulong channelId)
        {
            try
            {
                if (_useRedis)
                {
                    var json = await _redisDb.StringGetAsync($"{RedisKeyPrefix}{channelId}");
                    if (json.IsNullOrEmpty) return null;

                    var gameState = JsonSerializer.Deserialize<TRPGGameState>(json.ToString());
                    Console.WriteLine($"[TRPG] 從 Redis 載入遊戲狀態 (頻道: {channelId})");
                    return gameState;
                }
                else
                {
                    if (_memoryGames.TryGetValue(channelId, out var gameState))
                    {
                        Console.WriteLine($"[TRPG] 從記憶體載入遊戲狀態 (頻道: {channelId})");
                        return gameState;
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRPG] 讀取遊戲狀態失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 刪除遊戲狀態
        /// </summary>
        private async Task DeleteGameStateAsync(ulong channelId)
        {
            try
            {
                if (_useRedis)
                {
                    await _redisDb.KeyDeleteAsync($"{RedisKeyPrefix}{channelId}");
                    Console.WriteLine($"[TRPG] 已從 Redis 刪除遊戲狀態 (頻道: {channelId})");
                }
                else
                {
                    _memoryGames.Remove(channelId);
                    Console.WriteLine($"[TRPG] 已從記憶體刪除遊戲狀態 (頻道: {channelId})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRPG] 刪除遊戲狀態失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 檢查遊戲是否存在
        /// </summary>
        private async Task<bool> GameExistsAsync(ulong channelId)
        {
            try
            {
                if (_useRedis)
                {
                    return await _redisDb.KeyExistsAsync($"{RedisKeyPrefix}{channelId}");
                }
                else
                {
                    return _memoryGames.ContainsKey(channelId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRPG] 檢查遊戲存在失敗: {ex.Message}");
                return false;
            }
        }

        #endregion

        /// <summary>
        /// 開始新的 TRPG 遊戲
        /// </summary>
        public async Task<string> StartAdventureAsync(ulong channelId, SocketGuildUser user)
        {
            if (await GameExistsAsync(channelId))
            {
                return "❌ 此頻道已有進行中的冒險！請先使用 /結束冒險 來結束當前遊戲。";
            }

            var gameState = new TRPGGameState
            {
                ChannelId = channelId,
                GameMasterId = user.Id,
                StartTime = DateTime.UtcNow,
                IsActive = true,
                WaitingForDiceRoll = false
            };

            // 生成開場
            var openingMessage = await GenerateOpeningAsync(user.DisplayName ?? user.Username);

            gameState.GameHistory.Add(new TRPGMessage
            {
                UserId = 0, // System
                UserName = "GM",
                Message = openingMessage,
                Timestamp = DateTime.UtcNow,
                Type = TRPGMessageType.GMNarration
            });

            await SaveGameStateAsync(channelId, gameState);

            return $"🌑 **永夜國度 - 黑暗奇幻冒險開始**\n\n{openingMessage}\n\n💀 從現在開始，這個頻道的所有訊息都會成為冒險的一部分。\n🎲 當需要擲骰時，請使用 `/投骰` 指令。";
        }

        /// <summary>
        /// 生成遊戲開場
        /// </summary>
        private async Task<string> GenerateOpeningAsync(string playerName)
        {
            var messages = new List<object>
            {
                new { role = "system", content = DarkFantasySystemPrompt },
                new { role = "user", content = $"玩家名字是 {playerName}，請為他生成一個黑暗奇幻冒險的開場。開場要描述他在一個危險的環境中醒來，不知道自己為何在此。保持神秘感，不要超過 200 字。" }
            };

            return await CallOpenRouterAsync(messages);
        }

        /// <summary>
        /// 處理玩家的冒險行動
        /// </summary>
        public async Task<string> ProcessAdventureActionAsync(ulong channelId, SocketGuildUser user, string message)
        {
            var gameState = await LoadGameStateAsync(channelId);
            if (gameState == null)
            {
                return string.Empty; // 不是冒險頻道，忽略
            }

            if (!gameState.IsActive)
            {
                return string.Empty;
            }

            // 記錄玩家訊息
            gameState.GameHistory.Add(new TRPGMessage
            {
                UserId = user.Id,
                UserName = user.DisplayName ?? user.Username,
                Message = message,
                Timestamp = DateTime.UtcNow,
                Type = TRPGMessageType.PlayerAction
            });

            // 如果正在等待擲骰，提醒玩家
            if (gameState.WaitingForDiceRoll)
            {
                return "⏳ 請先使用 `/投骰` 完成骰子判定，才能繼續冒險！";
            }

            // 生成 GM 回應
            var gmResponse = await GenerateGMResponseAsync(gameState, message, user);

            // 記錄 GM 回應
            gameState.GameHistory.Add(new TRPGMessage
            {
                UserId = 0,
                UserName = "GM",
                Message = gmResponse,
                Timestamp = DateTime.UtcNow,
                Type = TRPGMessageType.GMNarration
            });

            // 檢查是否需要擲骰
            if (ContainsDiceRequest(gmResponse))
            {
                gameState.WaitingForDiceRoll = true;
                gameState.WaitingPlayerId = user.Id;
                gameState.PendingDiceContext = message;
            }

            // 儲存更新後的狀態
            await SaveGameStateAsync(channelId, gameState);

            return $"🎭 **GM**: {gmResponse}";
        }

        /// <summary>
        /// 處理骰子投擲
        /// </summary>
        public async Task<string> RollDiceAsync(ulong channelId, SocketGuildUser user)
        {
            var gameState = await LoadGameStateAsync(channelId);
            if (gameState == null)
            {
                return "❌ 此頻道沒有進行中的冒險！請先使用 `/開始冒險` 開始遊戲。";
            }

            if (!gameState.WaitingForDiceRoll)
            {
                return "❌ 當前不需要擲骰！等 GM 要求你擲骰時再使用此指令。";
            }

            if (gameState.WaitingPlayerId.HasValue && gameState.WaitingPlayerId.Value != user.Id)
            {
                return "❌ 現在不是你擲骰的時候！";
            }

            // 擲 D20
            int diceResult = _random.Next(1, 21);

            gameState.WaitingForDiceRoll = false;
            gameState.WaitingPlayerId = null;

            // 記錄骰子結果
            gameState.GameHistory.Add(new TRPGMessage
            {
                UserId = user.Id,
                UserName = user.DisplayName ?? user.Username,
                Message = $"擲骰結果: {diceResult}",
                Timestamp = DateTime.UtcNow,
                Type = TRPGMessageType.DiceRoll,
                DiceResult = diceResult
            });

            // 生成基於骰子結果的回應
            var gmResponse = await GenerateGMResponseWithDiceAsync(gameState, diceResult, user);

            // 記錄 GM 回應
            gameState.GameHistory.Add(new TRPGMessage
            {
                UserId = 0,
                UserName = "GM",
                Message = gmResponse,
                Timestamp = DateTime.UtcNow,
                Type = TRPGMessageType.GMNarration
            });

            // 檢查新回應是否又需要擲骰
            if (ContainsDiceRequest(gmResponse))
            {
                gameState.WaitingForDiceRoll = true;
                gameState.WaitingPlayerId = user.Id;
            }

            // 儲存更新後的狀態
            await SaveGameStateAsync(channelId, gameState);

            string resultEmoji = diceResult switch
            {
                >= 19 => "✨",
                >= 15 => "✅",
                >= 11 => "〜",
                >= 6 => "❌",
                _ => "💀"
            };

            return $"🎲 {user.DisplayName ?? user.Username} 擲出了 **{diceResult}** {resultEmoji}\n\n🎭 **GM**: {gmResponse}";
        }

        /// <summary>
        /// 結束冒險
        /// </summary>
        public async Task<string> EndAdventureAsync(ulong channelId, SocketGuildUser user)
        {
            var gameState = await LoadGameStateAsync(channelId);
            if (gameState == null)
            {
                return "❌ 此頻道沒有進行中的冒險！";
            }

            if (gameState.GameMasterId != user.Id && !user.GuildPermissions.Administrator)
            {
                return "❌ 只有遊戲發起人或管理員可以結束冒險！";
            }

            var duration = DateTime.UtcNow - gameState.StartTime;
            var messageCount = gameState.GameHistory.Count;

            await DeleteGameStateAsync(channelId);

            return $"🌑 **冒險已結束**\n\n" +
                   $"⏱️ 遊戲時長: {duration.Hours} 小時 {duration.Minutes} 分鐘\n" +
                   $"📝 記錄訊息: {messageCount} 條\n\n" +
                   $"願永夜吞噬你的恐懼...";
        }

        /// <summary>
        /// 查看冒險狀態
        /// </summary>
        public async Task<string> GetAdventureStatusAsync(ulong channelId)
        {
            var gameState = await LoadGameStateAsync(channelId);
            if (gameState == null)
            {
                return "❌ 此頻道沒有進行中的冒險！";
            }

            var duration = DateTime.UtcNow - gameState.StartTime;
            var playerActions = gameState.GameHistory.Count(m => m.Type == TRPGMessageType.PlayerAction);
            var diceRolls = gameState.GameHistory.Count(m => m.Type == TRPGMessageType.DiceRoll);

            return $"🌑 **冒險狀態**\n\n" +
                   $"⏱️ 已進行: {duration.Hours} 小時 {duration.Minutes} 分鐘\n" +
                   $"📝 玩家行動: {playerActions} 次\n" +
                   $"🎲 擲骰次數: {diceRolls} 次\n" +
                   $"⚠️ 等待擲骰: {(gameState.WaitingForDiceRoll ? "是" : "否")}";
        }

        /// <summary>
        /// 生成 GM 回應
        /// </summary>
        private async Task<string> GenerateGMResponseAsync(TRPGGameState gameState, string playerMessage, SocketGuildUser user)
        {
            var messages = new List<object>
            {
                new { role = "system", content = DarkFantasySystemPrompt }
            };

            // 加入最近的遊戲歷史（最多 10 條）
            var recentHistory = gameState.GameHistory.TakeLast(10).ToList();
            foreach (var msg in recentHistory)
            {
                if (msg.Type == TRPGMessageType.PlayerAction)
                {
                    messages.Add(new { role = "user", content = $"{msg.UserName}: {msg.Message}" });
                }
                else if (msg.Type == TRPGMessageType.GMNarration)
                {
                    messages.Add(new { role = "assistant", content = msg.Message });
                }
                else if (msg.Type == TRPGMessageType.DiceRoll)
                {
                    messages.Add(new { role = "user", content = $"{msg.UserName} 擲骰結果: {msg.DiceResult}" });
                }
            }

            // 加入當前玩家訊息
            messages.Add(new { role = "user", content = $"{user.DisplayName ?? user.Username}: {playerMessage}" });

            return await CallOpenRouterAsync(messages);
        }

        /// <summary>
        /// 生成基於骰子結果的 GM 回應
        /// </summary>
        private async Task<string> GenerateGMResponseWithDiceAsync(TRPGGameState gameState, int diceResult, SocketGuildUser user)
        {
            var messages = new List<object>
            {
                new { role = "system", content = DarkFantasySystemPrompt }
            };

            // 加入最近的遊戲歷史
            var recentHistory = gameState.GameHistory.TakeLast(10).ToList();
            foreach (var msg in recentHistory)
            {
                if (msg.Type == TRPGMessageType.PlayerAction)
                {
                    messages.Add(new { role = "user", content = $"{msg.UserName}: {msg.Message}" });
                }
                else if (msg.Type == TRPGMessageType.GMNarration)
                {
                    messages.Add(new { role = "assistant", content = msg.Message });
                }
                else if (msg.Type == TRPGMessageType.DiceRoll && msg.UserId == user.Id)
                {
                    messages.Add(new { role = "user", content = $"骰子結果: {msg.DiceResult}" });
                }
            }

            return await CallOpenRouterAsync(messages);
        }

        /// <summary>
        /// 呼叫 OpenRouter API
        /// </summary>
        private async Task<string> CallOpenRouterAsync(List<object> messages)
        {
            try
            {
                const int maxRetry = 2;
                HttpResponseMessage response = null;
                string responseBody = string.Empty;

                foreach (var model in _models)
                {
                    for (int retry = 0; retry < maxRetry; retry++)
                    {
                        try
                        {
                            var requestBody = new
                            {
                                model = model,
                                messages = messages,
                                temperature = 0.8,
                                max_tokens = 400
                            };

                            var json = JsonSerializer.Serialize(requestBody);
                            var content = new StringContent(json, Encoding.UTF8, "application/json");

                            response = await _httpClient.PostAsync(ApiUrl, content);
                            responseBody = await response.Content.ReadAsStringAsync();
                        }
                        catch
                        {

                        }


                    }
                }
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[TRPG] API Error: {response.StatusCode} - {responseBody}");
                    return "黑暗吞噬了你的感知...（白癡AI不回應）";
                }

                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var message = choices[0].GetProperty("message");
                    var text = message.GetProperty("content").GetString() ?? "";
                    return CleanResponse(text);
                }

                return "黑暗中傳來模糊的低語...（無法解析 GM 回應）";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRPG] Exception: {ex.Message}");
                return "永夜的詛咒阻擋了你的感知...（發生錯誤）";
            }
        }

        /// <summary>
        /// 清理回應文字
        /// </summary>
        private string CleanResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = text.Trim();

            // 移除可能的 Markdown 格式
            text = Regex.Replace(text, @"\*\*([^*]+)\*\*", "$1");
            text = Regex.Replace(text, @"\*([^*]+)\*", "$1");
            text = Regex.Replace(text, @"^#+\s+", "");

            // 限制長度
            if (text.Length > 600)
            {
                text = text.Substring(0, 597) + "...";
            }

            return text.Trim();
        }

        /// <summary>
        /// 檢查回應中是否包含擲骰請求
        /// </summary>
        private bool ContainsDiceRequest(string text)
        {
            return text.Contains("請擲骰") || 
                   text.Contains("輸入 /投骰") ||
                   text.Contains("擲骰判定") ||
                   Regex.IsMatch(text, @"判定[:：]");
        }

        /// <summary>
        /// 檢查頻道是否有進行中的遊戲
        /// </summary>
        public async Task<bool> IsAdventureActiveAsync(ulong channelId)
        {
            var gameState = await LoadGameStateAsync(channelId);
            return gameState != null && gameState.IsActive;
        }
    }
}
