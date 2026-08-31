# ?? 聖杯塔 Roguelike — 完整重設計文件 v2.0

> **閱讀說明**：本文件純為設計提案，尚未寫入任何程式碼。
> 請確認方向後再進行實作。

---

## 一、為什麼要重寫？

| 問題 | 舊版 | 新版目標 |
|------|------|---------|
| 戰鬥無聊 | 只有攻擊/防禦/寶具三個按鈕 | FGO 5張牌抽3張的卡牌選擇機制 |
| 寶具名稱假 | NPC寶具名稱顯示為「無」 | 從 Atlas API 取得真實寶具名稱+效果 |
| 儲存問題 | JSON 本地檔案 | Redis（已完成） |
| 戰鬥感 | 一個英靈打一個怪 | 3英靈小隊，多波敵人，有AOE/單體概念 |

---

## 二、FGO 真實戰鬥機制研究

### 2-1 卡牌系統 (Command Card System)

FGO 每位從者有 **5 張指令卡**，卡型配比視職階不同：

| 職階 | Buster | Arts | Quick | 典型例子 |
|------|--------|------|-------|---------|
| Saber | 2 | 1 | 2 | 阿爾托莉亞 |
| Archer | 1 | 3 | 1 | 吉爾伽美什 |
| Lancer | 2 | 2 | 1 | 庫夫林 |
| Berserker | 4 | 0 | 1 | 赫拉克勒斯 |
| Caster | 1 | 3 | 1 | 美狄亞 |
| Assassin | 0 | 2 | 3 | 佐佐木小次郎 |

**每回合流程：**
1. 從所有出場英靈的卡池（最多 3 人 × 5 張 = 15 張）隨機抽出 **5 張**
2. 玩家從這 5 張中選 **3 張**，按順序打出
3. 卡牌效果發動 → 敵人行動

### 2-2 三種卡型效果

| 卡型 | 圖示 | 主效果 | 副效果 |
|------|------|--------|--------|
| **Buster** | ?? | 高倍率物理爆發傷害 | 無NP獲得，無星星 |
| **Arts** | ?? | 中等傷害 | 高 NP 充能（+15~20%） |
| **Quick** | ?? | 低傷害 | 產生「暴擊星」（+6~10顆） |

### 2-3 卡牌連擊加成 (Chain Bonus)

**同色三連（Color Chain）：**
- Buster Chain：第1張 ×1.0, 第2張 ×1.5, 第3張 ×2.0
- Arts Chain：所有NP獲得量 ×1.5
- Quick Chain：所有暴擊星數量 ×2.0

**Brave Chain（同一英靈打3張）：**
- 追加第 4 次 Extra 攻擊（高傷害）

**First Card 加成：**
- 首張 Buster → 全部卡牌傷害 ×1.5
- 首張 Arts → 全部卡牌 NP +10%
- 首張 Quick → 全部卡牌暴擊星 +2

### 2-4 寶具系統 (Noble Phantasm)

**觸發條件：** NP 計量表達到 100%（不同英靈上限可能是 50%~300% 視設定，本遊戲統一 100%）

**Atlas Academy API 提供的寶具資料：**
```json
{
  "noblePhantasms": [{
    "name": "約束勝利之劍",
    "ruby": "Excalibur",
    "card": "buster",
    "npGain": { "buster": 300, "arts": 300 },
    "functions": [{
      "funcType": "damageNp",
      "targetType": "enemy",
      "svals": [{ "Value": 600, "Value2": 1500 }]
    }]
  }]
}
```

**寶具類型分類：**

| 類型 | 說明 | 代表英靈 |
|------|------|---------|
| 單體高傷 (ST) | 對單敵造成超高傷害 | 庫夫林、江流兒 |
| 全體傷害 (AOE) | 對所有敵人造成傷害 | 阿爾托莉亞、吉爾伽美什 |
| 輔助型 | 無傷害，提供BUFF | 美狄亞（NP回復）、安娜貝爾（大號令） |
| 混合型 | 傷害+效果 | 庫夫林?改（傷害+消除毒） |

### 2-5 暴擊系統 (Critical Hits)

- Quick 卡打出後，根據計算生成 **暴擊星（Critical Stars）**
- 下一回合起，每顆星提升英靈 **暴擊率 +2%**（疊加計算）
- 暴擊傷害倍率：**×2.0**
- 暴擊命中時 NP 額外 +1%/星

---

## 三、Discord Bot 實作方案

