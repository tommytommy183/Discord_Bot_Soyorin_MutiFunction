# YGO Duel System Design Document
# Yu-Gi-Oh! 決鬥系統設計文件

## 概述

在現有 Discord Bot 架構上新增 Yu-Gi-Oh! 決鬥系統。
遵循專案既有慣例：Redis + 記憶體雙重儲存、`Task<(Embed, ComponentBuilder)>` 回傳格式、
`CommonHelper.BuildErrorResponse()` 統一錯誤處理、Singleton DI 注冊。

---

## 1. Model Classes — YgoVM.cs

```csharp
// MusicBot2/Models/YgoVM.cs
namespace MusicBot2.Models
{
    // ── YGOProDeck API 回應 ─────────────────────────────────────────
    public class YgoApiResponse
    {
        [JsonProperty("data")]
        public List<YgoCardData> Data { get; set; }
    }

    public class YgoCardData
    {
        [JsonProperty("id")]         public int Id { get; set; }
        [JsonProperty("name")]       public string Name { get; set; }
        [JsonProperty("type")]       public string Type { get; set; }     // "Effect Monster", "Spell Card", etc.
        [JsonProperty("frameType")]  public string FrameType { get; set; } // "effect","normal","spell","trap","fusion","synchro"
        [JsonProperty("desc")]       public string Desc { get; set; }
        [JsonProperty("atk")]        public int? Atk { get; set; }
        [JsonProperty("def")]        public int? Def { get; set; }
        [JsonProperty("level")]      public int? Level { get; set; }
        [JsonProperty("attribute")]  public string Attribute { get; set; }
        [JsonProperty("race")]       public string Race { get; set; }
        [JsonProperty("card_images")] public List<YgoCardImage> CardImages { get; set; }
        [JsonProperty("card_prices")] public List<YgoCardPrice> CardPrices { get; set; }
    }

    public class YgoCardImage
    {
        [JsonProperty("id")]            public int Id { get; set; }
        [JsonProperty("image_url")]     public string ImageUrl { get; set; }
        [JsonProperty("image_url_small")] public string ImageUrlSmall { get; set; }
    }

    public class YgoCardPrice
    {
        [JsonProperty("tcgplayer_price")] public string TcgplayerPrice { get; set; }
    }

    // ── 遊戲內卡牌（含執行時狀態）────────────────────────────────────
    public class YgoCard
    {
        public int ApiId { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }       // "Monster","Spell","Trap"
        public string FrameType { get; set; }
        public string Desc { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Level { get; set; }
        public string Attribute { get; set; }
        public string Race { get; set; }
        public string ImageUrl { get; set; }

        // 執行時狀態
        public bool FaceDown { get; set; }         // 伏地（Set）
        public bool IsDefensePosition { get; set; } // 守備表示
        public bool AttackedThisTurn { get; set; }
        public bool SummonedThisTurn { get; set; } // 召喚同回合不能攻擊（召喚病）

        public bool IsMonster => Type == "Monster";
        public bool IsSpell   => Type == "Spell";
        public bool IsTrap    => Type == "Trap";

        // 需要貢獻數
        public int TributeRequired =>
            Level >= 7 ? 2 :
            Level >= 5 ? 1 : 0;

        public YgoCard Clone() => (YgoCard)MemberwiseClone();
    }

    // ── 玩家場地 ─────────────────────────────────────────────────────
    public class YgoPlayerField
    {
        public ulong UserId { get; set; }
        public string UserName { get; set; }
        public int LifePoints { get; set; } = 8000;

        // 場地（最多5格）
        public List<YgoCard> MonsterZone { get; set; } = new(); // index 0-4
        public List<YgoCard> SpellTrapZone { get; set; } = new();
        public YgoCard FieldSpell { get; set; } // 場地魔法區

        public List<YgoCard> Deck { get; set; } = new();
        public List<YgoCard> Hand { get; set; } = new();
        public List<YgoCard> Graveyard { get; set; } = new();
        public List<YgoCard> Banished { get; set; } = new();

        public bool NormalSummonedThisTurn { get; set; }
        public bool DrewThisTurn { get; set; }

        public int DeckCount => Deck.Count;
        public int HandCount => Hand.Count;
    }

    // ── 決鬥狀態 ─────────────────────────────────────────────────────
    public enum DuelPhase
    {
        NotStarted,
        DrawPhase,
        StandbyPhase,
        MainPhase1,
        BattlePhase,
        MainPhase2,
        EndPhase,
        GameOver
    }

    public enum DuelMode
    {
        PvP,      // 玩家 vs 玩家
        PvAI      // 玩家 vs AI（選擇動漫主角牌組）
    }

    public class YgoDuelState
    {
        public string DuelId { get; set; }          // "{p1Id}_{p2Id}" 或 "{p1Id}_ai"
        public ulong ChannelId { get; set; }
        public ulong Player1Id { get; set; }
        public ulong Player2Id { get; set; }        // 0 = AI
        public string AiDeckName { get; set; }      // AI 使用哪個動漫牌組

        public YgoPlayerField Field1 { get; set; }  // Player1 場地
        public YgoPlayerField Field2 { get; set; }  // AI / Player2 場地

        public DuelPhase CurrentPhase { get; set; } = DuelPhase.NotStarted;
        public DuelMode Mode { get; set; }
        public int TurnNumber { get; set; } = 1;
        public ulong CurrentTurnPlayerId { get; set; }

        public bool IsP2Accepted { get; set; }      // PvP 邀請是否已接受
        public ulong InviteMessageId { get; set; }  // 邀請訊息 ID（for 刪除或更新）

        public DateTime StartTime { get; set; }
        public DateTime LastActionTime { get; set; }
        public bool IsActive { get; set; } = true;

        public List<string> BattleLog { get; set; } = new(); // 最近 10 條戰鬥記錄

        // 自然語言監聽是否啟用（開始決鬥後在頻道自動偵測）
        public bool NLPEnabled { get; set; } = true;

        public ulong CurrentTurnOpponentId =>
            CurrentTurnPlayerId == Player1Id ? Player2Id : Player1Id;

        public YgoPlayerField CurrentField =>
            CurrentTurnPlayerId == Player1Id ? Field1 : Field2;

        public YgoPlayerField OpponentField =>
            CurrentTurnPlayerId == Player1Id ? Field2 : Field1;
    }

    // ── 動漫牌組定義 ─────────────────────────────────────────────────
    public class AnimeDeckDefinition
    {
        public string Name { get; set; }         // "遊戲王"
        public string CharacterName { get; set; } // "武藤遊戲"
        public string Series { get; set; }        // "DM", "GX", "5D's", "ARC-V"
        public string Emoji { get; set; }
        public string ThemeColor { get; set; }    // hex color
        public List<string> CardNames { get; set; } // YGOProDeck 的精確卡名
        public string AiPersonality { get; set; } // OpenRouter prompt 中 AI 的個性描述
    }

    // ── 行動結果 ──────────────────────────────────────────────────────
    public class DuelActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public bool DuelEnded { get; set; }
        public ulong WinnerId { get; set; }
    }

    // ── PvP 邀請等待池 ────────────────────────────────────────────────
    public class YgoDuelInvite
    {
        public ulong ChallengerId { get; set; }
        public ulong TargetId { get; set; }
        public ulong ChannelId { get; set; }
        public string ChallengerDeckName { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
```

