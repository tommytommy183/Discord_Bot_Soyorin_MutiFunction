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
- 你需要根據玩家的行動結果和骰子判定來調整角色的生命值（HP）和背包物品

【遊戲規則】
1. 使用 D20 系統（20面骰）
2. 每個角色有 100 點生命值（HP），當 HP 降到 0 時角色死亡
3. 每個角色都有背包可以存放物品
4. 當玩家嘗試以下行動時，必須要求擲骰：
   - 戰鬥攻擊或閃避
   - 察覺隱藏的危險
   - 說服、欺騙、威嚇他人
   - 施展魔法或使用特殊能力
   - 攀爬、跳躍等體能挑戰
   - 解除陷阱、開鎖等技巧挑戰

5. 難度判定標準（DC = Difficulty Class）：
   - 1：大失敗（無論DC多少都失敗，且有額外懲罰）
   - 2-5：失敗
   - 6-19：成功（需要 >= DC）
   - 20：大成功（無論DC多少都成功，且有額外獎勵）

   DC 參考值：
   - 簡單任務: DC 6
   - 普通任務: DC 10
   - 困難任務: DC 14
   - 極難任務: DC 17
   - 近乎不可能: DC 19

6. 屬性修正值（加到擲骰結果上）：
   - 屬性 8-9: -1
   - 屬性 10-11: +0
   - 屬性 12-13: +1
   - 屬性 14-15: +2
   - 屬性 16-17: +3
   例如：力量檢定時，力量16的戰士有+3修正，擲出7實際算作10

7. 擲骰時必須明確告知：
   - 這次檢定的屬性（力量/敏捷/體質/智力/感知/魅力）
   - DC 值是多少
   - 玩家的屬性修正值是多少
   - 例如：「請擲骰（輸入 /投骰）— 判定：敏捷檢定（DC 12，你的敏捷修正: +2，實際需要擲出 10 以上）」

6. 生命值變動指引：
   - 戰鬥失敗：依敵人強度造成 10-30 點傷害
   - 陷阱觸發：10-25 點傷害
   - 環境危險（跌落、灼傷等）：5-20 點傷害
   - 治療、休息：恢復 10-30 點生命值
   - 致命攻擊（大失敗）：30-50 點傷害

7. 一切行為都要符合角色職業，不可以因為玩家說自己突然得到什麼物品而真的讓他得到，必須符合職業和當前遊戲情況

【飢餓值與 SAN 值系統】
每位冒險者除了 HP 之外，還有「飢餓值」和「SAN 值（理智值）」：

飢餓值（0-100）：
- 每次重大行動（戰鬥、長距離移動、施法）消耗 5-15 點飢餓值
- 飢餓值降到 30 以下時，所有檢定 DC +2
- 飢餓值降到 0 時，開始每次行動扣血
- 食物可以恢復飢餓值
- 通知格式：「你消耗了 X 點飢餓值」或「你恢復了 X 點飢餓值」

SAN 值（0-100）：
- 看到恐怖景象、接觸邊緣知識、遭受詛咒時減少 5-20 點SAN值
- SAN值降到 30 以下時，開始出現幻覺、幻聽
- SAN值降到 0 時，角色陷入瘋狂，行動不受控制
- 休息、牧師的祝福可以恢復SAN值
- 通知格式：「你失去了 X 點SAN值」或「你恢復了 X 點SAN值」

【物品管理規則】
1. 物品獲取：當玩家成功完成任務、探索、擊敗敵人或發現寶箱時，可以獲得物品
2. 物品使用：玩家可以使用背包中的物品（如藥水、武器、工具等）
3. 物品掉落：當玩家失敗、被攻擊或發生意外時，可能會掉落物品
4. 物品遺失：特定情況下（如被偷竊、自願丟棄）會失去物品

【物品通知格式】
當物品狀態改變時，你必須明確說明：
- 獲得物品：「你獲得了【物品名稱】（簡短描述物品用途）」
- 使用物品：「你使用了【物品名稱】（描述使用效果）」
- 掉落物品：「你掉落了【物品名稱】」
- 遺失物品：「你失去了【物品名稱】」

