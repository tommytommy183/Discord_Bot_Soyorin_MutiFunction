# 聖杯戰爭 RPG - 開發者文件

## ??? 架構概述

### 核心組件

```
HolyGrailWarService.cs       # 主要遊戲邏輯
├── 玩家系統
│   ├── 註冊/登入
│   ├── 資料持久化
│   └── 每日獎勵
├── 召喚系統
│   ├── 稀有度抽取
│   ├── 從者生成
│   └── Atlas Academy API 整合
└── 戰鬥系統
    ├── 回合制戰鬥
    ├── 職階相剋
    ├── NP 系統
    └── PvE/PvP

HolyGrailWarVM.cs            # 資料模型
├── HgwPlayer                # 玩家資料
├── HgwServant               # 從者實例
├── HgwBattle                # 戰鬥狀態
├── ClassAdvantage           # 職階相剋邏輯
└── HgwBattleResult          # 戰鬥結果
```

---

## ?? 資料模型

### HgwPlayer
```csharp
{
    UserId: ulong,              // Discord User ID
    UserName: string,           // 玩家名稱
    Mana: int,                  // 魔力（遊戲貨幣）
    CommandSeals: int,          // 令咒（未來功能）
    Servants: List<HgwServant>, // 擁有的從者
    ActiveServantId: int?,      // 當前出戰從者
    Wins: int,                  // 勝場數
    Losses: int,                // 敗場數
    SummonCount: int,           // 召喚次數
    LastDailyBonus: DateTime?,  // 上次領取每日獎勵時間
    CreatedAt: DateTime         // 帳號創建時間
}
```

### HgwServant
```csharp
{
    InstanceId: int,            // 從者實例 ID（唯一）
    CollectionNo: int,          // FGO 圖鑑編號
    Name: string,               // 從者名稱
    ClassName: string,          // 職階
    Rarity: int,                // 稀有度 (1-5)
    Level: int,                 // 等級
    MaxHp: int,                 // 最大 HP
    CurrentHp: int,             // 當前 HP
    Attack: int,                // 攻擊力
    Defense: int,               // 防禦力
    CritRate: int,              // 爆擊率 (%)
    NpCharge: int,              // NP 充能 (0-100)
    NpName: string,             // 寶具名稱
    FaceUrl: string,            // 頭像 URL
    FullImageUrl: string        // 全身圖 URL
}
```

### HgwBattle
```csharp
{
    ChannelId: ulong,           // 戰鬥頻道 ID
    Player1Id: ulong,           // 玩家 1 ID
    Player2Id: ulong,           // 玩家 2 ID
    Player1Servant: HgwServant, // 玩家 1 從者（副本）
    Player2Servant: HgwServant, // 玩家 2 從者（副本）
    IsPlayer1Turn: bool,        // 當前回合
    TurnCount: int,             // 回合數
    BattleLog: List<string>,    // 戰鬥日誌
    IsVsNpc: bool              // 是否為 PvE
}
```

---

## ?? 遊戲機制

### 稀有度抽取機率
```csharp
RollRarity() {
    var roll = Random(0-99);
    if (roll < 1)  return 5;  // 1% SSR
    if (roll < 5)  return 4;  // 4% SR
    if (roll < 25) return 3;  // 20% R
    if (roll < 55) return 2;  // 30% UC
    return 1;                 // 45% C
}
```

### 屬性計算公式
```csharp
// 基礎屬性（依稀有度）
BaseHP = Rarity * 400 + 600;     // SSR: 2600, SR: 2200...
BaseATK = Rarity * 40 + 60;      // SSR: 260, SR: 220...

// 等級成長
MaxHP = BaseHP + (Level-1) * 50;
Attack = BaseATK + (Level-1) * 5;
Defense = 50 + (Level-1) * 3;
```

### 傷害計算
```csharp
// 普通攻擊
BaseDamage = Attacker.Attack - (Defender.Defense / 2);
ClassMultiplier = ClassAdvantage.GetMultiplier(AttackerClass, DefenderClass);
CritMultiplier = IsCritical ? 1.5 : 1.0;
FinalDamage = BaseDamage * ClassMultiplier * CritMultiplier;
MinDamage = 10;  // 保證傷害下限

// 寶具
NpDamage = Attacker.Attack * 3 * ClassMultiplier;
```

### 職階相剋倍率
- 優勢：**1.5x** 傷害
- 劣勢：**0.67x** 傷害
- 中立：**1.0x** 傷害

---

## ?? 資料持久化

### 儲存位置
```
Data/HolyGrailWar/{UserId}.json
```