---

## 2. Service Class Structure — YgoDuelService.cs

```
MusicBot2/Service/YgoDuelService.cs  （預估 1800–2500 行）
```

### 2-1 建構子與欄位

```csharp
public class YgoDuelService
{
    private readonly HttpClient _httpClient;
    private readonly IDatabase _redisDb;
    private readonly bool _useRedis;
    private readonly OpenRouterService _aiService;
    private readonly Random _random = new();

    // Redis key 前綴
    private const string DUEL_KEY_PREFIX  = "ygo:duel:";      // ygo:duel:{duelId}
    private const string INVITE_KEY_PREFIX = "ygo:invite:";    // ygo:invite:{challengerId}
    private const string CARD_CACHE_PREFIX = "ygo:card:";      // ygo:card:{cardName_urlencoded}
    private const string YGO_API_BASE     = "https://db.ygoprodeck.com/api/v7/";

    // 記憶體 fallback
    private static readonly Dictionary<ulong, YgoDuelState> _memoryDuels  = new();
    private static readonly Dictionary<ulong, YgoDuelInvite> _memoryInvites = new();
    private static readonly Dictionary<string, YgoCardData> _cardCache    = new();

    // 動漫牌組定義（見第 3 節）
    private static readonly Dictionary<string, AnimeDeckDefinition> _animeDeckDefs = BuildAnimeDeckDefs();
```

### 2-2 公開方法清單

| 方法簽名 | 說明 |
|---------|------|
| `Task<(Embed, ComponentBuilder)> StartPvAIDuelAsync(ulong channelId, SocketGuildUser player, string deckName)` | 玩家挑戰 AI，選擇 AI 牌組 |
| `Task<(Embed, ComponentBuilder)> ChallengePlayerAsync(ulong channelId, SocketGuildUser challenger, SocketGuildUser target, string challengerDeck)` | 發送 PvP 邀請 |
| `Task<(Embed, ComponentBuilder)> AcceptChallengeAsync(string duelId, SocketGuildUser accepter, string accepterDeck)` | 接受 PvP 邀請 |
| `Task<(Embed, ComponentBuilder)> DeclineChallengeAsync(string duelId)` | 拒絕邀請 |
| `Task<(Embed, ComponentBuilder)> GetBoardEmbedAsync(ulong channelId, ulong requesterId)` | 顯示當前場地狀態 |
| `Task<(Embed, ComponentBuilder)> DrawCardAsync(ulong channelId, ulong playerId)` | 抽牌（抽牌階段） |
| `Task<(Embed, ComponentBuilder)> NormalSummonAsync(ulong channelId, ulong playerId, int handIndex)` | 通常召喚（包含貢獻） |
| `Task<(Embed, ComponentBuilder)> SetMonsterAsync(ulong channelId, ulong playerId, int handIndex)` | 伏地表示 |
| `Task<(Embed, ComponentBuilder)> SetSpellTrapAsync(ulong channelId, ulong playerId, int handIndex)` | 魔陷伏地 |
| `Task<(Embed, ComponentBuilder)> ActivateSpellAsync(ulong channelId, ulong playerId, int handIndex)` | 發動魔法 |
| `Task<(Embed, ComponentBuilder)> AttackAsync(ulong channelId, ulong playerId, int attackerZone, int? targetZone)` | 攻擊（直接攻擊 targetZone=null） |
| `Task<(Embed, ComponentBuilder)> ChangePositionAsync(ulong channelId, ulong playerId, int monsterZone)` | 更換表示方式 |
| `Task<(Embed, ComponentBuilder)> AdvancePhaseAsync(ulong channelId, ulong playerId)` | 進入下一個階段 |
| `Task<(Embed, ComponentBuilder)> EndTurnAsync(ulong channelId, ulong playerId)` | 結束回合（跳到 End Phase 再切換） |
| `Task<(Embed, ComponentBuilder)> SurrenderAsync(ulong channelId, ulong playerId)` | 投降 |
| `Task<(Embed, ComponentBuilder)> ShowHandAsync(ulong channelId, ulong playerId)` | DM 給自己顯示手牌 |
| `Task<(Embed, ComponentBuilder)> ShowCardInfoAsync(string cardName)` | 查詢卡片詳情（呼叫 API） |
| `Task<(Embed, ComponentBuilder)> ListDecksAsync()` | 列出所有可用動漫牌組 |
| `Task ProcessNLPMessageAsync(SocketMessage message, YgoDuelState duel)` | 自然語言解析並呼叫對應方法 |
| `Task<string> GenerateAITurnAsync(YgoDuelState duel)` | AI 回合決策（呼叫 OpenRouter） |
| `Task ExecuteAITurnAsync(ulong channelId)` | 執行完整 AI 回合邏輯 |
| `Task<YgoCardData> FetchCardAsync(string cardName)` | 呼叫 YGOProDeck API 取得卡片資料 |
| `Task<List<YgoCard>> BuildDeckAsync(string deckName)` | 用 AnimeDeckDefinition 建立卡組 |

### 2-3 私有核心方法

