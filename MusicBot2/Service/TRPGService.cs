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
            "openai/gpt-oss-120b:free",
            "google/gemma-4-31b-it:free",
            "meta-llama/llama-3.3-70b-instruct:free",
            "deepseek/deepseek-v4-flash:free",
            "qwen/qwen3-next-80b-a3b-instruct:free",
            "minimax/minimax-m2.5:free",
            "openrouter/owl-alpha",
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
- 使用陰暗、詭異的語調來營造氛圍，但不要過度冗長
- 不要過度保護玩家，這是黑暗世界，死亡隨時可能發生
- 玩家如果即將死亡，你可以給予警告或暗示，但不要直接阻止，如果還是做了就死亡
- 當玩家做出危險或需要運氣的行動時，要求擲骰
- 你需要根據玩家的行動結果和骰子判定來調整角色的生命值（HP）

【遊戲規則】
1. 使用 D20 系統（20面骰）
2. 每個角色有 100 點生命值（HP），當 HP 降到 0 時角色死亡
3. 當玩家嘗試以下行動時，必須要求擲骰：
   - 戰鬥攻擊或閃避
   - 察覺隱藏的危險
   - 說服、欺騙、威嚇他人
   - 施展魔法或使用特殊能力
   - 攀爬、跳躍等體能挑戰
   - 解除陷阱、開鎖等技巧挑戰

4. 難度判定標準：
   - 1：大失敗
   - 2-5：失敗
   - 6-19：成功
   - 20：大成功

5. 生命值變動指引：
   - 戰鬥失敗：依敵人強度造成 10-30 點傷害
   - 陷阱觸發：10-25 點傷害
   - 環境危險（跌落、灼傷等）：5-20 點傷害
   - 治療、休息：恢復 10-30 點生命值
   - 致命攻擊（大失敗）：30-50 點傷害