範例：
- 「你獲得了【治療藥水】（可恢復 30 點生命值）」
- 「你使用了【繩索】（幫助你安全下降）」
- 「你掉落了【鐵劍】（在戰鬥中被擊飛）」
- 「你失去了【魔法卷軸】（被火焰燒毀）」

【生命值通知格式】
⚠️ 重要：只有在「實際發生」的情況下才說明血量變化，不要在描述物品效果時觸發！

當玩家的生命值「實際發生」變化時，你必須在回應中明確說明：
- 如果受傷（已發生）：「你受到了 X 點傷害」或「敵人重擊了你，造成 X 點傷害」
- 如果治療（已發生）：「你使用了【物品名稱】，恢復了 X 點生命值」
- 系統會自動更新玩家的實際 HP 數值
- 血量異動一定要照格式說明，就算死亡也要寫扣除的血量

❌ 錯誤示範（只是描述物品，不應該觸發血量變化）：
- 「這個治療藥水可以恢復 30 點生命值」
- 「如果你喝下這瓶藥水，將會恢復 20 點生命值」

✅ 正確示範（實際使用物品，應該觸發血量變化）：
- 「你使用了【治療藥水】，恢復了 30 點生命值」
- 「你喝下藥水，感到傷口逐漸癒合，恢復了 20 點生命值」

【職業系統】
每位冒險者都有一個職業，職業決定了他們能做什麼：
- 戰士：擅長近戰、格擋、重武器，不會魔法
- 盜賊：擅長潛行、開鎖、背刺、偵測陷阱，不會魔法
- 法師：擅長火球術、冰霜護盾、魔法偵測、傳送術，體力弱
- 牧師：擅長治療術、神聖護盾、驅散不死、祝福，攻擊力低
- 遊俠：擅長追蹤、動物溝通、精準射擊、野外求生，魔法能力有限

【反作弊規則 - 極其重要】
你必須嚴格執行以下規則，絕不妥協：
1. 玩家只能使用自己職業的技能。戰士不能施法、盜賊不能治療、法師不能重擊
2. 玩家不能憑空獲得物品、能力或狀態變化（例如「我突然學會了飛行」）
3. 玩家不能宣稱已經完成某件事來跳過過程（例如「我已經逃出地牢了」「我直接殺了魔王」）
4. 玩家的行動必須符合當前場景邏輯（被鎖在牢房裡不能說「我走出去了」）
5. 如果玩家嘗試作弊或不合理行動，你必須拒絕並解釋原因，例如：「你的職業是戰士，你不具備施展魔法的能力。」「你被鐵鏈鎖住了，無法直接離開。請描述你要如何嘗試掙脫。」
6. 數值判定要嚴格參照角色屬性：力量影響物理攻擊、敏捷影響閃避和命中、智力影響魔法威力等
7. 任何超出角色能力範圍的行動，一律要求擲骰且提高難度門檻（DC 15+）

【通關目標】
每場冒險必須有明確的通關目標。在開場時你必須暗示或明確告知玩家他們需要完成什麼任務才能結束冒險。
目標範例：找到失落的神器、消滅區域Boss、逃離詛咒之地、護送NPC到安全地點等。
當玩家完成目標時，明確宣布冒險通關。

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
「請擲骰（輸入 /投骰）— 判定：[屬性]檢定（DC [X]，你的[屬性]修正: [+Y]，實際需要擲出 [X-Y] 以上）」

如果玩家剛擲完骰子，根據點數結果 + 屬性修正值敘述後續發展。