```
BuildBoardEmbed(YgoDuelState duel)            → EmbedBuilder
BuildHandEmbed(YgoPlayerField field)          → EmbedBuilder  
ResolveBattle(YgoCard attacker, YgoCard defender, YgoPlayerField atkField, YgoPlayerField defField) → DuelActionResult
CheckGameOver(YgoDuelState duel)              → (bool ended, ulong winnerId)
ApplySpellEffect(YgoCard spell, YgoDuelState duel, ulong casterId) → string
SaveDuelStateAsync(YgoDuelState duel)         → Task
LoadDuelStateAsync(ulong channelId)           → Task<YgoDuelState>
DeleteDuelStateAsync(string duelId)           → Task
```

---

## 3. 動漫牌組卡牌清單

> 所有卡名均符合 YGOProDeck `/cardinfo.php?name=` 端點的精確英文名稱。
> 每套牌固定 **20 張**（簡化版），玩家 + AI 各用自己的牌組洗牌後抽 5 張開局。

### 3-1 武藤遊戲 / Yugi Muto — 千年謎 Dark Magician Deck

```csharp
new AnimeDeckDefinition {
    Name = "yugi",
    CharacterName = "武藤遊戲",
    Series = "DM",
    Emoji = "🔮",
    ThemeColor = "#7B2FBE",
    AiPersonality = "你是武藤遊戲，說話謙遜但在決鬥中充滿熱情與信念。",
    CardNames = new List<string>
    {
        // 怪獸 x12
        "Dark Magician",             // Lv7 ATK2500/DEF2100
        "Dark Magician",
        "Dark Magician Girl",        // Lv6 ATK2000/DEF1700
        "Kuriboh",                   // Lv1 ATK300/DEF200
        "Kuriboh",
        "Summoned Skull",            // Lv6 ATK2500/DEF1200
        "Celtic Guardian",           // Lv4 ATK1400/DEF1200
        "Big Shield Gardna",         // Lv4 ATK100/DEF2600
        "Buster Blader",             // Lv7 ATK2600/DEF2300
        "Skilled Dark Magician",     // Lv4 ATK1900/DEF1700
        "Dark Blade",                // Lv4 ATK1800/DEF1500
        "Magician of Faith",         // Lv1 ATK300/DEF400

        // 魔法 x5
        "Dark Magic Attack",         // Normal Spell, destroy all opponent S/T
        "Polymerization",            // Fusion
        "Monster Reborn",            // Special Summon from GY
        "Pot of Greed",              // Draw 2
        "Swords of Revealing Light", // Lock opponent 3 turns

        // 陷阱 x3
        "Mirror Force",              // Destroy all attacking monsters
        "Magic Cylinder",            // Negate attack, deal damage = ATK
        "Spellbinding Circle",       // Prevent monster from attacking
    }
}
```

### 3-2 海馬瀨人 / Seto Kaiba — 青眼白龍 Deck

```csharp
new AnimeDeckDefinition {
    Name = "kaiba",
    CharacterName = "海馬瀨人",
    Series = "DM",
    Emoji = "🐉",
    ThemeColor = "#1565C0",
    AiPersonality = "你是海馬瀨人，傲慢、冷酷，把對手叫做 loser，但決鬥技術高超。",
    CardNames = new List<string>
    {
        // 怪獸 x12
        "Blue-Eyes White Dragon",    // Lv8 ATK3000/DEF2500
        "Blue-Eyes White Dragon",
        "Blue-Eyes White Dragon",
        "Lord of D.",                // Lv4 ATK1200/DEF1100
        "Vorse Raider",              // Lv4 ATK1900/DEF1200
        "Battle Ox",                 // Lv4 ATK1700/DEF1000
        "Luster Dragon",             // Lv4 ATK1600/DEF1400
        "Luster Dragon",
        "Kaiser Sea Horse",          // Lv4 ATK1700/DEF1650
        "X-Head Cannon",             // Lv4 ATK1800/DEF1500
        "Y-Dragon Head",             // Lv4 ATK1500/DEF1600
        "Z-Metal Tank",              // Lv4 ATK1500/DEF1300

        // 魔法 x5
        "The Flute of Summoning Dragon", // Special Summon 2 Dragon-type
        "Cost Down",                 // Reduce level of all hand by 2
        "Enemy Controller",          // Change ATK/DEF, or tribute to take opponent monster
        "Shrink",                    // Halve ATK
        "Monster Reborn",

        // 陷阱 x3
        "Ring of Destruction",       // Destroy 1 monster, both take damage = ATK
        "Crush Card Virus",          // Tribute 1 DARK ATK<=1000, destroy opponent's ATK>1500
        "Negate Attack",             // Negate attack and end Battle Phase
    }
}
```

### 3-3 城之內克也 / Joey Wheeler — 真紅眼黑龍 Deck

```csharp
new AnimeDeckDefinition {
    Name = "joey",
    CharacterName = "城之內克也",
    Series = "DM",
    Emoji = "🃏",
    ThemeColor = "#C62828",
    AiPersonality = "你是城之內克也，熱血、直率、有時莽撞，用街頭智慧決鬥。",
    CardNames = new List<string>
    {
        // 怪獸 x12
        "Red-Eyes B. Dragon",        // Lv7 ATK2400/DEF2000
        "Jinzo",                     // Lv6 ATK2400/DEF1500，禁止陷阱
        "Panther Warrior",           // Lv4 ATK2000/DEF1600
        "Gearfried the Iron Knight", // Lv4 ATK1800/DEF1600
        "Alligator's Sword",         // Lv4 ATK1500/DEF1200
        "Rocket Warrior",            // Lv4 ATK1500/DEF1300
        "Garoozis",                  // Lv5 ATK1800/DEF1500
        "Skull Dice",                // Lv1 ATK100/DEF100（特殊）
        "Time Wizard",               // Lv2 ATK500/DEF400
        "Thousand Dragon",           // Lv7 ATK2400/DEF2000（Time Wizard 進化用）
        "Little-Winguard",           // Lv4 ATK1400/DEF1800
        "Swordsman of Landstar",     // Lv3 ATK500/DEF1200

        // 魔法 x5
        "Graceful Dice",             // Roll die, multiply monster ATK by result
        "Dragon Nails",              // Equip, +600 ATK to Dragon
        "Scapegoat",                 // Special Summon 4 Scapegoat tokens
        "Giant Trunade",             // Return all S/T to hand
        "Monster Reborn",

        // 陷阱 x3
        "Graverobber",               // Activate GY spell of opponent
        "Skull Dice",                // Roll die, multiply ATK/DEF
        "Reinforcements",            // +500 ATK to one attacking monster
    }
}
```