### 3-1 技術挑戰：Discord 按鈕限制

Discord 每則訊息最多 **5 行按鈕 × 5 個 = 25 個**

FGO 原版需要顯示 5 張牌讓玩家選 3 張，加上 NP、英靈資訊 → 可能超過限制。

**解決方案：兩段式互動**

**階段 1：抽牌展示（5 張）**
```
?????? 本回合手牌 ??????
[??阿爾托莉亞·B] [??阿爾托莉亞·A] [??吉爾·Q]
[??赫拉克勒斯·B] [??赫拉克勒斯·B]
??????????????????????
選牌順序：[已選1] [已選2] [已選3]
[? 確認出手]
```

**每張牌一個按鈕，customId 格式：**
```
hgwt_card_{channelId}_{servantIndex}_{cardType}_{cardIndex}
例：hgwt_card_1234567890_0_buster_2
```

**階段 2：選完3張確認打出**

玩家按確認後：
1. Bot 計算所有傷害、NP獲得、暴擊星
2. 顯示完整戰鬥動畫（文字）
3. 敵人自動反擊
4. 顯示新的回合狀態

### 3-2 NP 展示按鈕

當 NP ? 100% 時，在手牌頁面顯示：
```
[?? 阿爾托莉亞 約束勝利之劍 — NP 100%]
```
（可替換其中一張已選的牌）

---

## 四、完整遊戲資料結構設計

### 4-1 玩家資料（Redis 持久化）

```csharp
public class HgwTowerPlayer
{
    public ulong UserId;
    public string UserName;
    public int SummonTickets;       // 召喚券
    public int SaintQuartz;         // 聖晶石
    public List<HgwOwnedServant> OwnedServants;  // 圖鑑
    public int HighestFloor;        // 最高層紀錄
    public int TotalRuns;
    public int TotalKills;
    public DateTime? LastDailyReward;
}
```

### 4-2 從者資料（圖鑑）

```csharp
public class HgwOwnedServant
{
    // 基礎資訊（從API取得）
    public int CollectionNo;
    public string Name;
    public string ClassName;        // "saber", "archer", etc.
    public int Rarity;

    // 玩家培養數值
    public int Level;               // 1~100
    public int NpLevel;             // 1~5

    // 從API取得的戰鬥數據
    public int BaseAtk;             // API 的 atkBase
    public int BaseHp;              // API 的 hpBase
    public string NpName;           // 寶具名稱
    public string NpRuby;           // 寶具讀音（如 Excalibur）
    public string NpCard;           // "buster"/"arts"/"quick"
    public string NpTargetType;     // "enemy"/"enemyAll"
    public int NpDmgMultiplier;     // 從API取得的傷害倍率
    public string NpEffect;         // 描述文字（從funcType/funcDesc整合）

    // 指令卡配置（從API取得）
    public List<string> Cards;      // 例如 ["buster","buster","arts","quick","quick"]
    public string FaceUrl;
}
```

### 4-3 Run 內戰鬥狀態

```csharp
public class HgwRunState
{
    public ulong ChannelId;
    public ulong PlayerId;
    public int CurrentFloor;
    public int Gold;
    public List<HgwRunServant> Team;    // 3位上場英靈的當前狀態
    public HgwFloorEncounter Encounter; // 當前遭遇
    public HgwBattleState? Battle;      // 當前戰鬥（若在戰鬥中）
}

public class HgwRunServant
{
    public int CollectionNo;
    public string Name;
    public string ClassName;
    public string[] Cards;          // 5張指令卡
    public int CurrentHp;
    public int MaxHp;
    public int Attack;
    public float NpGauge;           // 0.0f ~ 1.0f
    public string NpName;
    public string NpCard;           // 寶具卡型
    public string NpTargetType;
    public int NpDmgMultiplier;
    public string NpEffect;
    public string FaceUrl;

    // Run 內加成
    public float AtkBuff;
    public float DefBuff;
}

public class HgwBattleState
{
    public List<HgwEnemy> Enemies;
    public int TurnCount;
    public int CritStars;           // 當前累積的暴擊星

    // 選牌進度
    public List<HgwCardPlay>? SelectedCards;  // 玩家已選的牌（最多3張）
    public List<HgwCardPlay> HandCards;        // 本回合抽到的5張牌
    public BattlePhase Phase;       // Drawing/Selecting/EnemyTurn/Result
}

public enum BattlePhase { Drawing, Selecting, EnemyTurn, Result }

public class HgwCardPlay
{
    public int ServantIndex;        // 0/1/2
    public string CardType;         // "buster"/"arts"/"quick"/"np"
    public int CardIndex;           // 0~4 (牌在手中的位置)
}

public class HgwEnemy
{
    public string Name;
    public string Class;            // 職階（用於相剋計算）
    public int CurrentHp;
    public int MaxHp;
    public int Attack;
    public bool IsElite;
    public bool IsBoss;
    public string Skills;           // 技能描述
}
```

