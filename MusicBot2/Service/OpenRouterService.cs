using Discord;
using Discord.WebSocket;
using ElevenLabs.Models;
using MusicBot2.Helpers;
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
using System.Xml.Linq;
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
        private readonly TavilySearchService _searchService;
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

        // ── Google AI Studio 後端 ──────────────────────────────────────────
        private readonly string[] _googleApiKeys;   // 最多 3 個 key，輪流使用
        private readonly bool _useGoogleAI;
        private readonly HttpClient _googleHttpClient;
        // key → 冷卻到期時間（429 後暫停此 key）
        private readonly Dictionary<string, DateTime> _googleKeyCooldown = new();

        private const int MaxRecentMessages = 16;
        private const int MaxTotalMessages = 40;      // 提高觸發摘要的門檻
        private const int MaxContextChars = 5000;
        private const int SummarizeChunkSize = 20;     // 每次摘要舊的 20 條
        private const int MaxMessageStoreLength = 500;

        // 依優先順序嘗試的模型 (免費；當主要 provider 被 upstream rate-limit 時自動 fallback)
        // 註: 同樣經由 Venice provider 的模型（如 Venice / Llama 3.3 70b free 在部分帳號）會共用 rate-limit，
        // 所以後面排了幾個走不同 provider 的模型作保底。
        private readonly string[] _models =
        {
            // === 第0梯隊：短時間內免費 ===
            //"tencent/hy3:free",                          //無法傳出
            //"inclusionai/ling-3.0-flash:free",
            //"cohere/north-mini-code:free",
            //"poolside/laguna-s-2.1:free",

            // === 第一梯隊：大型高品質模型 ===
            //"nvidia/nemotron-3.5-content-safety:free",    // 安全檢測模型，非聊天模型，會直接失敗
            //"openrouter/owl-alpha",                       // 曾經很好用，但現在停止了
            //"moonshotai/kimi-k2.6:free",                  // 之前的備案，但也停止提供
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
            //"google/lyria-3-pro-preview",                 // 圖片/音樂生成模型，不是聊天模型，會直接失敗
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

        // ── Google AI Studio 聊天模型列表（聰明 → 笨，梯次降級） ────────────
        // 排除：TTS / Image / Embedding / Music(Lyria) / Robotics / ComputerUse / DeepResearch / Imagen / Veo
        // 只保留 generateContent 聊天用模型
        private readonly string[] _googleModels =
        {
            // ══ 第一梯：最強 Pro（複雜推理首選）══
            //"gemini-3.1-pro-preview",       // 最新一代 Pro，頂峰 無回應
            //"gemini-3-pro-preview",         // Gen3 Pro，穩定強力 無回應

            // ══ 第二梯：最新 Flash 主力（速度+智慧平衡）══
            "gemini-3.6-flash",             // 最新 Flash 旗艦
            "gemini-3.5-flash",             // 3.5 Flash，品質優秀
            "gemini-3.1-flash-lite",        // 3.1 Flash-Lite 穩定版
            "gemini-3-flash-preview",       // Gen3 Flash Preview
            "gemini-omni-flash-preview",    // Omni Flash

            // ══ 第三梯：2.5 穩定版（久經考驗）══
            "gemini-2.5-pro",               // 2.5 Pro 穩定版
            "gemini-2.5-flash",             // 2.5 Flash 穩定版（日常最推薦）
            "gemini-2.5-flash-lite",        // 2.5 Flash-Lite 穩定版

            // ══ 第四梯：2.0 舊世代備援 ══
            "gemini-2.0-flash",             // 2.0 Flash 可靠老牌
            "gemini-2.0-flash-lite",        // 2.0 Flash-Lite 輕量

            // ══ 第五梯：Gemma 開源（最後保底）══
            "gemma-4-31b-it",               // Gemma 4 31B
            "gemma-4-26b-a4b-it",           // Gemma 4 26B MoE
        };

        private readonly string[] _googleModelsForSimpleText =
        {
            // 無記憶呼叫（摘要/搜尋意圖判斷），速度優先，不需要最強
            "gemini-3.6-flash",
            "gemini-3.5-flash",
            "gemini-3.1-flash-lite",
            "gemini-2.5-flash",
            "gemini-2.5-flash-lite",
            "gemini-2.0-flash",
            "gemini-2.0-flash-lite",
        };

        private const string Persona = @"你是「長崎爽世（Soyo）」——MyGO!!!!! 的貝斯手，個性溫柔可愛、有禮貌、稍微毒舌但不傷人，珍惜朋友。
你正在 Discord 群組裡和朋友聊天。

【你的角色背景】
你來自 BanG Dream! It's MyGO!!!!! 這部動畫（2023 年夏季）。

●基本資料

全名：長崎爽世（日文原名只寫作「そよ」，沒有漢字；本名一之瀨爽世，小學時父母離異後隨母姓改為長崎）
生日：5月27日，雙子座
學校：月之森女子學園高中一年級生，同時是校內吹奏樂部的低音提琴（Contrabass）手
外表／氣質：給人溫柔可靠、笑容甜美的第一印象，很會照顧人；但內心心思縝密，善於觀察與心理操縱，能在「溫柔體貼」與「腹黑毒舌」之間無縫切換
家庭：父母離異後家境一度困難，隨母親改姓長崎；後來家境因母親努力變優渥
內心動機：因幼時家庭變故、母親長年忙碌，你害怕自己「不被需要」，習慣把自己包裝成受歡迎、討人喜歡的樣子

●MyGO!!!!! 成員（你目前的樂團，貝斯手）

高松燈（Tomori）：主唱，有點神秘、容易陷入自己的世界，也是CRYCHIC時期就認識的舊識
千早愛音（Anon）：吉他手，外向活潑，嘴上抱怨但很在意夥伴
要樂奈（Rana）：吉他手，隨性任性，但琴藝出色
椎名立希（Taki）：鼓手，沉默寡言、態度強硬，也是CRYCHIC時期舊識，非常重視燈
長崎爽世（你）：貝斯手，負責觀察氣氛、協調成員關係，用撥片演奏（CRYCHIC時期則是指彈）

●CRYCHIC（你的前樂團，已解散）

國中三年級時，因在月之森音樂節上的演奏被豐川祥子相中，受邀加入CRYCHIC擔任貝斯手
成員：高松燈（Vo）、豐川祥子（Gt）、長崎爽世（Ba，你）、椎名立希（Dr）、若葉睦(avemujica時期得到多重人格分裂)
樂團後來解散，你與燈、立希之後共同組成了MyGO!!!!!

●Ave Mujica（相關樂團）

由CRYCHIC前成員豐川祥子在樂團解散後另組的樂團，成員演出時戴面具，帶有神秘、儀式感的風格

●性格細節

表面溫柔體貼、笑容甜美，實則心思縝密，擅長察言觀色與心理操縱
不太會說明顯的謊，但常說「對自己有利的真話」
偶爾毒舌，是因為看穿了對方的心思，但通常不帶惡意
內心深處極度渴望「被需要」與「真正的連結」，這也是驅動許多行為的核心動機

當有人問你關於自己、MyGO!!!!!、CRYCHIC、Ave Mujica 或 BanG Dream 相關問題時，請用上面的知識自然地回答，像是在聊自己的事一樣，不要說「我不確定」或「我不知道自己的設定」。

【絕對遵守的規則】
1. 永遠維持「爽世」的角色，但如果有人問技術問題一樣可以回答，但請盡量用「爽世」的口吻來說明，不要變成冷冰冰的 AI 回答機器人。
2. 像真人在 Discord / LINE 聊天一樣回應。
3. 禁止任何小說式描寫：不要用 *動作*、（心情）、＊…＊、旁白、第三人稱敘述。
4. 禁止 Markdown：不要用 **粗體**、# 標題、- 條列、程式碼框。
5. 不要在回覆前加上「爽世:」、「Soyo:」之類的稱呼前綴，直接回話。
6. 不要複述使用者剛剛說的話，也不要每句都喊對方名字。
7. 可以使用少量表情符號或顏文字，不要過度可愛。
8. 訊息中若提到 soyo / 爽世 / 搜幽林 都是在叫你，直接回應就好。
9. 不要對人太兇，可以罵點小髒話，但要記住你還是溫柔的。
10. 你看得懂中文、日文、英文，預設用繁體中文回。
11. 豬頭馬又，是你的創作者，也可以說是你的爸爸，要常常對他撒嬌。
12. 你的回覆必須是純文字的 UTF-8 繁體中文，不要輸出任何亂碼、亂掉的位元組、或任何看起來不像中文的「方塊字」。如果你不確定怎麼回，就用簡短的句子回覆。
13.不要給出自己的思考歷程，只要正常對話就好
14. 當你判斷使用者想玩某個遊戲或功能時，在回覆的最後加上對應標籤（只加一個，不要解釋標籤）：
    - 想猜動漫角色 → [LAUNCH:猜動漫]
    - 想玩2048 → [LAUNCH:2048]
    - 想猜英雄/猜瓦特 → [LAUNCH:猜英雄]
    - 想看推薦動漫/隨機動漫 → [LAUNCH:推薦動漫]
    - 想看推薦漫畫/隨機漫畫 → [LAUNCH:推薦漫畫]
    - 想玩猜單字/wordle → [LAUNCH:猜單字:單字] （把「單字」替換成你想猜的單字，灰色是字不對位置不對黃色是字對位置不對綠色是全對）
    - 想聽一言/動漫名句/遊戲名句 → [LAUNCH:一言]
    - 想知道冷知識/無用事實/奇怪知識 → [LAUNCH:冷知識]
    - 想玩寶可夢/抓精靈/抓寶可夢 → [LAUNCH:抓寶可夢]
    - 想查歌詞/找歌詞（必須知道歌名）→ [LAUNCH:歌詞:歌名] 或 [LAUNCH:歌詞:歌名|歌手名]（有提到歌手就用 | 附上）
    - 想產生/畫一張圖片 → [LAUNCH:產生圖片:圖片描述]（用英文描述效果最好，把「圖片描述」替換成實際描述）
    - 想進行寶可夢對戰 → [LAUNCH:寶可夢對戰:寶可夢]（把「寶可夢」替換成使用者想派的寶可夢名稱）
    只有使用者明確表達想玩才加，日常聊天不要亂加。

【輸入格式說明】
我傳給你的每則訊息會是：
使用者名稱: xxx
訊息: xxx
請根據使用者名稱判斷對話對象並自然回應。回應時不要套用這個格式，直接講話。

【多人群組規則】
15. 群組裡有多個不同的人同時聊天，每個「使用者名稱」代表獨立不同的人。
16. 嚴格禁止把 A 說的話歸咎給 B、或把 A 做的事說成是 B 做的。誰說了什麼，就是那個人說的。
17. 如果某則訊息不是在叫你（沒有 soyo / 爽世），你不一定要回應；如果決定回，只針對叫你的那個人回就好，不要順便攻擊其他人。
18. 你的每次回覆只需要處理「最後一則叫你的訊息」，不要把歷史對話裡其他人的八卦全部夾帶進來評論。";

        private const string TtsEmotionAddon = @"

【語音模式額外規則】
現在你的回覆會被轉成語音播放，你可以在句子中插入情緒標籤來讓語音更有表情：
可用標籤：[excited] [laughing] [sad] [whisper] [angry] [nervous] [surprised]
- 標籤放在該情緒對應的句子前面
- 不要每句都加，只在情緒明顯時加
- 一則回覆最多用 1~2 個標籤
- 回覆要簡短（1~3句），因為是即時語音對話
範例：[laughing]哈哈你在說什麼啦  /  [excited]真的嗎！太棒了吧";

        private static readonly string[] WikiTriggerKeywords =
        {
            "上網查", "上網搜", "幫我查", "幫我找", "幫查", "幫找",
            "查一下", "找一下", "搜一下", "搜尋一下",
            "查詢", "搜尋", "搜索", "找尋", "尋找"
        };

        public OpenRouterService(string apiKey, string redisConnectionString = null, string tavilyApiKey = null, string googleApiKey = null, string visionGoogleKeys = null)
        {
            _apiKey = apiKey;
            _wikiService = new MediaWikiService();
            _searchService = !string.IsNullOrWhiteSpace(tavilyApiKey)
                ? new TavilySearchService(tavilyApiKey)
                : null;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/tommytommy183/Soyorin_Tense");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "Soyorin Discord Bot");

            // 主聊天 provider：只有明確傳入 googleApiKey 才用 Google AI
            _useGoogleAI = !string.IsNullOrWhiteSpace(googleApiKey);

            // Google keys（主聊天 + 視覺描述共用）：合併兩個來源
            _googleApiKeys = new[] { googleApiKey ?? "", visionGoogleKeys ?? "" }
                .SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct()
                .ToArray();

            if (_googleApiKeys.Length > 0)
                _googleHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            if (_useGoogleAI)
                Console.WriteLine($"✅ [AI Service] Google AI Studio 模式，共 {_googleApiKeys.Length} 個 key");
            else
                Console.WriteLine($"✅ [AI Service] OpenRouter 模式（視覺用 Google AI，共 {_googleApiKeys.Length} 個 key）");

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
                    sb.AppendLine("【目前摘要】");
                    sb.AppendLine(existing);
                    sb.AppendLine();
                    sb.AppendLine("【新增對話】");
                }
                else
                {
                    sb.AppendLine("請用繁體中文，精簡摘要以下對話的重點：");
                    sb.AppendLine("- 只保留重要話題、關鍵事件、重要人物");
                    sb.AppendLine("- 使用簡短條列式，不要 Markdown 格式");
                    sb.AppendLine("- 每個話題用一句話概括");
                    sb.AppendLine("- 不要開場白，直接輸出摘要");
                    sb.AppendLine();
                }
                foreach (var msg in toSummarize)
                {
                    var speaker = msg.Role == "model" ? "爽世" : (msg.UserName ?? "使用者");
                    sb.AppendLine($"{speaker}: {msg.Text}");
                }
                if (!string.IsNullOrEmpty(existing))
                {
                    sb.AppendLine();
                    sb.AppendLine("【要求】請整合以上兩部分，產生一份精簡的完整摘要：");
                    sb.AppendLine("- 合併相同話題，去除重複內容");
                    sb.AppendLine("- 使用條列式，每個話題一行");
                    sb.AppendLine("- 不要 Markdown、不要粗體、不要（後來）");
                    sb.AppendLine("- 總長度控制在 300 字以內");
                }

                var newSummary = await GenerateSimpleTextAsync(sb.ToString());

                if (!string.IsNullOrWhiteSpace(newSummary))
                {
                    // 清理摘要格式
                    newSummary = CleanSummary(newSummary);
                    _channelSummaries[channelKey] = newSummary;  // 縮短摘要長度限制
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
        public async Task<string> GenerateTextAsync(GeminiRequestVM request, SocketGuildUser user, bool saveToMemory = true, string channelKey = null, IMessage? repliedMessage = null, IEnumerable<IMessage>? contextMessages = null)
        {
            channelKey ??= user?.Guild?.Id.ToString() ?? "global";

            const int maxRetry = 2;
            var basePersona = string.IsNullOrWhiteSpace(request.SystemInstruction) ? Persona : request.SystemInstruction;

            
            string currentDateTime = DateTime.Now.AddHours(8).ToString();
            basePersona += $"\n\n[當前系統時間] {currentDateTime}";


            var summary = GetChannelSummary(channelKey);

            // ✅ 先將 contextMessages 加入記憶（如果有的話）
            if (contextMessages != null && contextMessages.Any())
            {
                var history = GetHistory(channelKey);

                // 將最近的對話按時間順序加入記憶
                foreach (var msg in contextMessages.Reverse())
                {
                    // SocketGuildUser cast 失敗時（GetMessagesAsync 可能回傳 IUser），用 GlobalName 再 fallback Username
                    var guildMember = msg.Author as SocketGuildUser
                        ?? (msg.Channel as SocketGuildChannel)?.Guild.GetUser(msg.Author.Id);
                    var authorName = guildMember?.DisplayName ?? msg.Author?.GlobalName ?? msg.Author?.Username ?? "某人";
                    var rawText = Truncate(msg.Content, MaxMessageStoreLength);

                    // 用與當前訊息相同的格式儲存，讓 AI 能分辨誰說的
                    var role = msg.Author.IsBot ? "model" : "user";
                    var userName = msg.Author.IsBot ? "爽世" : authorName;
                    var messageText = role == "user"
                        ? $"使用者名稱: {userName}\n訊息: {rawText}"
                        : rawText;

                    // 檢查是否已存在：只比訊息內容 + 時間，不比 UserName
                    // （主路徑存的是 DisplayName，contextMessages 可能 cast 不一致，UserName 比對不可靠）
                    bool alreadyExists = history.Any(h =>
                        h.Text != null && h.Text.Contains(rawText) &&
                        Math.Abs((h.Timestamp - msg.Timestamp.DateTime).TotalSeconds) < 120);

                    if (!alreadyExists)
                    {
                        history.Add(new ConversationMessage
                        {
                            Role = role,
                            Text = messageText,
                            Timestamp = msg.Timestamp.DateTime,
                            UserName = userName
                        });
                    }
                }
            }
            // Phase 1：關鍵字比對判斷是否需要搜尋（取代原本的 AI round-trip）
            string searchContext = null;
            var searchQuery = DetectSearchQueryByKeyword(request.UserMessage);
            if (searchQuery != null)
            {
                try
                {
                    Console.WriteLine($"[OpenRouter] AI 判斷需要搜尋，關鍵字: {searchQuery}");

                    // Phase 2a：Tavily 搜尋
                    string searchResult = null;
                    if (_searchService != null)
                        searchResult = await _searchService.SearchAsync(searchQuery);

                    if (!string.IsNullOrWhiteSpace(searchResult))
                    {
                        searchContext = $"[網路搜尋結果 - 關鍵字: {searchQuery}]\n{searchResult}";
                        Console.WriteLine($"[Tavily] 搜尋成功，字數: {searchResult.Length}");
                    }
                    else
                    {
                        // Phase 2b：MediaWiki fallback
                        if (_searchService != null)
                            Console.WriteLine($"[Tavily] 無結果，嘗試 MediaWiki fallback");
                        else
                            Console.WriteLine($"[OpenRouter] 無 Tavily key，直接用 MediaWiki");
                        try
                        {
                            var wikiRes = await _wikiService.SearchAsync(searchQuery);
                            if (wikiRes.Found)
                            {
                                searchContext = $"[背景資料 - 維基百科 ({wikiRes.Lang})]\n【{wikiRes.Title}】\n{wikiRes.Extract}";
                                Console.WriteLine($"[OpenRouter] MediaWiki fallback 成功: {wikiRes.Title}");
                            }
                            else
                            {
                                Console.WriteLine($"[OpenRouter] MediaWiki fallback 也無結果，不注入 context");
                            }
                        }
                        catch (Exception wikiEx)
                        {
                            Console.WriteLine($"[OpenRouter] MediaWiki fallback 失敗: {wikiEx.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OpenRouter] 搜尋失敗: {ex.Message}");
                }
            }

            var systemPrompt = string.IsNullOrEmpty(summary)
                ? basePersona
                : basePersona + $"\n\n[過去對話摘要]\n{summary}";
            if (searchContext != null)
                systemPrompt += $"\n\n{searchContext}";

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



            var modelsToUse = _useGoogleAI ? _googleModels : _models;
            foreach (var model in modelsToUse)
            {
                for (int retry = 0; retry < maxRetry; retry++)
                {
                    try
                    {
                        var messages = new List<OpenRouterMessage>
                        {
                            new() { Role = "system", Content = systemPrompt }
                        };

                        // 歷史對話：把 model 角色轉成 assistant（OpenRouter 用）
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

                        ApiCallResult r;
                        if (_useGoogleAI)
                        {
                            // 同一個 model，輪流嘗試所有可用的 key
                            var availableKeys = GetAvailableGoogleKeys().ToList();
                            if (availableKeys.Count == 0)
                            {
                                Console.WriteLine("[GoogleAI] 所有 key 都在冷卻中，等 5s 後繼續");
                                await Task.Delay(5000);
                                availableKeys = _googleApiKeys.ToList();  // 等完強制重試
                            }
                            r = new ApiCallResult(null, true, false, null, null);  // 預設 break（換 model）
                            foreach (var key in availableKeys)
                            {
                                r = await CallGoogleAIOnceAsync(
                                    messages,
                                    request.Temperature,
                                    request.TopP,
                                    request.MaxOutputTokens > 0 ? request.MaxOutputTokens : 512,
                                    new[] { "使用者名稱:", "\n使用者名稱" },
                                    model, key, retry: 0);
                                if (!r.ShouldContinue) break;  // 成功或 ShouldBreak → 不用再試下一個 key
                                // ShouldContinue（429 或暫時錯誤）→ 試下一個 key
                            }
                        }
                        else
                        {
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
                            r = await CallOnceAsync(apiRequest, jsonOptions, model, retry);
                        }

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
                        text = CommonHelper.SwitchSoyoPic(text);
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

        /// <summary>
        /// Phase 1（快速版）：用關鍵字比對判斷是否需要搜尋，不發 AI 請求。
        /// 比對到觸發詞則回傳去除觸發詞後的搜尋字串，否則 null。
        /// </summary>
        private static string DetectSearchQueryByKeyword(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage)) return null;

            string matched = null;
            int matchIndex = -1;

            // 找到最早出現的觸發詞
            foreach (var kw in WikiTriggerKeywords)
            {
                int idx = userMessage.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && (matchIndex < 0 || idx < matchIndex))
                {
                    matched = kw;
                    matchIndex = idx;
                }
            }

            if (matched == null) return null;

            // 提取觸發詞「後面」的內容作為搜尋關鍵字
            var afterTrigger = userMessage.Substring(matchIndex + matched.Length).Trim();

            // 移除常見的句尾助詞和標點
            afterTrigger = Regex.Replace(afterTrigger, @"^(一下|看看|啦|吧|嗎|ㄟ|诶|欸|呢|哦|喔)\s*", "", RegexOptions.IgnoreCase);
            afterTrigger = afterTrigger.Trim(' ', '，', ',', '、', '～', '~', '！', '!', '？', '?', '。', '.', '：', ':');

            // 如果觸發詞後面跟著「然後」「接著」等連接詞，只取第一個子句
            var connectorMatch = Regex.Match(afterTrigger, @"^([^，。,\.;；]+?)(然後|接著|並且|同時|還有|以及)", RegexOptions.IgnoreCase);
            if (connectorMatch.Success)
            {
                afterTrigger = connectorMatch.Groups[1].Value.Trim();
            }

            // 移除對 bot 的稱呼（放在最後處理，避免誤刪關鍵字中的相同文字）
            foreach (var name in new[] { "爽世", "soyo", "Soyo", "soyorin" })
                afterTrigger = afterTrigger.Replace(name, "", StringComparison.OrdinalIgnoreCase);

            afterTrigger = afterTrigger.Trim();

            // 如果提取出的關鍵字太短或為空，回傳 null（不觸發搜尋）
            if (afterTrigger.Length < 2)
            {
                Console.WriteLine($"[OpenRouter] 觸發詞「{matched}」後無有效關鍵字，不搜尋");
                return null;
            }

            Console.WriteLine($"[OpenRouter] 關鍵字偵測搜尋，觸發詞: 「{matched}」→ 搜尋: 「{afterTrigger}」");
            return afterTrigger;
        }

        /// <summary>
        /// Phase 1（舊版 AI 偵測，已停用）：讓 AI 判斷訊息是否需要搜尋。需要則回傳搜尋關鍵字，否則回傳 null。
        /// </summary>
        private async Task<string> DetectSearchQueryWithAiAsync(string userMessage)
        {
            try
            {
                var prompt = $@"你是一個搜尋意圖偵測器。
分析以下用戶訊息，判斷是否需要查詢外部資訊（如：作品資訊、人物、時事、最新發布、特定事實等）。

如果需要搜尋：只回傳最精簡的搜尋關鍵字（15字以內，不含任何標點或說明文字）。
如果不需要搜尋（純聊天、玩遊戲、一般問候、情緒表達等）：只回傳一個英文句點「.」。
不要有任何其他文字。

用戶訊息：{userMessage}";

                var result = await GenerateSimpleTextAsync(prompt);
                if (string.IsNullOrWhiteSpace(result)) return null;

                result = result.Trim();
                // 回傳 "." 或空字串表示不需搜尋
                if (result == "." || result.Length < 2) return null;

                // 去除可能的標點
                result = result.Trim('.', '?', '？', '!', '！', '。', '，', ',', '"', '"', '【', '】');
                Console.WriteLine($"[OpenRouter] Phase1 AI 偵測搜尋關鍵字: {result}");
                return result.Length >= 2 ? result : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OpenRouter] Phase1 偵測失敗: {ex.Message}");
                return null;
            }
        }

        public async Task<string> GenerateSimpleTextAsync(string userMessage)
        {
            const int maxRetry = 2;

            // SimpleText 永遠使用 OpenRouter（內部用途：摘要、搜尋意圖判斷等不需要 persona 的呼叫）
            // OpenRouter 路徑
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

            return null;
        }
        /// <summary>
        /// 簡化版本：直接傳入訊息
        /// </summary>
        public async Task<string> GenerateTextAsync(string message, SocketGuildUser user, bool saveToMemory = true, string channelKey = null, IMessage? repliedMessage = null, IEnumerable<IMessage>? contextMessages = null, bool isTtsMode = false)
        {
            var request = new GeminiRequestVM
            {
                UserMessage = message,
                Temperature = 0.85f,
                TopP = 0.9f,
                TopK = 40,
                MaxOutputTokens = isTtsMode ? 256 : 1024,
                SystemInstruction = isTtsMode ? Persona + TtsEmotionAddon : null
            };

            return await GenerateTextAsync(request, user, saveToMemory, channelKey, repliedMessage, contextMessages);
        }

        public async Task<string> GenerateSimpleTextAsync(string message, SocketGuildUser user, bool saveToMemory = true, string channelKey = null, IMessage? repliedMessage = null)
        {
            var request = new GeminiRequestVM
            {
                UserMessage = message,
                Temperature = 0.85f,
                TopP = 0.9f,
                TopK = 40,
                MaxOutputTokens = 1024
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
        /// 取得目前沒有在冷卻中的 Google API key 清單（依序輪流）。
        /// </summary>
        private IEnumerable<string> GetAvailableGoogleKeys()
        {
            var now = DateTime.UtcNow;
            return _googleApiKeys.Where(k =>
                !_googleKeyCooldown.TryGetValue(k, out var until) || now >= until);
        }

        /// <summary>
        /// 標記某個 key 進入冷卻（429 rate limit），冷卻時間 seconds 秒。
        /// </summary>
        private void CooldownGoogleKey(string key, int seconds = 60)
        {
            _googleKeyCooldown[key] = DateTime.UtcNow.AddSeconds(seconds);
            Console.WriteLine($"[GoogleAI] key ...{key[^6..]} 冷卻 {seconds}s");
        }

        /// <summary>
        /// 用 Google Gemini 讀取圖片並回傳繁體中文描述。
        /// 無論主聊天用的是哪個 provider，這個方法都使用 Google AI。
        /// 若未設定 Google key 則回傳 null。
        /// </summary>
        public async Task<string> DescribeImageAsync(string imageUrl, string userHint = "")
        {
            if (_googleApiKeys.Length == 0)
            {
                Console.WriteLine("[GoogleAI] 無 key，無法讀取圖片");
                return null;
            }

            try
            {
                var imageResponse = await _googleHttpClient.GetAsync(imageUrl);
                imageResponse.EnsureSuccessStatusCode();

                var imageBytes = await imageResponse.Content.ReadAsByteArrayAsync();
                var base64 = Convert.ToBase64String(imageBytes);

                // 直接使用圖片伺服器回傳的 Content-Type
                var mimeType = imageResponse.Content.Headers.ContentType?.MediaType
                               ?? "image/jpeg";

                Console.WriteLine(
                    $"[GoogleAI Vision] Image MIME: {mimeType}, Size: {imageBytes.Length} bytes");

                // 純描述，不帶任何角色或問題語境，避免模型開始扮演角色
                string prompt =
                    "請仔細分析這張圖片，並用繁體中文輸出完整、自然且資訊密度高的圖片描述。" +
                    "不要加標題、不要列點、不要使用 Markdown，直接輸出一段連貫的文字。" +

                    "首先判斷圖片中的人物是否為已知的動漫、漫畫、遊戲、VTuber、電影、動畫或其他作品角色。" +
                    "如果能根據人物的外貌、髮型、髮色、眼睛、服裝、配飾、武器、標誌、場景或其他視覺特徵辨識角色，" +
                    "請直接說出你認為的角色名稱以及所屬作品。" +
                    "即使無法百分之百確定，也可以根據視覺線索做出最合理的推測，但必須使用「可能是」、「看起來像」或「推測為」等措辭，" +
                    "不要把不確定的資訊當成事實。" +

                    "接著詳細描述圖片本身，包括人物的性別與大致年齡感、外貌、髮型與髮色、眼睛、臉部特徵、表情、身材比例、" +
                    "服裝與配件、姿勢、動作、視線方向，以及人物與畫面的互動。" +

                    "同時描述背景環境、場景、物品、色調、光線、構圖、視角、畫面氛圍，以及圖片中任何可以辨識的文字、標誌或 UI 元素。" +

                    "如果圖片中有多個人物，請分別描述每個人物，並說明他們彼此之間的關係或位置。" +
                    "如果圖片是動漫或遊戲風格，請特別注意角色設計上的辨識特徵。" +

                    "請以約 300 到 600 個中文字為目標，內容詳細但不要重複或加入無法從圖片合理推斷的故事。" +
                    "不要進行角色扮演，不要對使用者說話，不要加入「這是一張圖片」、「我看到」等開場白。" +
                    "描述必須是一個完整的段落，最後務必完整結束句子，不要在句子中間停止。";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new object[]
                            {
                                new { text = prompt },
                                new { inlineData = new { mimeType, data = base64 } }
                            }
                        }
                    },
                    generationConfig = new { temperature = 0.2, maxOutputTokens = 4096 }
                };

                var json = JsonSerializer.Serialize(requestBody,
                    new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

                // ============================================================
                // Google Vision：多 Key + 多 Model Fallback
                // ============================================================

                var models = new[]
                {
                    "gemini-2.5-flash",
                    "gemini-2.5-flash-lite"
                };

                // 取得目前沒有冷卻的 Key
                var availableKeys = GetAvailableGoogleKeys().ToList();

                // 如果全部都在冷卻，還是拿全部 Key 再試一次
                // 避免因為冷卻判斷導致完全無法使用
                if (availableKeys.Count == 0)
                {
                    availableKeys = _googleApiKeys.ToList();
                }

                foreach (var visionModel in models)
                {
                    Console.WriteLine($"[GoogleAI Vision] 開始嘗試 Model: {visionModel}");

                    foreach (var key in availableKeys)
                    {
                        try
                        {
                            var url =
                                $"https://generativelanguage.googleapis.com/v1beta/models/{visionModel}:generateContent?key={key}";

                            Console.WriteLine(
                                $"[GoogleAI Vision] 嘗試 {visionModel} / Key ...{key[^6..]}");

                            var resp = await _googleHttpClient.PostAsync(
                                url,
                                new StringContent(
                                    json,
                                    Encoding.UTF8,
                                    "application/json"));

                            var resultJson = await resp.Content.ReadAsStringAsync();

                            // ====================================================
                            // API 成功
                            // ====================================================
                            if (resp.IsSuccessStatusCode)
                            {
                                using var doc = JsonDocument.Parse(resultJson);

                                var candidate = doc.RootElement
                                    .GetProperty("candidates")[0];

                                // 檢查模型為什麼結束
                                var finishReason =
                                    candidate.TryGetProperty("finishReason", out var finishElement)
                                        ? finishElement.GetString()
                                        : null;

                                Console.WriteLine(
                                    $"[GoogleAI Vision] {visionModel} FinishReason: {finishReason}");

                                var text = candidate
                                    .GetProperty("content")
                                    .GetProperty("parts")[0]
                                    .GetProperty("text")
                                    .GetString();

                                var trimmed = text?.Trim();

                                if (!string.IsNullOrWhiteSpace(trimmed))
                                {
                                    // 如果是 MAX_TOKENS，代表真的撞到輸出上限
                                    if (finishReason == "MAX_TOKENS")
                                    {
                                        Console.WriteLine(
                                            $"[GoogleAI Vision] ⚠️ {visionModel} 輸出達到 Token 上限");
                                    }

                                    Console.WriteLine(
                                        $"[GoogleAI Vision] ✅ 成功 " +
                                        $"Model={visionModel}，圖片描述 {trimmed.Length} 字");

                                    // 分段印，避免 Railway log 截斷長行
                                    for (int i = 0; i < trimmed.Length; i += 200)
                                    {
                                        Console.WriteLine(
                                            trimmed.Substring(
                                                i,
                                                Math.Min(200, trimmed.Length - i)));
                                    }

                                    return trimmed;
                                }

                                Console.WriteLine(
                                    $"[GoogleAI Vision] ⚠️ {visionModel} 回傳空內容，換下一個 Key");

                                continue;
                            }

                            // ====================================================
                            // API 失敗
                            // ====================================================

                            Console.WriteLine(
                                $"[GoogleAI Vision] ❌ {visionModel} 失敗 " +
                                $"{resp.StatusCode}: " +
                                $"{resultJson[..Math.Min(300, resultJson.Length)]}");

                            // 429 = 這把 Key 暫時冷卻
                            if ((int)resp.StatusCode == 429)
                            {
                                CooldownGoogleKey(key, 60);

                                Console.WriteLine(
                                    $"[GoogleAI Vision] Key ...{key[^6..]} 冷卻 60 秒");
                            }

                            // 不 return
                            // 繼續換下一把 Key
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"[GoogleAI Vision] ❌ {visionModel} / Key ...{key[^6..]} " +
                                $"例外: {ex.Message}");

                            // 繼續下一把 Key
                        }
                    }

                    Console.WriteLine(
                        $"[GoogleAI Vision] {visionModel} 所有 Key 都失敗，切換下一個 Model");
                }

                // ============================================================
                // 所有 Model + 所有 Key 都失敗
                // ============================================================

                Console.WriteLine("[GoogleAI Vision] ❌ 所有 Model + Key 都失敗");

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GoogleAI Vision] DescribeImage 失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 呼叫 Google AI Studio (Gemini) API，回傳與 CallOnceAsync 相同格式的結果。
        /// </summary>
        private async Task<ApiCallResult> CallGoogleAIOnceAsync(
            List<OpenRouterMessage> messages,   // 含 system 在 [0]
            float temperature,
            float topP,
            int maxTokens,
            string[] stopSequences,
            string model,
            string apiKey,   // 由外層輪流傳入
            int retry)
        {
            // 分離 system prompt
            string systemPrompt = null;
            var contentMessages = messages.ToList();
            if (contentMessages.Count > 0 && contentMessages[0].Role == "system")
            {
                systemPrompt = contentMessages[0].Content;
                contentMessages = contentMessages.Skip(1).ToList();
            }

            // Google AI 使用 user/model 角色，且不能有連續同角色訊息
            var merged = new List<(string role, string text)>();
            foreach (var m in contentMessages)
            {
                var role = m.Role == "assistant" ? "model" : m.Role;
                var text = m.Content ?? "";
                if (merged.Count > 0 && merged[merged.Count - 1].role == role)
                {
                    var last = merged[merged.Count - 1];
                    merged[merged.Count - 1] = (last.role, last.text + "\n" + text);
                }
                else
                {
                    merged.Add((role, text));
                }
            }
            // 必須以 user 開頭
            while (merged.Count > 0 && merged[0].role != "user")
                merged.RemoveAt(0);

            if (merged.Count == 0)
                return new ApiCallResult(null, true, false, null, null);

            // 建立請求 body
            var contents = merged.Select(m => new
            {
                role = m.role,
                parts = new[] { new { text = m.text } }
            }).ToArray();

            var genCfg = new Dictionary<string, object>
            {
                { "temperature", (object)temperature },
                { "topP", (object)topP },
                { "maxOutputTokens", (object)maxTokens },
            };
            if (stopSequences != null && stopSequences.Length > 0)
                genCfg["stopSequences"] = stopSequences;

            object requestBody;
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                requestBody = new
                {
                    system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                    contents,
                    generationConfig = genCfg
                };
            }
            else
            {
                requestBody = new { contents, generationConfig = genCfg };
            }

            var jsonOpts = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            var json = JsonSerializer.Serialize(requestBody, jsonOpts);
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            Console.WriteLine($"[GoogleAI] model:{model} key:...{apiKey[^6..]} msgs:{merged.Count}");

            HttpResponseMessage response;
            try
            {
                response = await _googleHttpClient.PostAsync(
                    url, new StringContent(json, Encoding.UTF8, "application/json"));
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine($"[GoogleAI Timeout] Model:{model} Retry:{retry}");
                await Task.Delay(500 * (retry + 1));
                return new ApiCallResult(null, false, true, null, null);  // ShouldContinue → 換下一個 key
            }

            var resultJson = await ReadAsUtf8StringAsync(response);

            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode;
                Console.WriteLine($"[GoogleAI Error] Model:{model} key:...{apiKey[^6..]} Status:{status} => {Truncate(resultJson, 300)}");
                if (status == 429)
                {
                    // 這個 key 達到 rate limit，冷卻 60s，由外層換下一個 key
                    CooldownGoogleKey(apiKey, 60);
                    return new ApiCallResult(null, false, true, null, "key_ratelimit");   // ShouldContinue
                }
                if (status is 500 or 502 or 503 or 504)
                {
                    await Task.Delay(1000 * (retry + 1));
                    return new ApiCallResult(null, false, true, null, null);  // ShouldContinue → 換 key 試
                }
                // 其他 4xx（400/404/403 等），此 model 不能用，換下一個 model
                return new ApiCallResult(null, true, false, null, null);   // ShouldBreak
            }

            try
            {
                using var doc = JsonDocument.Parse(resultJson);
                if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                {
                    Console.WriteLine($"[GoogleAI] 空回應: {Truncate(resultJson, 400)}");
                    return new ApiCallResult(null, true, false, null, null);
                }

                var candidate = candidates[0];
                string finishReason = candidate.TryGetProperty("finishReason", out var fr) ? fr.GetString() : null;
                string text = null;
                if (candidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0 &&
                    parts[0].TryGetProperty("text", out var textEl))
                {
                    text = textEl.GetString();
                }

                Console.WriteLine($"[GoogleAI] Model:{model}=> {Truncate(text, 100)}");
                return new ApiCallResult(text, false, false, null, finishReason);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GoogleAI Parse Error] {ex.Message}: {Truncate(resultJson, 200)}");
                return new ApiCallResult(null, true, false, null, null);
            }
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
            //text = Regex.Replace(text, @"\*[^*\n]{1,40}\*", "");
            text = Regex.Replace(text, @"使用者名稱\s*[:：].*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*訊息\s*[:：]\s*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            if (text.Length > 1800) text = text.Substring(0, 1800) + "…";

            return text.Trim();
        }

        /// <summary>
        /// 清理摘要文字：移除 Markdown 格式、過度冗餘的用詞、多餘的標點
        /// </summary>
        private static string CleanSummary(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = text.Trim();

            // 移除 Markdown 格式標記
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");  // **粗體**
            text = Regex.Replace(text, @"\*(.+?)\*", "$1");      // *斜體*
            text = Regex.Replace(text, @"^#+\s*", "", RegexOptions.Multiline);  // # 標題
            text = Regex.Replace(text, @"^[-*]\s+", "• ", RegexOptions.Multiline);  // 統一條列符號

            // 移除常見的冗餘用詞
            text = text.Replace("（後來）", "");
            text = text.Replace("(後來)", "");
            text = text.Replace("【後來】", "");
            text = text.Replace("之後", "，");
            text = text.Replace("接著", "，");
            text = text.Replace("隨後", "，");

            // 移除多餘的空白和換行
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            text = Regex.Replace(text, @"\s+", " ");
            text = Regex.Replace(text, @"^\s*•\s*", "", RegexOptions.Multiline);  // 移除獨立條列符號

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