### 3-4 遊城十代 / Jaden Yuki — 元素英雄 Deck

```csharp
new AnimeDeckDefinition {
    Name = "jaden",
    CharacterName = "遊城十代",
    Series = "GX",
    Emoji = "⚡",
    ThemeColor = "#E65100",
    AiPersonality = "你是遊城十代，活潑、不按牌理出牌，認為決鬥是樂趣所在。",
    CardNames = new List<string>
    {
        // 怪獸 x12
        "Elemental HERO Neos",       // Lv7 ATK2500/DEF2000
        "Elemental HERO Sparkman",   // Lv4 ATK1600/DEF1400
        "Elemental HERO Burstinatrix", // Lv3 ATK1200/DEF800
        "Elemental HERO Avian",      // Lv3 ATK1000/DEF1000
        "Elemental HERO Clayman",    // Lv4 ATK800/DEF2000
        "Elemental HERO Bubbleman",  // Lv4 ATK800/DEF1200
        "Elemental HERO Wildheart",  // Lv4 ATK1500/DEF1600
        "Wroughtweiler",             // Lv3 ATK800/DEF1000
        "Neo-Spacian Grand Mole",    // Lv3 ATK900/DEF300
        "Neo-Spacian Air Hummingbird", // Lv3 ATK800/DEF600
        "Elemental HERO Heat",       // Lv4 ATK1600/DEF1200
        "Elemental HERO Lady Heat",  // Lv4 ATK1300/DEF1000

        // 魔法 x5
        "Polymerization",            // Fusion Summon
        "Miracle Fusion",            // Fusion using GY monsters
        "O - Oversoul",              // Special Summon Normal HERO from GY
        "A Hero Lives",              // Pay 2000 LP, Special Summon Level4 or lower Elemental HERO
        "Bubble Shuffle",            // Switch Bubbleman to ATK, take control of opponent monster

        // 陷阱 x3
        "Elemental HERO Flame Wingman", // Fusion ATK2100 DEF1200（直接放成融合怪獸處理方式見附記）
        "Hero Signal",               // Special Summon 1 Level4 or lower Elemental HERO when take battle damage
        "Negate Attack",
    }
}
```

> **附記**：融合怪獸不放入主牌組而放入「融合牌組」(ExtraDeck)。
> 簡化實作：當玩家打出 Polymerization 時，若手牌/場地有對應材料，
> 直接從 ExtraDeck 清單中搜尋可用的融合怪獸，特殊召喚出來。

### 3-5 不動遊星 / Yusei Fudo — 星塵龍 Synchro Deck

```csharp
new AnimeDeckDefinition {
    Name = "yusei",
    CharacterName = "不動遊星",
    Series = "5D's",
    Emoji = "✨",
    ThemeColor = "#263238",
    AiPersonality = "你是不動遊星，沉著冷靜，相信羈絆的力量，言簡意賅。",
    CardNames = new List<string>
    {
        // 怪獸 x12（含調整者）
        "Junk Synchron",             // Lv3 TUNER ATK1300/DEF500
        "Junk Synchron",
        "Speed Warrior",             // Lv2 ATK900/DEF400
        "Speed Warrior",
        "Quillbolt Hedgehog",        // Lv2 ATK800/DEF800
        "Nitro Synchron",            // Lv2 TUNER ATK300/DEF100
        "Turbo Synchron",            // Lv1 TUNER ATK100/DEF500
        "Quickdraw Synchron",        // Lv5 TUNER ATK700/DEF1400
        "Debris Dragon",             // Lv4 TUNER ATK1000/DEF2000
        "Hyper Synchron",            // Lv4 TUNER ATK1600/DEF1300
        "Synchron Explorer",         // Lv2 ATK0/DEF0
        "Unknown Synchron",          // Lv1 TUNER ATK0/DEF0

        // 魔法 x5
        "Synchro Blast Wave",        // Destroy 1 S/T when Synchro Monster attacks
        "Graceful Revival",          // Special Summon 1 Level 1 monster from GY
        "Fighting Spirit",           // Equip, +300 for each monster opponent controls
        "Scrapstorm",                // Draw then send Synchro to GY
        "Monster Reborn",

        // 陷阱 x3
        "Scrap-Iron Scarecrow",      // Negate one attack (resets)
        "Synchro Strike",            // +500 ATK to Synchro Monster
        "Urgent Tuning",             // Synchro Summon during Battle Phase
    }
}
```

### 3-6 榊遊矢 / Yuya Sakaki — 奇異眼鐘擺龍 Pendulum Deck

```csharp
new AnimeDeckDefinition {
    Name = "yuya",
    CharacterName = "榊遊矢",
    Series = "ARC-V",
    Emoji = "🎭",
    ThemeColor = "#2E7D32",
    AiPersonality = "你是榊遊矢，充滿表演精神，相信決鬥能帶來笑容，喜歡以特技翻盤。",
    CardNames = new List<string>
    {
        // 怪獸 x12（含鐘擺怪獸）
        "Odd-Eyes Pendulum Dragon",  // Lv7 Scale:4 ATK2500/DEF2000
        "Odd-Eyes Pendulum Dragon",
        "Performapal Sword Fish",    // Lv3 Scale:3 ATK400/DEF300
        "Performapal Trampolynx",    // Lv2 Scale:4 ATK400/DEF300
        "Performapal Springoose",    // Lv5 Scale:6 ATK1800/DEF1000
        "Performapal Monkeyboard",   // Lv1 Scale:6 ATK100/DEF100
        "Performapal Skullcrobat Joker", // Lv4 Scale:8 ATK1800/DEF100
        "Performapal Partnaga",      // Lv2 Scale:1 ATK200/DEF600
        "Performapal Whip Snake",    // Lv4 ATK1700/DEF900
        "Performapal Hip Hippo",     // Lv3 Scale:6 ATK800/DEF600
        "Performapal Pendulum Sorcerer", // Lv4 Scale:2 ATK1500/DEF800
        "Odd-Eyes Dragon",           // Lv7 ATK2400/DEF2000

        // 魔法 x5
        "Sky Iris",                  // Field Spell, protect Odd-Eyes
        "Duelist Alliance",          // Search Pendulum from Deck
        "Pendulum Shift",            // Change scale of Pendulum
        "Smile World",               // +100 ATK per monster on field
        "Spiral Flame Strike",       // 1500 damage to opponent LP

        // 陷阱 x3
        "Pendulum Reborn",           // Special Summon 1 Pendulum from GY/Extra
        "Performapal Popperup",      // Increase scale by total monsters in Extra
        "Damage = Reptile",          // Special Summon reptile token when take damage
    }
}
```