---

## 五、戰鬥計算公式（完整版）

### 5-1 傷害計算

```
傷害 = 基礎傷害 × 卡型倍率 × 位置修正 × First Card 加成
      × 職階相剋 × 暴擊倍率 × NP倍率（寶具時）

基礎傷害 = 英靈攻擊力 × 0.23

卡型基礎倍率：
  Buster: 1.5x
  Arts:   1.0x
  Quick:  0.8x

位置修正（同色連擊時）：
  第1張: 1.0x
  第2張: 1.5x（若同色）
  第3張: 2.0x（若同色）

First Card 加成（首張卡型的整體加成）：
  首張 Buster → 全部 ×1.5 攻擊加成
  首張 Arts   → 全部 NP+10%
  首張 Quick  → 全部暴擊星+2

暴擊倍率：
  未暴擊: 1.0x
  暴擊:   2.0x（消耗7顆星觸發暴擊）

職階相剋：
  優勢: 2.0x
  劣勢: 0.5x
  中立: 1.0x
```

### 5-2 NP 充能計算

```
NP獲得量 = 基礎NP獲得 × 卡型NP率 × First Card 加成

卡型NP率：
  Buster: 0%（無NP）
  Arts:   +20%
  Quick:  +10%

Arts Chain 加成：Arts Chain 時所有NP ×1.5
```

### 5-3 暴擊星計算

```
暴擊星 = 基礎星 × 卡型星率

卡型星率：
  Buster: 0顆
  Arts:   1~2顆
  Quick:  6~10顆（依英靈星出率加成）

Quick Chain 加成：暴擊星 ×2.0
```

---

## 六、API 資料取得策略

### 6-1 需要的 API 欄位

**召喚時（一次性讀取並快取）：**

```
GET https://api.atlasacademy.io/nice/TW/servant/{id}

需要的欄位：
- cards: string[]          → 指令卡配置（5張）
- atkBase / hpBase         → 基礎攻擊/HP
- noblePhantasms[].name    → 寶具名
- noblePhantasms[].ruby    → 寶具讀音
- noblePhantasms[].card    → 寶具卡型 buster/arts/quick
- noblePhantasms[].functions[].funcType  → 效果類型
- noblePhantasms[].functions[].targetType → 單體/範圍
- noblePhantasms[].npGain  → NP獲得量
- extraAssets.faces.ascension  → 頭像URL
```

**funcType 對應效果描述：**

| funcType | 遊戲描述 |
|----------|---------|
| damageNp | 對敵人造成傷害 |
| damageNpIndividualSum | 對特定屬性敵人造成加乘傷害 |
| gainNp | 提升己方NP |
| gainStar | 獲取暴擊星 |
| addState (atk buff) | 提升攻擊力 X% |
| addState (def buff) | 提升防禦力 X% |
| addState (np gain up) | 提升NP獲取量 |
| instantDeath | 即死效果 |

### 6-2 快取策略

```
1. 首次抽到某英靈時，向 API 請求完整資料
2. 將解析後的關鍵欄位儲存進 Redis（key: hgwt_servant_cache:{collectionNo}）
3. TTL: 7天（定期更新以追蹤遊戲更新）
4. 啟動時不預載，採用 lazy loading
```

---

## 七、Discord UI 設計（完整流程）

### 7-1 開始爬塔 → 選隊伍

```
?? 聖杯塔 — 第 1 層
????????????????????

選擇出征英靈（最多3位）：

[?? 阿爾托莉亞 Lv.80]  [?? 吉爾伽美什 Lv.70]  [?? 赫拉克勒斯 Lv.60]
[?? 梅林 Lv.90]          [??? 江流兒 Lv.65]

已選: 阿爾托莉亞 ? | 吉爾伽美什 ? | 赫拉克勒斯 ?

[?? 出發！]
```

### 7-2 遭遇 → 遭遇事件卡

```
?? 第 5 層

? 道路分岐！

這一層出現了選擇點：

[?? 戰鬥 — 骷髏軍隊 (×2)]
[?? 商店 — 旅行商人]
[?? 休息 — 魔力補給點]
```