【生命值通知格式】
當玩家的生命值發生變化時，你必須在回應中明確說明：
- 如果受傷：「你受到了 X 點傷害」或「敵人重擊了你，造成 X 點傷害」
- 如果治療：「你恢復了 X 點生命值」或「你感到身體逐漸恢復，治癒 X 點生命值」
- 系統會自動更新玩家的實際 HP 數值

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
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/tommytommy183/Soyorin_Tense");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "Soyorin TRPG Bot");

            // 嘗試連線 Redisㄐㄧㄤ
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
            Console.WriteLine($"[TRPG] 玩家 {user.Username} 嘗試在頻道 {channelId} 開始冒險");

            if (await GameExistsAsync(channelId))
            {
                Console.WriteLine($"[TRPG] 頻道 {channelId} 已有進行中的冒險");
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

            Console.WriteLine($"[TRPG] 開始生成開場訊息...");

            // 生成開場
            var openingMessage = await GenerateOpeningAsync(user.DisplayName ?? user.Username);

            Console.WriteLine($"[TRPG] 開場訊息生成完成");

            gameState.GameHistory.Add(new TRPGMessage
            {
                UserId = 0, // System
                UserName = "GM",
                Message = openingMessage,
                Timestamp = DateTime.UtcNow,
                Type = TRPGMessageType.GMNarration
            });

            await SaveGameStateAsync(channelId, gameState);

            Console.WriteLine($"[TRPG] 冒險成功開始於頻道 {channelId}");

            return $"🌑 **永夜國度 - 黑暗奇幻冒險開始**\n\n{openingMessage}\n\n💀 從現在開始，這個頻道的所有訊息都會成為冒險的一部分。\n👥 支援多人同時冒險！\n🎲 當需要擲骰時，請使用 `/投骰` 指令。";
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

            Console.WriteLine($"[TRPG] 玩家 {user.Username} 在頻道 {channelId} 進行行動: {message}");

            // 確保角色存在（自動加入冒險）
            var character = gameState.GetOrCreateCharacter(user.Id, user.DisplayName ?? user.Username);

            // 檢查角色是否還活著
            if (!character.IsAlive)
            {
                return $"💀 {character.UserName} 已經死亡，無法進行行動。請等待冒險結束或使用管理指令復活。";
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

            // 如果正在等待擲骰，檢查是否為等待中的玩家
            if (gameState.WaitingForDiceRoll)
            {
                // 如果是等待中的玩家，提醒他先擲骰
                if (gameState.WaitingPlayerId == user.Id)
                {
                    return "⏳ 請先使用 `/投骰` 完成骰子判定，才能繼續冒險！";
                }
                // 如果是其他玩家，允許他們也進行行動（多玩家模式）
                Console.WriteLine($"[TRPG] 其他玩家 {user.Username} 加入行動（目前等待 {gameState.WaitingPlayerId} 擲骰）");
            }

            // 生成 GM 回應
            Console.WriteLine($"[TRPG] 開始生成 GM 回應...");
            var gmResponse = await GenerateGMResponseAsync(gameState, message, user);
            Console.WriteLine($"[TRPG] GM 回應生成完成");

            // 解析並處理生命值變化
            ProcessHealthChanges(gameState, gmResponse);

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
                Console.WriteLine($"[TRPG] GM 要求玩家 {user.Username} 擲骰");
            }

            // 儲存更新後的狀態
            await SaveGameStateAsync(channelId, gameState);

            // 附加角色狀態
            var statusInfo = $"\n\n💊 {character.UserName}: {character.CurrentHP}/{character.MaxHP} HP {character.GetHealthStatus()}";

            return $"🎭 **GM**: {gmResponse}{statusInfo}";
        }

        /// <summary>
        /// 處理骰子投擲
        /// </summary>
        public async Task<string> RollDiceAsync(ulong channelId, SocketGuildUser user)
        {
            Console.WriteLine($"[TRPG] 玩家 {user.Username} 嘗試在頻道 {channelId} 擲骰");

            var gameState = await LoadGameStateAsync(channelId);
            if (gameState == null)
            {
                Console.WriteLine($"[TRPG] 頻道 {channelId} 沒有進行中的冒險");
                return "❌ 此頻道沒有進行中的冒險！請先使用 `/開始冒險` 開始遊戲。";
            }

            if (!gameState.WaitingForDiceRoll)
            {
                Console.WriteLine($"[TRPG] 目前不需要擲骰");
                return "❌ 當前不需要擲骰！等 GM 要求你擲骰時再使用此指令。";
            }

            // 多玩家模式：檢查是否為等待擲骰的玩家
            if (gameState.WaitingPlayerId.HasValue && gameState.WaitingPlayerId.Value != user.Id)
            {
                Console.WriteLine($"[TRPG] 玩家 {user.Username} 不是當前等待擲骰的玩家（等待中: {gameState.WaitingPlayerId.Value}）");
                return $"⏳ 目前等待的是其他玩家擲骰，請稍候。";
            }

            // 擲 D20
            int diceResult = _random.Next(1, 21);
            Console.WriteLine($"[TRPG] 玩家 {user.Username} 擲出 {diceResult}");

            gameState.WaitingForDiceRoll = false;
            gameState.WaitingPlayerId = null;
            gameState.PendingDiceContext = null;

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

            Console.WriteLine($"[TRPG] 開始生成基於骰子結果的 GM 回應...");

            // 生成基於骰子結果的回應
            var gmResponse = await GenerateGMResponseWithDiceAsync(gameState, diceResult, user);

            Console.WriteLine($"[TRPG] GM 回應生成完成");

            // 解析並處理生命值變化
            ProcessHealthChanges(gameState, gmResponse);

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
                Console.WriteLine($"[TRPG] GM 再次要求玩家 {user.Username} 擲骰");
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

            // 獲取玩家角色
            var character = gameState.GetOrCreateCharacter(user.Id, user.DisplayName ?? user.Username);
            var statusInfo = $"\n\n💊 {character.UserName}: {character.CurrentHP}/{character.MaxHP} HP {character.GetHealthStatus()}";

            Console.WriteLine($"[TRPG] 擲骰處理完成");

            return $"🎲 {user.DisplayName ?? user.Username} 擲出了 **{diceResult}** {resultEmoji}\n\n🎭 **GM**: {gmResponse}{statusInfo}";
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

            var statusText = $"🌑 **冒險狀態**\n\n" +
                   $"⏱️ 已進行: {duration.Hours} 小時 {duration.Minutes} 分鐘\n" +
                   $"📝 玩家行動: {playerActions} 次\n" +
                   $"🎲 擲骰次數: {diceRolls} 次\n" +
                   $"⚠️ 等待擲骰: {(gameState.WaitingForDiceRoll ? "是" : "否")}\n\n";

            // 添加所有角色狀態
            if (gameState.Characters.Count > 0)
            {
                statusText += "👥 **冒險者狀態**\n";
                foreach (var character in gameState.Characters.Values.OrderByDescending(c => c.CurrentHP))
                {
                    statusText += $"{character.GetHealthStatus()} {character.UserName}: {character.CurrentHP}/{character.MaxHP} HP ({character.GetHealthDescription()})\n";
                }
            }
            else
            {
                statusText += "👥 **冒險者狀態**: 尚無冒險者\n";
            }

            return statusText;
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

            // 加入當前所有角色的血量資訊
            if (gameState.Characters.Count > 0)
            {
                var charactersStatus = gameState.GetCharactersStatusSummary();
                messages.Add(new { role = "system", content = $"【當前冒險者狀態】\n{charactersStatus}" });
            }

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
            var character = gameState.GetOrCreateCharacter(user.Id, user.DisplayName ?? user.Username);
            messages.Add(new { role = "user", content = $"{user.DisplayName ?? user.Username} ({character.CurrentHP}/{character.MaxHP} HP): {playerMessage}" });

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

            // 加入當前所有角色的血量資訊
            if (gameState.Characters.Count > 0)
            {
                var charactersStatus = gameState.GetCharactersStatusSummary();
                messages.Add(new { role = "system", content = $"【當前冒險者狀態】\n{charactersStatus}" });
            }

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

            // 加入當前玩家及骰子結果
            var character = gameState.GetOrCreateCharacter(user.Id, user.DisplayName ?? user.Username);
            messages.Add(new { role = "user", content = $"{user.DisplayName ?? user.Username} ({character.CurrentHP}/{character.MaxHP} HP) 擲骰結果: {diceResult}" });

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
                HttpResponseMessage? response = null;
                string responseBody = string.Empty;
                bool hasSuccessfulResponse = false;

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

                            Console.WriteLine($"[TRPG] 嘗試使用模型: {model} (重試: {retry + 1}/{maxRetry})");
                            response = await _httpClient.PostAsync(ApiUrl, content);
                            responseBody = await response.Content.ReadAsStringAsync();

                            if (response.IsSuccessStatusCode)
                            {
                                Console.WriteLine($"[TRPG] 模型 {model} 回應成功");
                                hasSuccessfulResponse = true;
                                break;
                            }
                            else
                            {
                                Console.WriteLine($"[TRPG] 模型 {model} 回應失敗: {response.StatusCode}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[TRPG] 模型 {model} 發生錯誤 (重試 {retry + 1}/{maxRetry}): {ex.Message}");
                        }
                    }

                    if (hasSuccessfulResponse)
                        break;
                }

                if (response == null || !hasSuccessfulResponse)
                {
                    Console.WriteLine($"[TRPG] 所有模型都失敗了");
                    return "黑暗吞噬了你的感知...（所有 AI 模型都無法回應）";
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
                    Console.WriteLine($"[TRPG] GM 回應生成成功，長度: {text.Length}");
                    return CleanResponse(text);
                }

                Console.WriteLine($"[TRPG] 無法從回應中解析內容");
                return "黑暗中傳來模糊的低語...（無法解析 GM 回應）";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRPG] CallOpenRouterAsync Exception: {ex.Message}");
                Console.WriteLine($"[TRPG] StackTrace: {ex.StackTrace}");
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
        /// 處理生命值變化（從 GM 回應中解析）
        /// </summary>
        private void ProcessHealthChanges(TRPGGameState gameState, string gmResponse)
        {
            try
            {
                // 匹配傷害：「受到了 X 點傷害」、「造成 X 點傷害」等
                var damagePatterns = new[]
                {
                    @"受到(?:了)?[\s]*(\d+)[\s]*點傷害",
                    @"造成(?:了)?[\s]*(\d+)[\s]*點傷害",
                    @"損失(?:了)?[\s]*(\d+)[\s]*點生命值",
                    @"失去(?:了)?[\s]*(\d+)[\s]*HP",
                    @"扣除(?:了)?[\s]*(\d+)[\s]*(?:點)?血量"
                };

                // 匹配治療：「恢復了 X 點生命值」、「治癒 X 點生命值」等
                var healPatterns = new[]
                {
                    @"恢復(?:了)?[\s]*(\d+)[\s]*點生命值",
                    @"治癒(?:了)?[\s]*(\d+)[\s]*點生命值",
                    @"治療(?:了)?[\s]*(\d+)[\s]*點生命值",
                    @"回復(?:了)?[\s]*(\d+)[\s]*點生命值",
                    @"增加(?:了)?[\s]*(\d+)[\s]*HP"
                };

                // 檢查傷害
                foreach (var pattern in damagePatterns)
                {
                    var match = Regex.Match(gmResponse, pattern);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int damage))
                    {
                        // 找到最後一個行動的玩家
                        var lastPlayerAction = gameState.GameHistory
                            .Where(m => m.Type == TRPGMessageType.PlayerAction || m.Type == TRPGMessageType.DiceRoll)
                            .LastOrDefault();

                        if (lastPlayerAction != null && gameState.Characters.TryGetValue(lastPlayerAction.UserId, out var character))
                        {
                            character.TakeDamage(damage);
                            Console.WriteLine($"[TRPG] {character.UserName} 受到 {damage} 點傷害，剩餘 HP: {character.CurrentHP}/{character.MaxHP}");
                        }
                        break;
                    }
                }

                // 檢查治療
                foreach (var pattern in healPatterns)
                {
                    var match = Regex.Match(gmResponse, pattern);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int healing))
                    {
                        // 找到最後一個行動的玩家
                        var lastPlayerAction = gameState.GameHistory
                            .Where(m => m.Type == TRPGMessageType.PlayerAction || m.Type == TRPGMessageType.DiceRoll)
                            .LastOrDefault();

                        if (lastPlayerAction != null && gameState.Characters.TryGetValue(lastPlayerAction.UserId, out var character))
                        {
                            character.Heal(healing);
                            Console.WriteLine($"[TRPG] {character.UserName} 恢復 {healing} 點生命值，當前 HP: {character.CurrentHP}/{character.MaxHP}");
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRPG] ProcessHealthChanges 錯誤: {ex.Message}");
            }
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