---

## 4. 自然語言偵測模式

`ProcessNLPMessageAsync` 的 Regex + 關鍵字比對邏輯：

```csharp
// ── 回合推進 ──────────────────────────────────────────────────────────
// 抽牌
static readonly Regex DrawRgx = new(
    @"(draw|抽牌|抽卡|我抽|draw card|摸牌)", RegexOptions.IgnoreCase);

// 進入戰鬥階段
static readonly Regex BattlePhaseRgx = new(
    @"(battle phase|戰鬥階段|go to battle|進入戰鬥|我要攻擊)", RegexOptions.IgnoreCase);

// 結束回合
static readonly Regex EndTurnRgx = new(
    @"(end turn|結束回合|turn end|我結束|end my turn|pass)", RegexOptions.IgnoreCase);

// 下一個階段
static readonly Regex NextPhaseRgx = new(
    @"(next phase|下一個階段|phase|繼續)", RegexOptions.IgnoreCase);

// ── 召喚 ────────────────────────────────────────────────────────────
// 通常召喚：「召喚 Dark Magician」「我召喚 1號」「normal summon card 3」
static readonly Regex SummonRgx = new(
    @"(召喚|summon|normal summon|ns)\s*(?<target>.+)", RegexOptions.IgnoreCase);

// 伏地：「set 2號」「伏地 mirror force」
static readonly Regex SetRgx = new(
    @"(set|伏地|背面)\s*(?<target>.+)", RegexOptions.IgnoreCase);

// ── 攻擊 ────────────────────────────────────────────────────────────
// 「1號攻擊 對方1號」「用 Dark Magician 攻擊直接」
// 「attack 場地1 with 攻擊2」「直接攻擊」
static readonly Regex AttackRgx = new(
    @"(攻擊|attack|直接攻擊|direct attack|衝)\s*(?<target>.*)", RegexOptions.IgnoreCase);
static readonly Regex AttackFromRgx = new(
    @"(用|use|with)\s*(?<attacker>.+?)\s*(攻擊|attack)\s*(?<target>.*)", RegexOptions.IgnoreCase);

// ── 魔法/陷阱 ───────────────────────────────────────────────────────
// 「發動 Mirror Force」「activate trap」「打出 Pot of Greed」
static readonly Regex ActivateRgx = new(
    @"(發動|activate|打出|play)\s*(?<target>.+)", RegexOptions.IgnoreCase);

// ── 投降 ─────────────────────────────────────────────────────────────
static readonly Regex SurrenderRgx = new(
    @"(投降|surrender|i give up|認輸|放棄)", RegexOptions.IgnoreCase);

// ── 查看手牌 ──────────────────────────────────────────────────────────
static readonly Regex ShowHandRgx = new(
    @"(看手牌|show hand|我的手牌|看牌|check hand)", RegexOptions.IgnoreCase);

// ── 場地資訊 ──────────────────────────────────────────────────────────
static readonly Regex ShowBoardRgx = new(
    @"(show board|看場地|場地狀態|board|場面)", RegexOptions.IgnoreCase);
```

### 4-1 數字/名稱解析

手牌/場地目標解析優先級：
1. 中文數字：「1號」「第2張」→ index 0, 1
2. 阿拉伯數字：「1」「2」→ index -1（0-based）
3. 卡名模糊比對：先完整比對，再 `Contains()` 比對手牌卡名

```csharp
private int ParseCardTarget(string input, List<YgoCard> cards)
{
    // 中文序數
    var zhNumMap = new Dictionary<string, int> {
        {"1號",0},{"2號",1},{"3號",2},{"4號",3},{"5號",4},
        {"第1張",0},{"第2張",1},{"第3張",2},{"第4張",3},{"第5張",4}
    };
    foreach (var kv in zhNumMap)
        if (input.Contains(kv.Key)) return kv.Value;

    // 阿拉伯數字
    if (int.TryParse(input.Trim(), out int n) && n >= 1 && n <= cards.Count)
        return n - 1;

    // 卡名比對（忽略大小寫）
    var lower = input.ToLower().Trim();
    for (int i = 0; i < cards.Count; i++)
        if (cards[i].Name.ToLower().Contains(lower)) return i;

    return -1;
}
```

---

## 5. 規則實作計劃

### 5-1 回合結構（簡化）

```
每回合 6 個 Phase（枚舉 DuelPhase）：
DrawPhase    → 強制抽 1 張（先手第 1 回合跳過抽牌）
StandbyPhase → 觸發持續效果（簡化：直接跳過）
MainPhase1   → 召喚/設置/發動魔陷（可做多次，但通常召喚 1 次/回合）
BattlePhase  → 攻擊宣言（先手第 1 回合跳過戰鬥階段）
MainPhase2   → 再次可設置魔陷（攻擊後的補強）
EndPhase     → 手牌上限 6 張，超過需棄牌；切換到對方回合
```

### 5-2 召喚規則

```
通常召喚（Normal Summon）：
  Level 1-4：直接從手牌召喚到怪獸區（站立/攻擊表示）
  Level 5-6：需獻祭場上 1 隻怪獸
  Level 7+  ：需獻祭場上 2 隻怪獸
  每回合限 1 次通常召喚

伏地（Set）：
  怪獸伏地：FaceDown=true, IsDefensePosition=true（視為守備表示）
  魔陷伏地：FaceDown=true，MainPhase1/2 均可做

特殊召喚（Special Summon）：
  Monster Reborn、A Hero Lives、Polymerization 等卡效果觸發
  本回合限一次通常召喚不限制特殊召喚次數
```

### 5-3 攻擊規則