### 7-3 進入戰鬥 → 回合展示

```
?? 第 5 層戰鬥 — 回合 1
????????????????????

?? 敵人陣容：
  ?? 骷髏軍士 HP: 340/340 ATK: 45
  ?? 骷髏法師 HP: 220/220 ATK: 60

?? 暴擊星: 0 顆

?? 你的英靈：
  ?? 阿爾托莉亞  HP: 1800/2000  NP: ██████???? 60%
  ?? 吉爾伽美什  HP: 1500/1700  NP: ████?????? 40%
  ?? 赫拉克勒斯  HP: 1200/1400  NP: ██???????? 20%

??????? 本回合手牌 ???????

[??阿·B]  [??阿·A]  [??赫·B]  [??吉·Q]  [??赫·B]

已選：(空) (空) (空)

[取消上一張] [確認出手 →]
```

（每張牌是一個按鈕，按下後加入「已選」區域）

### 7-4 選牌後 → 戰鬥結果動畫

```
?? 出手結果 — 回合 1
????????????????????

? 阿爾托莉亞 [?? BUSTER]
  → 骷髏軍士造成 420 傷害！(首張Buster全隊×1.5)

? 赫拉克勒斯 [?? BUSTER]
  → 骷髏軍士造成 680 傷害！(×2連擊加成) ?BRAVE!

? 赫拉克勒斯 [?? BUSTER]
  → 骷髏軍士造成 960 傷害！(×3連擊加成) ?BRAVE!

?? BUSTER CHAIN! ??
? Extra Attack — 赫拉克勒斯 → 骷髏法師 890 傷害！

?? 敵人回合 ??

?? 骷髏法師 → 阿爾托莉亞 受到 42 傷害
?? 骷髏軍士 已倒下，無法行動

?? 回合結束 ??

NP 充能：阿 +0% / 吉 +0% / 赫 +0%
暴擊星：0 顆

[繼續下一回合 →]
```

### 7-5 NP 釋放

```
?? 出手結果 — 回合 3
????????????????????

【寶具發動！】
阿爾托莉亞 NP 100% → 釋放

??? 約束勝利之劍 — Excalibur ???
「我命令汝，消散吧，光輝！」

→ 對所有敵人造成 [AOE] 3200 傷害！
  骷髏軍士 受到 3200 傷害 → ?? 死亡！
  骷髏法師 受到 3200 傷害 → ?? 死亡！

?? 所有敵人已被消滅！戰鬥勝利！
```

---

## 八、遊戲整體節奏（Roguelike 設計）

### 8-1 地圖結構（每5層一個大節）

```
層數   類型
1-4    普通遭遇（戰鬥/事件/商店隨機）
5      精英戰（強制）
6-9    普通遭遇
10     BOSS 戰（強制）
...以此類推...
50     中途 BOSS + 特殊獎勵
100    最終 BOSS（通關）
```

### 8-2 遭遇地圖（每層進入時選擇）

參考殺戮尖塔：每層玩家看到 **2~3 個選項** 自由選擇路線：

```
第 6 層路線選擇：
[ ?? 普通戰鬥 ] → [ ?? 商店 ]
[ ?? 休息 ] → [ ?? 精英戰 → 稀有遺物]
```

### 8-3 遺物系統（Run 內永久加成）

| 遺物名稱 | 效果 | 取得方式 |
|---------|------|---------|
| 聖杯碎片 | 所有英靈 HP+20% | BOSS掉落 |
| 魔力迴路 | Arts 卡 NP獲得+50% | 商店 |
| 令咒碎片 | 每5層恢復所有英靈 30% HP | 精英掉落 |
| 時鐘塔認証 | 首張Buster加成 × 1.3 | 稀有寶箱 |
| 阿瓦隆護鞘 | 隊伍HP歸零時保留全員 1 HP（每場1次） | 傳說寶箱 |
| 王之財寶鑰匙 | 每回合暴擊星+3 | 初期選擇 |

### 8-4 商店商品

| 商品 | 價格 | 效果 |
|------|------|------|
| 急救草藥 | 40金 | 全隊HP+30% |
| 魔力補給劑 | 60金 | 全隊NP+50% |
| 強化魔符 | 80金 | 本次Run內選定英靈ATK+15% |
| 暴擊之星 | 50金 | 下回合額外+5顆暴擊星 |
| 遺物 | 120金 | 隨機遺物 |
| 召喚券（稀有） | 200金 | 免費1抽 |