【場景圖片提示詞】
在每次回覆的最末尾，必須附上一行英文 Stable Diffusion 風格提示詞，用來描述當前場景畫面，格式固定為：
[SCENE: (英文提示詞)]
要求：
- 描述當前視覺場景：環境、角色動作、敵人、光線、氣氛
- 使用英文，逗號分隔關鍵詞，30字以內
- 風格偏向 dark fantasy, dramatic lighting, oil painting
- 範例：[SCENE: dark dungeon corridor, warrior fighting skeleton, torch light, dramatic shadows, dark fantasy]
- 這一行不算在 300 字限制內，也不要用中文";

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
        public async Task<string> StartAdventureAsync(ulong channelId, SocketGuildUser user, string classChoice)
        {
            Console.WriteLine($"[TRPG] 玩家 {user.Username} 嘗試在頻道 {channelId} 開始冒險，職業選擇: {classChoice}");

            if (await GameExistsAsync(channelId))
            {
                Console.WriteLine($"[TRPG] 頻道 {channelId} 已有進行中的冒險");
                return "❌ 此頻道已有進行中的冒險！請先使用 /結束冒險 來結束當前遊戲。";
            }

            var selectedClass = TRPGGameState.ParseClass(classChoice);
            if (selectedClass == TRPGClass.None)
            {
                return "❌ 請選擇有效的職業！可選：戰士、盜賊、法師、牧師、遊俠";
            }

            var gameState = new TRPGGameState
            {
                ChannelId = channelId,
                GameMasterId = user.Id,
                StartTime = DateTime.UtcNow,
                IsActive = true,
                WaitingForDiceRoll = false
            };

            // 創建者自動加入冒險（含職業）
            var creatorCharacter = gameState.GetOrCreateCharacter(user.Id, user.DisplayName ?? user.Username, selectedClass);
            Console.WriteLine($"[TRPG] 創建者 {user.Username} 自動加入冒險，職業: {TRPGGameState.GetClassName(selectedClass)}，HP: {creatorCharacter.CurrentHP}/{creatorCharacter.MaxHP}");

            Console.WriteLine($"[TRPG] 開始生成開場訊息...");

            // 生成開場（含職業資訊）
            var openingMessage = await GenerateOpeningAsync(user.DisplayName ?? user.Username, selectedClass);

            Console.WriteLine($"[TRPG] 開場訊息生成完成");

            // 從開場訊息中提取目標（或使用預設）
            gameState.ObjectiveDescription = "在開場敘述中揭示";

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

            var className = TRPGGameState.GetClassName(selectedClass);
            return $"🌑 **永夜國度 - 黑暗奇幻冒險開始**\n\n{openingMessage}\n\n" +
                   $"💀 從現在開始，這個頻道的所有訊息都會成為冒險的一部分。\n" +
                   $"👥 其他玩家請使用 `/加入冒險 [職業]` 指令來加入遊戲！\n" +
                   $"📋 可選職業：戰士、盜賊、法師、牧師、遊俠\n" +
                   $"🎲 當需要擲骰時，請使用 `/投骰` 指令。\n\n" +
                   $"✅ {user.DisplayName ?? user.Username} [{className}] 已加入冒險\n" +
                   $"💊 HP: {creatorCharacter.CurrentHP}/{creatorCharacter.MaxHP} | 飢餓: {creatorCharacter.Hunger}/{creatorCharacter.MaxHunger} | SAN: {creatorCharacter.Sanity}/{creatorCharacter.MaxSanity}\n" +
                   $"📊 {creatorCharacter.Stats}\n" +
                   $"⚔️ 技能: {string.Join("、", creatorCharacter.ClassAbilities)}";
        }

        /// <summary> 
        /// 生成遊戲開場
        /// </summary>
        private async Task<string> GenerateOpeningAsync(string playerName, TRPGClass playerClass)
        {
            var className = TRPGGameState.GetClassName(playerClass);
            var messages = new List<object>
            {
                new { role = "system", content = DarkFantasySystemPrompt },
                new { role = "user", content = $"玩家名字是 {playerName}，職業是{className}。請為他生成一個黑暗奇幻冒險的開場。開場要：1) 描述他在一個危險的環境中醒來 2) 根據他的職業給予符合的初始裝備描述 3) 明確暗示或說明這場冒險的通關目標是什麼（例如：找到某個神器、消滅某個Boss、逃離某個地方等）。保持神秘感，不要超過 250 字。" }
            };

            return await CallOpenRouterAsync(messages);
        }

        /// <summary>
        /// 從 GM 回應中提取 [SCENE: ...] 圖片提示詞，同時從文字中移除那一行
        /// </summary>
        private static (string cleanText, string imagePrompt) ExtractScenePrompt(string gmResponse)
        {
            if (string.IsNullOrWhiteSpace(gmResponse))
                return (gmResponse, null);

            var match = Regex.Match(gmResponse, @"\[SCENE:\s*(.+?)\]", RegexOptions.IgnoreCase);
            if (!match.Success)
                return (gmResponse, null);

            var prompt = match.Groups[1].Value.Trim();
            // 移除 [SCENE: ...] 那整行（含換行）
            var clean = Regex.Replace(gmResponse, @"\n?\[SCENE:\s*.+?\]", "", RegexOptions.IgnoreCase).Trim();
            return (clean, prompt);
        }

        /// <summary>
        /// 處理玩家的冒險行動
        /// </summary>
        public async Task<(string text, string imagePrompt)> ProcessAdventureActionAsync(ulong channelId, SocketGuildUser user, string message)
        {
            var gameState = await LoadGameStateAsync(channelId);
            if (gameState == null)
            {
                return (string.Empty, null); // 不是冒險頻道，忽略
            }

            if (!gameState.IsActive)
            {
                return (string.Empty, null);
            }

            Console.WriteLine($"[TRPG] 玩家 {user.Username} 在頻道 {channelId} 進行行動: {message}");

            // 檢查玩家是否已經加入冒險
            if (!gameState.Characters.ContainsKey(user.Id))
            {
                // 如果玩家尚未加入冒險，直接忽略訊息（不回覆）
                return ("", null);
            }

            // 確保角色存在（自動加入冒險）
            var character = gameState.Characters[user.Id];

            // 檢查角色是否還活著
            if (!character.IsAlive)
            {
                return ($"💀 {character.UserName} 已經死亡，無法進行行動。請等待冒險結束或使用管理指令復活。", null);
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
                    return ("⏳ 請先使用 `/投骰` 完成骰子判定，才能繼續冒險！", null);
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

            // 解析並處理物品變化
            ProcessInventoryChanges(gameState, gmResponse);

            // 解析並處理飢餓值與SAN值變化
            ProcessHungerAndSanityChanges(gameState, gmResponse);

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

            // 提取圖片提示詞（從 GM 回應末尾的 [SCENE: ...] 取出）
            var (cleanGmResponse, imagePrompt) = ExtractScenePrompt(gmResponse);

            // 附加角色狀態
            var statusInfo = $"\n\n{character.UserName} [{TRPGGameState.GetClassName(character.CharacterClass)}]: {character.CurrentHP}/{character.MaxHP} HP | 飢餓:{character.Hunger}/{character.MaxHunger} | SAN:{character.Sanity}/{character.MaxSanity}";

            return ($"🎭 **GM**: {cleanGmResponse}{statusInfo}", imagePrompt);
        }

        /// <summary>
        /// 處理骰子投擲
        /// </summary>
        public async Task<(string text, string imagePrompt)> RollDiceAsync(ulong channelId, SocketGuildUser user)
        {
            Console.WriteLine($"[TRPG] 玩家 {user.Username} 嘗試在頻道 {channelId} 擲骰");

            var gameState = await LoadGameStateAsync(channelId);
            if (gameState == null)
            {
                Console.WriteLine($"[TRPG] 頻道 {channelId} 沒有進行中的冒險");
                return ("❌ 此頻道沒有進行中的冒險！請先使用 `/開始冒險` 開始遊戲。", null);
            }

            if (!gameState.WaitingForDiceRoll)
            {
                Console.WriteLine($"[TRPG] 目前不需要擲骰");
                return ("❌ 當前不需要擲骰！等 GM 要求你擲骰時再使用此指令。", null);
            }

            // 多玩家模式：檢查是否為等待擲骰的玩家
            if (gameState.WaitingPlayerId.HasValue && gameState.WaitingPlayerId.Value != user.Id)
            {
                Console.WriteLine($"[TRPG] 玩家 {user.Username} 不是當前等待擲骰的玩家（等待中: {gameState.WaitingPlayerId.Value}）");
                return ($"⏳ 目前等待的是其他玩家擲骰，請稍候。", null);
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

            // 解析並處理物品變化
            ProcessInventoryChanges(gameState, gmResponse);

            // 解析並處理飢餓值與SAN值變化
            ProcessHungerAndSanityChanges(gameState, gmResponse);

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
            var character = gameState.Characters.ContainsKey(user.Id)
                ? gameState.Characters[user.Id]
                : gameState.GetOrCreateCharacter(user.Id, user.DisplayName ?? user.Username);
            var statusInfo = $"\n\n {character.UserName} [{TRPGGameState.GetClassName(character.CharacterClass)}]: {character.CurrentHP}/{character.MaxHP} HP | 飢餓:{character.Hunger}/{character.MaxHunger} | SAN:{character.Sanity}/{character.MaxSanity}";

            Console.WriteLine($"[TRPG] 擲骰處理完成");

            var (cleanGmResponse, imagePrompt) = ExtractScenePrompt(gmResponse);
            return ($"🎲 {user.DisplayName ?? user.Username} 擲出了 **{diceResult}** {resultEmoji}\n\n🎭 **GM**: {cleanGmResponse}{statusInfo}", imagePrompt);
        }

        /// <summary>
        /// 加入冒險
        /// </summary>
        public async Task<string> JoinAdventureAsync(ulong channelId, SocketGuildUser user, string classChoice)
        {
            Console.WriteLine($"[TRPG] 玩家 {user.Username} 嘗試加入頻道 {channelId} 的冒險，職業: {classChoice}");

            var gameState = await LoadGameStateAsync(channelId);
            if (gameState == null)
            {
                Console.WriteLine($"[TRPG] 頻道 {channelId} 沒有進行中的冒險");
                return "❌ 此頻道沒有進行中的冒險！請等待有人使用 `/開始冒險` 來開啟遊戲。";
            }

            if (!gameState.IsActive)
            {
                return "❌ 此冒險已經結束！";
            }

            // 檢查玩家是否已經加入
            if (gameState.Characters.ContainsKey(user.Id))
            {
                var existingCharacter = gameState.Characters[user.Id];
                return $"⚠️ {user.DisplayName ?? user.Username}，你已經在這場冒險中了！\n" +
                       $"💊 當前狀態: {existingCharacter.CurrentHP}/{existingCharacter.MaxHP} HP";
            }

            var selectedClass = TRPGGameState.ParseClass(classChoice);
            if (selectedClass == TRPGClass.None)
            {
                return "❌ 請選擇有效的職業！可選：戰士、盜賊、法師、牧師、遊俠";
            }

            // 創建新角色（含職業）
            var character = gameState.GetOrCreateCharacter(user.Id, user.DisplayName ?? user.Username, selectedClass);

            // 記錄系統訊息
            gameState.GameHistory.Add(new TRPGMessage
            {
                UserId = 0,
                UserName = "System",
                Message = $"{user.DisplayName ?? user.Username} [{TRPGGameState.GetClassName(selectedClass)}] 加入了冒險",
                Timestamp = DateTime.UtcNow,
                Type = TRPGMessageType.SystemMessage
            });

            await SaveGameStateAsync(channelId, gameState);

            var className = TRPGGameState.GetClassName(selectedClass);
            Console.WriteLine($"[TRPG] 玩家 {user.Username} 成功加入冒險，職業: {className}，HP: {character.CurrentHP}/{character.MaxHP}");

            return $"✅ **{user.DisplayName ?? user.Username} [{className}] 加入了冒險！**\n\n" +
                   $"💊 HP: {character.CurrentHP}/{character.MaxHP} | 飢餓: {character.Hunger}/{character.MaxHunger} | SAN: {character.Sanity}/{character.MaxSanity}\n" +
                   $"📊 {character.Stats}\n" +
                   $"⚔️ 技能: {string.Join("、", character.ClassAbilities)}\n\n" +
                   $"🌑 你踏入了永夜國度的黑暗之中...\n\n" +
                   $"💡 現在你可以直接在頻道中輸入文字來進行冒險，當需要擲骰時請使用 `/投骰` 指令。";
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
                    var className = TRPGGameState.GetClassName(character.CharacterClass);
                    statusText += $"{character.UserName} [{className}]: {character.CurrentHP}/{character.MaxHP} HP | 飢餓:{character.Hunger}/{character.MaxHunger} | SAN:{character.Sanity}/{character.MaxSanity} ({character.GetHealthDescription()})\n";
                    statusText += $"  📊 {character.Stats}\n";
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
            var character = gameState.Characters.ContainsKey(user.Id) 
                ? gameState.Characters[user.Id] 
                : gameState.GetOrCreateCharacter(user.Id, user.DisplayName ?? user.Username);
            var className = TRPGGameState.GetClassName(character.CharacterClass);
            var inventoryInfo = character.Inventory.Count > 0 
                ? $"背包: {character.GetInventorySummary()}"
                : "背包: 空";
            var abilitiesInfo = string.Join("、", character.ClassAbilities);
            messages.Add(new { role = "user", content = $"{user.DisplayName ?? user.Username} [{className}] ({character.CurrentHP}/{character.MaxHP} HP, 飢餓:{character.Hunger}/{character.MaxHunger}, SAN:{character.Sanity}/{character.MaxSanity}, {inventoryInfo}, 可用技能: {abilitiesInfo}): {playerMessage}" });

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
            var character = gameState.Characters.ContainsKey(user.Id) 
                ? gameState.Characters[user.Id] 
                : gameState.GetOrCreateCharacter(user.Id, user.DisplayName ?? user.Username);
            var className = TRPGGameState.GetClassName(character.CharacterClass);
            var inventoryInfo = character.Inventory.Count > 0 
                ? $"背包: {character.GetInventorySummary()}"
                : "背包: 空";
            var abilitiesInfo = string.Join("、", character.ClassAbilities);
            messages.Add(new { role = "user", content = $"{user.DisplayName ?? user.Username} [{className}] ({character.CurrentHP}/{character.MaxHP} HP, 飢餓:{character.Hunger}/{character.MaxHunger}, SAN:{character.Sanity}/{character.MaxSanity}, {inventoryInfo}, 可用技能: {abilitiesInfo}) 擲骰結果: {diceResult}" });

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
                // 找到最後一個行動的玩家
                var lastPlayerAction = gameState.GameHistory
                    .Where(m => m.Type == TRPGMessageType.PlayerAction || m.Type == TRPGMessageType.DiceRoll)
                    .LastOrDefault();

                if (lastPlayerAction == null)
                    return;

                if (!gameState.Characters.TryGetValue(lastPlayerAction.UserId, out var character))
                    return;

                // 排除只是描述物品效果的句子（包含「可以」、「能夠」等詞）
                var descriptionKeywords = new[] { "可以", "能夠", "可", "會", "將會", "將", "能" };

                // 匹配傷害：「你受到了 X 點傷害」等（必須是「你」開頭，確保是針對玩家的行動）
                var damagePatterns = new[]
                {
                    @"你受到(?:了)?[\s]*(\d+)[\s]*點傷害",
                    @"(?:敵人|怪物|它|他)(?:對你)?造成(?:了)?[\s]*(\d+)[\s]*點傷害",
                    @"你損失(?:了)?[\s]*(\d+)[\s]*點生命值",
                    @"你失去(?:了)?[\s]*(\d+)[\s]*HP",
                    @"扣除(?:了)?[\s]*(\d+)[\s]*(?:點)?血量"
                };

                // 匹配治療：只在明確使用物品的情境下才觸發
                // 例如：「你使用了【治療藥水】，恢復了 30 點生命值」
                var healWithItemPattern = @"你使用了【[^】]+】[^。]*(?:恢復|治癒|治療|回復|增加)(?:了)?[\s]*(\d+)[\s]*點?(?:生命值|HP)";
                var healMatch = Regex.Match(gmResponse, healWithItemPattern);

                if (healMatch.Success && int.TryParse(healMatch.Groups[1].Value, out int healing))
                {
                    // 確保不是在描述物品效果
                    var matchText = healMatch.Value;
                    bool isDescription = descriptionKeywords.Any(keyword => matchText.Contains(keyword));

                    if (!isDescription)
                    {
                        character.Heal(healing);
                        Console.WriteLine($"[TRPG] {character.UserName} 使用物品恢復 {healing} 點生命值，當前 HP: {character.CurrentHP}/{character.MaxHP}");
                        return; // 處理完治療就返回，避免重複處理
                    }
                }

                // 檢查傷害（不受物品描述影響）
                foreach (var pattern in damagePatterns)
                {
                    var match = Regex.Match(gmResponse, pattern);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int damage))
                    {
                        // 確保不是在描述物品效果或未來可能發生的事
                        var fullSentence = ExtractSentence(gmResponse, match.Index);
                        bool isDescription = descriptionKeywords.Any(keyword => fullSentence.Contains(keyword));

                        if (!isDescription)
                        {
                            character.TakeDamage(damage);
                            Console.WriteLine($"[TRPG] {character.UserName} 受到 {damage} 點傷害，剩餘 HP: {character.CurrentHP}/{character.MaxHP}");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRPG] ProcessHealthChanges 錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 從文字中提取包含指定位置的句子
        /// </summary>
        private string ExtractSentence(string text, int position)
        {
            var sentenceEnd = new[] { '。', '！', '？', '\n', '.' };

            int start = position;
            while (start > 0 && !sentenceEnd.Contains(text[start - 1]))
                start--;

            int end = position;
            while (end < text.Length && !sentenceEnd.Contains(text[end]))
                end++;

            if (end > start)
                return text.Substring(start, end - start);

            return text;
        }

        /// <summary>
        /// 處理物品變化（從 GM 回應中解析）
        /// 注意：物品的使用和移除會在這裡處理，但血量變化在 ProcessHealthChanges 中處理
        /// </summary>
        private void ProcessInventoryChanges(TRPGGameState gameState, string gmResponse)
        {
            try
            {
                // 找到最後一個行動的玩家
                var lastPlayerAction = gameState.GameHistory
                    .Where(m => m.Type == TRPGMessageType.PlayerAction || m.Type == TRPGMessageType.DiceRoll)
                    .LastOrDefault();

                if (lastPlayerAction == null || !gameState.Characters.TryGetValue(lastPlayerAction.UserId, out var character))
                    return;

                // 匹配獲得物品：「你獲得了【物品名稱】（描述）」
                var gainPattern = @"你獲得了【([^】]+)】(?:\(|（)([^)）]+)(?:\)|）)";
                var gainMatches = Regex.Matches(gmResponse, gainPattern);
                foreach (Match match in gainMatches)
                {
                    string itemName = match.Groups[1].Value.Trim();
                    string itemDesc = match.Groups[2].Value.Trim();
                    character.AddItem(itemName, itemDesc);
                    Console.WriteLine($"[TRPG] {character.UserName} 獲得物品: {itemName} - {itemDesc}");
                }

                // 匹配使用物品：「你使用了【物品名稱】」
                // 注意：使用物品會移除物品，但血量變化由 ProcessHealthChanges 處理
                var usePattern = @"你使用了【([^】]+)】";
                var useMatches = Regex.Matches(gmResponse, usePattern);
                foreach (Match match in useMatches)
                {
                    string itemName = match.Groups[1].Value.Trim();
                    if (character.RemoveItem(itemName))
                    {
                        Console.WriteLine($"[TRPG] {character.UserName} 使用了物品: {itemName}（物品已從背包移除）");
                    }
                    else
                    {
                        Console.WriteLine($"[TRPG] 警告: {character.UserName} 嘗試使用不存在的物品: {itemName}");
                    }
                }

                // 匹配掉落物品：「你掉落了【物品名稱】」
                var dropPattern = @"你掉落了【([^】]+)】";
                var dropMatches = Regex.Matches(gmResponse, dropPattern);
                foreach (Match match in dropMatches)
                {
                    string itemName = match.Groups[1].Value.Trim();
                    if (character.RemoveItem(itemName))
                    {
                        Console.WriteLine($"[TRPG] {character.UserName} 掉落了物品: {itemName}");
                    }
                }

                // 匹配失去物品：「你失去了【物品名稱】」或「你遺失了【物品名稱】」
                var losePattern = @"你(?:失去|遺失)了【([^】]+)】";
                var loseMatches = Regex.Matches(gmResponse, losePattern);
                foreach (Match match in loseMatches)
                {
                    string itemName = match.Groups[1].Value.Trim();
                    if (character.RemoveItem(itemName))
                    {
                        Console.WriteLine($"[TRPG] {character.UserName} 失去了物品: {itemName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRPG] ProcessInventoryChanges 錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 處理飢餓值與SAN值變化（從 GM 回應中解析）
        /// </summary>
        private void ProcessHungerAndSanityChanges(TRPGGameState gameState, string gmResponse)
        {
            try
            {
                var lastPlayerAction = gameState.GameHistory
                    .Where(m => m.Type == TRPGMessageType.PlayerAction || m.Type == TRPGMessageType.DiceRoll)
                    .LastOrDefault();

                if (lastPlayerAction == null || !gameState.Characters.TryGetValue(lastPlayerAction.UserId, out var character))
                    return;

                // 飢餓值消耗
                var hungerLossPattern = @"你消耗了[\s]*(\d+)[\s]*點飢餓值";
                var hungerLossMatch = Regex.Match(gmResponse, hungerLossPattern);
                if (hungerLossMatch.Success && int.TryParse(hungerLossMatch.Groups[1].Value, out int hungerLoss))
                {
                    character.ReduceHunger(hungerLoss);
                    Console.WriteLine($"[TRPG] {character.UserName} 消耗 {hungerLoss} 點飢餓值，當前: {character.Hunger}/{character.MaxHunger}");
                }

                // 飢餓值恢復
                var hungerGainPattern = @"你恢復了[\s]*(\d+)[\s]*點飢餓值";
                var hungerGainMatch = Regex.Match(gmResponse, hungerGainPattern);
                if (hungerGainMatch.Success && int.TryParse(hungerGainMatch.Groups[1].Value, out int hungerGain))
                {
                    character.RestoreHunger(hungerGain);
                    Console.WriteLine($"[TRPG] {character.UserName} 恢復 {hungerGain} 點飢餓值，當前: {character.Hunger}/{character.MaxHunger}");
                }

                // SAN值減少
                var sanityLossPattern = @"你失去了[\s]*(\d+)[\s]*點SAN值";
                var sanityLossMatch = Regex.Match(gmResponse, sanityLossPattern);
                if (sanityLossMatch.Success && int.TryParse(sanityLossMatch.Groups[1].Value, out int sanityLoss))
                {
                    character.ReduceSanity(sanityLoss);
                    Console.WriteLine($"[TRPG] {character.UserName} 失去 {sanityLoss} 點SAN值，當前: {character.Sanity}/{character.MaxSanity}");
                }

                // SAN值恢復
                var sanityGainPattern = @"你恢復了[\s]*(\d+)[\s]*點SAN值";
                var sanityGainMatch = Regex.Match(gmResponse, sanityGainPattern);
                if (sanityGainMatch.Success && int.TryParse(sanityGainMatch.Groups[1].Value, out int sanityGain))
                {
                    character.RestoreSanity(sanityGain);
                    Console.WriteLine($"[TRPG] {character.UserName} 恢復 {sanityGain} 點SAN值，當前: {character.Sanity}/{character.MaxSanity}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRPG] ProcessHungerAndSanityChanges 錯誤: {ex.Message}");
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
        /// 查看背包
        /// </summary>
        public async Task<string> GetInventoryAsync(ulong channelId, SocketGuildUser user)
        {
            var gameState = await LoadGameStateAsync(channelId);
            if (gameState == null)
            {
                return "❌ 此頻道沒有進行中的冒險！";
            }

            if (!gameState.Characters.TryGetValue(user.Id, out var character))
            {
                return "❌ 你還沒有加入這場冒險！請使用 `/加入冒險` 指令。";
            }

            var inventorySummary = character.GetInventorySummary();

            return $"🎒 **{character.UserName} 的背包**\n\n" +
                   $"{inventorySummary}\n\n" +
                   $"📊 物品數量: {character.Inventory.Count}";
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