```
戰鬥計算：
  攻擊表示 vs 攻擊表示：
    ATK差 = atkAtk - defAtk
    若 ATK差 > 0：防守方怪獸毀滅，防守方扣 ATK差 LP
    若 ATK差 = 0：雙方怪獸毀滅，LP 不變
    若 ATK差 < 0：攻擊方怪獸毀滅，攻擊方扣 ATK差 LP

  攻擊表示 vs 守備表示（面朝上）：
    若 ATK > DEF：守備方怪獸毀滅，LP 不扣
    若 ATK = DEF：守備方怪獸毀滅，LP 不扣（攻擊方也不扣）
    若 ATK < DEF：守備方怪獸不毀滅，攻擊方扣 DEF-ATK LP

  攻擊面朝下怪獸：先翻面（reveal），再按上述守備規則計算

  直接攻擊（對方無怪獸時）：
    對方 LP -= 攻擊方 ATK

召喚病（Summoning Sickness）：
  SummonedThisTurn = true 的怪獸本回合不能攻擊
  （特殊召喚的怪獸同樣適用，除非卡文特別說明）

每隻怪獸每回合只能攻擊 1 次（AttackedThisTurn = true 後不能再攻擊）
```

### 5-4 血量判定

```
任一玩家 LifePoints <= 0 → 決鬥結束
對方牌組為 0 且需要抽牌 → 決鬥結束（Deck Out 判負）
```

### 5-5 魔法/陷阱簡化效果

只實作以下預定義效果，其餘卡牌發動時只顯示效果文字、不實際執行（需手動協議）：

```csharp
switch (card.Name)
{
    case "Monster Reborn":
        // 讓玩家選擇自己墓地一隻怪獸特殊召喚
        break;

    case "Pot of Greed":
        // 抽 2 張
        DrawCards(field, 2); break;

    case "Dark Hole":
        // 毀滅場上所有怪獸
        DestroyAllMonsters(duel); break;

    case "Swords of Revealing Light":
        // 設置 SwordsCounter=3，對方怪獸 3 回合不能攻擊
        opponentField.SwordsCounter = 3; break;

    case "Mirror Force":
        // 當對方宣告攻擊時：毀滅對方所有攻擊表示怪獸
        // （陷阱：在攻擊判定前觸發，需在 AttackAsync 中 hook）
        break;

    case "Polymerization":
        // 呼叫 FusionSummonAsync，搜尋 ExtraDeck 可融合的卡
        break;

    case "Scapegoat":
        // Special Summon 4 Sheep Token (ATK0/DEF0)
        SummonTokens(field, "Sheep Token", 4, 0, 0); break;

    case "Enemy Controller":
        // Phase 1：切換對方一隻怪獸表示方式
        // Phase 2（獻祭）：暫時奪取對方怪獸（不在此迭代實作）
        break;

    default:
        // 顯示效果文字，提示「此效果需雙方協議執行」
        break;
}
```

---

## 6. 場地 Embed 佈局設計

```
╔══════════════════════════════════════════════════════╗
║  ⚔️ YU-GI-OH! DUEL  •  Turn 3 • Main Phase 1        ║
╠══════════════════════════════════════════════════════╣
║  🔮 武藤遊戲 (AI)          LP: ████████░░  6400/8000 ║
║  Deck: 15  Hand: ●●●  GY: 2                          ║
║  ┌────┬────┬────┬────┬────┐                           ║
║  │[①] │[▓] │    │    │    │  ← 怪獸區 (ATK/DEF)      ║
║  │DM  │???  │    │    │    │                           ║
║  │ATK │SET │    │    │    │                           ║
║  │2500│    │    │    │    │                           ║
║  └────┴────┴────┴────┴────┘                           ║
║  ┌────┬────┬────┬────┬────┐                           ║
║  │[▓] │    │    │    │    │  ← 魔陷區                  ║
║  │SET │    │    │    │    │                           ║
║  └────┴────┴────┴────┴────┘                           ║
╠══════════════════════════════════════════════════════╣
║  ⚔️ === BATTLE LOG ===                                ║
║  > Dark Magician attacked Blue-Eyes! 2500 > 3000     ║
║  > 遊戲 took 500 damage! LP 7000→6500                ║
╠══════════════════════════════════════════════════════╣
║  👤 你 (海馬瀨人)           LP: ██████████  8000/8000 ║
║  Deck: 14  Hand: ●●●●  GY: 1                         ║
║  ┌────┬────┬────┬────┬────┐                           ║
║  │[①] │    │    │    │    │  ← 怪獸區                  ║
║  │BEWD│    │    │    │    │                           ║
║  │ATK │    │    │    │    │                           ║
║  │3000│    │    │    │    │                           ║
║  └────┴────┴────┴────┴────┘                           ║
║  ┌────┬────┬────┬────┬────┐                           ║
║  │    │    │    │    │    │  ← 魔陷區                  ║
║  └────┴────┴────┴────┴────┘                           ║
╠══════════════════════════════════════════════════════╣
║  💡 你的行動：                                          ║
║  [抽牌] [召喚] [攻擊] [魔陷] [結束回合]                ║
╚══════════════════════════════════════════════════════╝
```

### 6-1 實際 EmbedBuilder 對應