---

## 九、永久升級系統（Meta progression）

使用通關後獲得的**聖晶石**解鎖：

| 升級 | 效果 | 費用 |
|------|------|------|
| 隊伍容量+1（3→4）| 出征可帶4人 | 20聖晶石 |
| 初始金幣+50 | 起始多50金 | 5聖晶石 |
| 強化選牌（抽6選3）| 每回合抽6張選3 | 15聖晶石 |
| NP起始值+20% | 開局NP全員20% | 10聖晶石 |
| 遺物槽+1 | 最多攜帶5個遺物 | 20聖晶石 |

---

## 十、實作優先順序

### Phase 1（核心可玩）— 約2~3週
- [x] Redis 儲存（已完成）
- [x] 抽卡系統（已完成）
- [x] 每日獎勵（已完成）
- [ ] API 取得卡牌配置 + 寶具數據
- [ ] 5張牌選3張的UI
- [ ] Buster/Arts/Quick 計算
- [ ] 連擊加成
- [ ] 敵人行動邏輯
- [ ] 勝負判定

### Phase 2（完整roguelike）— 約1~2週
- [ ] 分岐路線選擇
- [ ] 商店系統
- [ ] 遺物系統
- [ ] 精英/BOSS特殊技能
- [ ] 暴擊星系統

### Phase 3（Meta/品質提升）— 約1週
- [ ] 永久升級
- [ ] 更好的視覺設計（進度條、動畫感）
- [ ] 排行榜
- [ ] 音效文字演出（大量emoji）

---

## 十一、需要確認的決策

1. **選牌UI流程**：5張各自一個按鈕，還是下拉選單（Select Menu）？
   - 建議：按鈕（視覺更直覺，且最多5個正好一排）

2. **寶具效果**：是否要完整實作「即死/Debuff」等複雜效果？
   - 建議：Phase 1 只實作「傷害型」，Phase 2 再加Buff/Debuff

3. **敵人職階**：是否要讓敵人有職階相剋？
   - 建議：有，增加策略深度

4. **英靈技能**：FGO還有3個主動技能，要加嗎？
   - 建議：Phase 2 再加，Phase 1 只做卡牌戰鬥

5. **舊版聖杯戰爭 (PvP)**：是否要保留？
   - 建議：可以獨立存在，兩個系統並行

---

## 十二、技術架構概覽

```
HolyGrailTowerService.cs
├── 玩家管理（Redis CRUD）
├── 抽卡系統（已完成）
├── 爬塔管理
│   ├── 路線生成
│   ├── 遭遇管理
│   └── 戰鬥管理
│       ├── DrawCards()           ← 隨機抽5張
│       ├── HandleCardSelect()    ← 玩家選牌
│       ├── ExecuteCards()        ← 計算傷害
│       ├── EnemyTurn()           ← 敵人行動
│       └── CheckBattleEnd()      ← 勝負判定
└── 按鈕路由 HandleButtonAsync()
    ├── hgwt_select_{userId}_{colNo}   ← 選英靈
    ├── hgwt_start_{userId}            ← 開始出征
    ├── hgwt_card_{chId}_{pos}         ← 選牌
    ├── hgwt_confirm_{chId}            ← 確認出手
    ├── hgwt_np_{chId}_{servIdx}       ← 釋放寶具
    ├── hgwt_shop_{chId}_{item}        ← 商店購買
    ├── hgwt_rest_{chId}               ← 休息
    └── hgwt_next_{chId}               ← 前往下一層

HolyGrailTowerVM.cs（資料結構）
HolyGrailWarService.cs（PvP系統，獨立保留）
```

---

## 結語

這份設計對比舊版的三按鈕戰鬥，新版的核心改進是：

1. **有深度**：每回合的手牌都不同，需要策略選擇
2. **有主題**：FGO 的 Buster/Arts/Quick 連擊機制完整還原
3. **有趣**：暴擊星、Chain Bonus、寶具互動豐富
4. **有成長感**：英靈抽卡培養、遺物收集、永久升級
5. **有故事感**：寶具有真實名稱和效果描述，從API取得

**請確認以下事項後再開始實作：**
- [ ] 整體方向是否符合預期？
- [ ] 選牌UI設計（按鈕方式）是否可接受？
- [ ] Phase 1 範圍是否合理？
- [ ] 舊版聖杯戰爭(PvP)是否要保留？