### 存檔時機
- 玩家註冊
- 召喚從者
- 戰鬥結束
- 治療從者
- 選擇從者
- 領取每日獎勵

### 讀取時機
- Bot 啟動時載入所有玩家資料
- 快取於記憶體中（`Dictionary<ulong, HgwPlayer>`）

---

## ?? 外部 API

### Atlas Academy API
```
基礎從者清單：
https://api.atlasacademy.io/export/TW/basic_servant.json

詳細從者資料：
https://api.atlasacademy.io/nice/TW/servant/{collectionNo}?lore=false
```

### 資料快取策略
1. **基礎清單**：啟動時載入一次
2. **詳細資料**：按需載入並快取
3. **圖片 URL**：快取於 `_servantCache`

---

## ?? 戰鬥流程

```
1. StartBattleAsync()
   ├── 驗證雙方從者
   ├── 複製從者資料（避免汙染原始資料）
   ├── 初始化戰鬥狀態
   └── 顯示戰鬥畫面 + 按鈕

2. ExecuteBattleActionAsync()
   ├── 驗證回合
   ├── 執行行動（Attack/NP/Defend）
   ├── 檢查戰鬥結束
   ├── [PvE] NPC 自動行動
   └── 更新戰鬥畫面

3. FinishBattleAsync()
   ├── 結算勝負
   ├── 更新玩家資料（勝場/魔力）
   ├── 同步從者狀態（HP/NP）
   └── 儲存資料
```

---

## ?? 技術細節

### 執行緒安全
- `_initLock`: SemaphoreSlim(1,1) 確保初始化只執行一次
- 所有 `_players` 和 `_battles` 操作皆為原子性

### 錯誤處理
- 所有公開方法都用 try-catch 包裹
- 回傳 `(Embed, ComponentBuilder)` 統一錯誤顯示格式
- 使用 `CommonHelper.BuildErrorResponse()` 標準化錯誤訊息

### 效能優化
- 從者池預載入記憶體
- API 請求結果快取
- 異步載入非關鍵資料

---

## ?? 擴展建議

### Phase 2: 成長系統
```csharp
// 經驗值與升級
void AddExperience(int exp) {
    Experience += exp;
    while (Experience >= GetExpForNextLevel()) {
        LevelUp();
    }
}

// 技能系統
class Skill {
    string Name;
    int Level;
    SkillEffect Effect;
}
```

### Phase 3: 複雜戰鬥
```csharp
// 多從者編隊
class Team {
    HgwServant[] Servants;  // 最多 3 位
    int FrontLineIndex;     // 前排從者
}

// Buff/Debuff 系統
class BattleEffect {
    EffectType Type;
    int Duration;
    double Multiplier;
}
```

### Phase 4: 社交系統
```csharp
// 公會
class Guild {
    ulong GuildId;
    List<ulong> Members;
    int GuildLevel;
    Dictionary<string, int> Resources;
}

// 好友系統
class FriendSystem {
    List<ulong> Friends;
    Dictionary<ulong, HgwServant> SupportServants;
}
```

---

## ?? 代碼規範

### 命名規則
- Service: `{Feature}Service.cs`
- Model: `{Feature}VM.cs`
- 私有欄位: `_camelCase`
- 公開屬性: `PascalCase`

### 註解規範
```csharp
/// <summary>方法說明</summary>
/// <param name="參數">參數說明</param>
/// <returns>返回值說明</returns>
public async Task<ResultType> MethodName(ParamType param)
```

### Discord.NET 組件
```csharp
// Embed 建構
var embed = new EmbedBuilder()
    .WithTitle("標題")
    .WithDescription("內容")
    .WithColor(Color.Gold)
    .Build();

// 按鈕建構
var component = new ComponentBuilder()
    .WithButton("標籤", "customId", ButtonStyle.Primary)
    .Build();
```

---

## ?? 已知問題

1. **記憶體管理**：長時間運行後玩家資料可能過多
   - 解決方案：定期清理非活躍玩家快取

2. **API 限流**：Atlas Academy API 無官方速率限制文件
   - 解決方案：添加本地快取和重試機制

3. **並發戰鬥**：同一玩家可能在多個頻道戰鬥
   - 解決方案：改為 per-user 戰鬥狀態

---

## ?? 參考資源

- [Discord.NET 文件](https://docs.discordnet.dev/)
- [Atlas Academy API](https://api.atlasacademy.io/)
- [FGO Wiki](https://fategrandorder.fandom.com/)

---

**Happy Coding! ???**