```csharp
private Embed BuildBoardEmbed(YgoDuelState duel)
{
    var eb = new EmbedBuilder()
        .WithTitle($"⚔️ YU-GI-OH! DUEL • Turn {duel.TurnNumber} • {PhaseToString(duel.CurrentPhase)}")
        .WithColor(new Color(0xFFD700));

    // 對方（AI / Player2）場地
    var opp = duel.OpponentField;
    var oppMonsters = BuildZoneString(opp.MonsterZone, true);
    var oppST       = BuildZoneString(opp.SpellTrapZone, true);
    eb.AddField(
        $"{opp.UserName}  LP: {opp.LifePoints}/8000  🃏 Deck:{opp.DeckCount}  ✋ Hand:{opp.HandCount}  🪦 GY:{opp.Graveyard.Count}",
        $"**怪獸區：**\n{oppMonsters}\n**魔陷區：**\n{oppST}",
        inline: false
    );

    // 戰鬥記錄
    if (duel.BattleLog.Any())
    {
        var logText = string.Join("\n", duel.BattleLog.TakeLast(5).Select(l => $"> {l}"));
        eb.AddField("📋 戰鬥記錄", logText, inline: false);
    }

    // 己方場地
    var me = duel.CurrentField;
    var myMonsters = BuildZoneString(me.MonsterZone, false);
    var myST       = BuildZoneString(me.SpellTrapZone, false);
    eb.AddField(
        $"{me.UserName}  LP: {me.LifePoints}/8000  🃏 Deck:{me.DeckCount}  ✋ Hand:{me.HandCount}  🪦 GY:{me.Graveyard.Count}",
        $"**怪獸區：**\n{myMonsters}\n**魔陷區：**\n{myST}",
        inline: false
    );

    // LP 血條
    eb.WithFooter($"⚔️ 對方 {BuildHPBar(opp.LifePoints)}  |  你 {BuildHPBar(me.LifePoints)}");

    return eb.Build();
}

private string BuildZoneString(List<YgoCard> zone, bool isOpponent)
{
    var slots = new string[5];
    for (int i = 0; i < 5; i++) slots[i] = $"[{i+1}]空";
    for (int i = 0; i < zone.Count && i < 5; i++)
    {
        var c = zone[i];
        if (c.FaceDown && isOpponent)
            slots[i] = $"[{i+1}]❓伏";
        else if (c.IsDefensePosition)
            slots[i] = $"[{i+1}]{c.Name}\n守 DEF{c.Def}";
        else
            slots[i] = $"[{i+1}]{c.Name}\n攻 ATK{c.Atk}";
    }
    return string.Join("  ", slots);
}

private string BuildHPBar(int lp)
{
    int filled = (int)Math.Round(lp / 800.0); // max 10 bars
    return new string('█', Math.Max(0, filled)) + new string('░', Math.Max(0, 10 - filled)) + $" {lp}";
}
```

---

## 7. Slash Commands

在 `SlashCommandHandler.cs` 加入以下指令：

```csharp
// ── 決鬥相關指令 ─────────────────────────────────────────────

[SlashCommand("決鬥ai", "挑戰動漫角色 AI 進行決鬥")]
[SlashCommand("duel-ai", "Challenge an anime character AI to a duel")]
// 參數：
//   deck (Choice)：你自己使用的牌組 (yugi/kaiba/joey/jaden/yusei/yuya)
//   opponent (Choice)：AI 使用的牌組 (yugi/kaiba/joey/jaden/yusei/yuya)

[SlashCommand("決鬥挑戰", "挑戰另一位玩家決鬥")]
[SlashCommand("duel-challenge", "Challenge another player to a duel")]
// 參數：
//   target (User)：挑戰對象
//   deck (Choice)：你自己使用的牌組

[SlashCommand("決鬥場地", "顯示當前決鬥場地")]
[SlashCommand("duel-board", "Show current duel board")]

[SlashCommand("決鬥手牌", "查看自己的手牌（DM 給你）")]
[SlashCommand("duel-hand", "View your hand (sent via DM)")]

[SlashCommand("決鬥抽牌", "在抽牌階段抽一張牌")]
[SlashCommand("duel-draw", "Draw a card during Draw Phase")]

[SlashCommand("決鬥召喚", "通常召喚手牌中的怪獸")]
[SlashCommand("duel-summon", "Normal summon a monster from your hand")]
// 參數：card_index (int 1-6)

[SlashCommand("決鬥伏地", "將手牌中的牌伏地")]
[SlashCommand("duel-set", "Set a card from your hand")]
// 參數：card_index (int 1-6)

[SlashCommand("決鬥攻擊", "宣告攻擊")]
[SlashCommand("duel-attack", "Declare an attack")]
// 參數：attacker_zone (int 1-5), target_zone (int 1-5, 0=direct)

[SlashCommand("決鬥發動", "發動手牌/場上的魔法或陷阱")]
[SlashCommand("duel-activate", "Activate a spell or trap")]
// 參數：card_index (int)

[SlashCommand("決鬥換相", "切換場上怪獸的攻守表示")]
[SlashCommand("duel-change", "Change a monster's battle position")]
// 參數：zone (int 1-5)

[SlashCommand("決鬥階段", "進入下一個回合階段")]
[SlashCommand("duel-phase", "Advance to next phase")]

[SlashCommand("結束回合", "直接結束本回合（跳過剩餘階段）")]
[SlashCommand("duel-endturn", "End your turn")]

[SlashCommand("決鬥投降", "投降，結束決鬥")]
[SlashCommand("duel-surrender", "Surrender and end the duel")]

[SlashCommand("決鬥牌組", "查看所有可用的動漫牌組")]
[SlashCommand("duel-decks", "Show all available anime decks")]

[SlashCommand("查詢卡片", "查詢一張卡的詳細資料")]
[SlashCommand("card-info", "Look up a Yu-Gi-Oh! card")]
// 參數：name (string)
```

---

## 8. Button IDs

所有按鈕 CustomId 遵循 `ygo_{action}_{duelId}_{param}` 格式。

`duelId` = `{player1Id}_{player2Id}`（PvP）或 `{player1Id}_ai`。

```
// ── PvP 邀請 ──────────────────────────────────────────────────
ygo_accept_{duelId}              // 接受決鬥邀請
ygo_decline_{duelId}             // 拒絕決鬥邀請

// ── 選擇牌組（邀請接受後） ────────────────────────────────────
ygo_deck_{duelId}_{deckName}     // 選擇牌組（yugi/kaiba/joey/jaden/yusei/yuya）
// e.g. ygo_deck_12345_67890_kaiba

// ── 回合行動（每次更新 Board Embed 都帶這些按鈕） ──────────────
ygo_draw_{duelId}                // 抽牌
ygo_phase_{duelId}               // 下一階段
ygo_endturn_{duelId}             // 結束回合
ygo_board_{duelId}               // 重新顯示場地
ygo_hand_{duelId}                // 查看手牌（DM）

// ── 召喚/設置選擇手牌（出現數字按鈕 1-6）────────────────────────
ygo_summon_{duelId}_{handIndex}  // 召喚手牌第 N 張 (0-based)
ygo_set_{duelId}_{handIndex}     // 伏地手牌第 N 張
ygo_activate_{duelId}_{handIndex} // 發動手牌第 N 張

// ── 攻擊選擇（先選攻擊者，再選目標）──────────────────────────────
ygo_atkselect_{duelId}_{zone}    // 選擇攻擊方怪獸（zone 1-5）
ygo_atktarget_{duelId}_{zone}    // 選擇攻擊目標（zone 1-5，zone=0 = 直接攻擊）

// ── 換相 ────────────────────────────────────────────────────────
ygo_change_{duelId}_{zone}       // 切換怪獸表示方式（zone 1-5）

// ── 貢獻選擇（Level 5+ 召喚時出現） ──────────────────────────────
ygo_tribute_{duelId}_{zone}      // 選擇獻祭哪隻場上怪獸（zone 1-5）
ygo_tribute_confirm_{duelId}     // 確認獻祭並完成召喚

// ── 融合（Polymerization 發動後）────────────────────────────────
ygo_fusion_{duelId}_{extraIndex} // 選擇從 ExtraDeck 召喚哪隻融合怪獸

// ── 投降確認 ────────────────────────────────────────────────────
ygo_surrender_confirm_{duelId}   // 確認投降
ygo_surrender_cancel_{duelId}    // 取消投降
```

### 8-1 Program.cs 路由添加

在 `InteractionCreated()` 的按鈕分派區塊加入：

```csharp
else if (component.Data.CustomId.StartsWith("ygo_accept_"))
{
    var duelId = component.Data.CustomId["ygo_accept_".Length..];
    var result = await _ygoDuelService.AcceptChallengeAsync(duelId, (SocketGuildUser)component.User, "yugi");
    await component.RespondAsync(embed: result.embed, components: result.component.Build());
}
else if (component.Data.CustomId.StartsWith("ygo_decline_"))
{
    var duelId = component.Data.CustomId["ygo_decline_".Length..];
    var result = await _ygoDuelService.DeclineChallengeAsync(duelId);
    await component.RespondAsync(embed: result.embed, components: result.component.Build());
}
else if (component.Data.CustomId.StartsWith("ygo_deck_"))
{
    // ygo_deck_{duelId}_{deckName}
    var parts = component.Data.CustomId.Split('_');
    // parts: ["ygo","deck",p1Id,p2Id,deckName] 或 ["ygo","deck",p1Id,"ai",deckName]
    // 最後一個 part 是 deckName，中間兩個是 duelId
    var deckName = parts[^1];
    var duelId   = string.Join("_", parts[2..^1]);
    var result   = await _ygoDuelService.AcceptChallengeAsync(duelId, (SocketGuildUser)component.User, deckName);
    await component.RespondAsync(embed: result.embed, components: result.component.Build());
}
else if (component.Data.CustomId.StartsWith("ygo_draw_"))
{
    var duelId = component.Data.CustomId["ygo_draw_".Length..];
    var result = await _ygoDuelService.DrawCardAsync(component.ChannelId, component.User.Id);
    await component.RespondAsync(embed: result.embed, components: result.component.Build());
}
else if (component.Data.CustomId.StartsWith("ygo_endturn_"))
{
    var result = await _ygoDuelService.EndTurnAsync(component.ChannelId, component.User.Id);
    await component.RespondAsync(embed: result.embed, components: result.component.Build());
}
else if (component.Data.CustomId.StartsWith("ygo_summon_"))
{
    var parts     = component.Data.CustomId.Split('_');
    var handIndex = int.Parse(parts[^1]);
    var result    = await _ygoDuelService.NormalSummonAsync(component.ChannelId, component.User.Id, handIndex);
    await component.RespondAsync(embed: result.embed, components: result.component.Build());
}
else if (component.Data.CustomId.StartsWith("ygo_atktarget_"))
{
    var parts  = component.Data.CustomId.Split('_');
    var zone   = int.Parse(parts[^1]);
    // attacker zone 存在 service 的 pending state
    var result = await _ygoDuelService.ConfirmAttackAsync(component.ChannelId, component.User.Id, zone);
    await component.RespondAsync(embed: result.embed, components: result.component.Build());
}
// ... 其餘按鈕依此類推
```

---

## 9. DI 注冊（Program.cs ConfigureServices）

```csharp
.AddSingleton<YgoDuelService>(sp => new YgoDuelService(
    redisConn,
    sp.GetRequiredService<OpenRouterService>()
))
```

---

## 10. AI 對手決策（OpenRouter Prompt）

```csharp
private const string AiDuelSystemPrompt = @"
你是 Yu-Gi-Oh! 決鬥 AI，扮演 {character}。
你的個性：{personality}

現在是你的回合，請根據以下場地狀態決定行動：
{boardState}

你的手牌：
{handList}

請用以下格式回覆行動指令（每行一個動作，最多 3 個）：
ACTION: SUMMON|{手牌卡名}
ACTION: ATTACK|{攻擊方卡名}→{目標卡名或DIRECT}
ACTION: SPELL|{魔法卡名}
ACTION: ENDTURN

以及對決鬥的台詞（1-2句，符合角色個性）：
SPEECH: {台詞}
";
```

解析回覆時用 Regex 提取每個 `ACTION:` 行，依序執行對應的服務方法。

---

## 11. Redis Key 命名

```
ygo:duel:{duelId}           # 決鬥狀態 JSON（duelId = {p1Id}_{p2Id}）
ygo:invite:{challengerId}   # 邀請狀態 JSON（TTL 60 秒）
ygo:channel:{channelId}     # channelId → duelId 映射（用於 NLP 查找）
ygo:card:{encodedName}      # 卡片資料快取 JSON（TTL 24 小時）
```

---

## 12. 實作順序建議

1. **YgoVM.cs** — 所有 Model 類別
2. **YgoDuelService 骨架** — 建構子、Redis 連線、`BuildAnimeDeckDefs()`
3. **FetchCardAsync + BuildDeckAsync** — API 整合與快取
4. **StartPvAIDuelAsync + GetBoardEmbedAsync** — 最小可玩流程
5. **DrawCardAsync + AdvancePhaseAsync + EndTurnAsync** — 回合推進
6. **NormalSummonAsync + AttackAsync** — 核心戰鬥
7. **SpellTrap 預定義效果** — 主要卡牌效果
8. **GenerateAITurnAsync + ExecuteAITurnAsync** — AI 對手
9. **ProcessNLPMessageAsync** — 自然語言輸入
10. **ChallengePlayerAsync + AcceptChallengeAsync** — PvP 流程
11. **Program.cs 路由 + DI 注冊**
12. **SlashCommandHandler.cs 指令**
