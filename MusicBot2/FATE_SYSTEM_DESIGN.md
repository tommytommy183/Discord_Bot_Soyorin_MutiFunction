# ?? 聖杯塔 Roguelike 系統設計文檔

## ?? Fate 系列設定參考

### 核心概念
- **聖杯戰爭**：7 位御主召喚 7 位英靈（Servant）爭奪聖杯
- **職階系統**：Saber、Archer、Lancer、Rider、Caster、Assassin、Berserker
- **令咒（Command Spell）**：御主控制從者的絕對命令（3 劃）
- **寶具（Noble Phantasm）**：從者的必殺技
- **魔力系統**：御主提供魔力維持從者存在
- **職階相性**：Saber > Lancer > Archer > Saber（三騎士循環）

### 從者稀有度
- ????? SSR：傳說英雄（1% 機率）
- ???? SR：知名英雄（3% 機率）
- ??? R：一般英雄（12% 機率）
- ?? UC：弱小英靈（24% 機率）
- ? C：雜兵級（60% 機率）

---

## ?? 遊戲系統設計

### 一、抽卡系統（Gacha）

#### 貨幣系統
- ?? **召喚券**：基礎貨幣，用於抽卡
  - 初始 10 張
  - 每日獎勵 +3 張
  - 爬塔獎勵（每 5 層 +2 張）

- ?? **聖晶石**：高級貨幣（未來可用）
  - 付費貨幣（暫不開放）
  - 保底機制用（第 10 抽必出 SR 以上）

#### 抽卡機制
- **單抽**：消耗 1 張召喚券
- **十連抽**（未來）：消耗 10 張召喚券，第 10 抽必出 SR 以上
- **重複從者**：寶具等級 +1（最高 Lv.5）
- **保底**：
  - 30 抽內必出 SR
  - 90 抽內必出 SSR

#### 從者屬性
```csharp
public class TowerServant
{
    int CollectionNo;        // 從者編號
    string Name;             // 名稱
    string ClassName;        // 職階
    int Rarity;              // 稀有度 1-5
    int Level;               // 等級 1-100
    int Experience;          // 經驗值
    int NpLevel;             // 寶具等級 1-5
    int SkillLevel;          // 技能等級 1-10

    // 戰鬥屬性（基於稀有度和等級計算）
    int MaxHp = (600 + Rarity * 200) * Level;
    int Attack = (50 + Rarity * 20) * Level;
    int Defense = (30 + Rarity * 10) * Level;
}
```

---

### 二、爬塔系統（Roguelike）

#### 塔層結構（100 層）
- **第 1-9 層**：新手村
  - 60% 普通戰鬥
  - 15% 商店
  - 15% 寶箱
  - 10% 休息點

- **第 5、15、25... 層**：精英戰
  - 強化敵人
  - 獎勵 +2 召喚券

- **第 10、20、30... 層**：BOSS 戰
  - 超強敵人
  - 獎勵 +3 召喚券 + 稀有遺物

- **第 50 層**：中層 BOSS
  - 解鎖新機制

- **第 100 層**：最終 BOSS
  - 通關獎勵：大量聖晶石 + 限定稱號

#### 遭遇類型

##### 1. ?? 戰鬥（60%）
- **敵人種類**：
  - 普通怪：骷髏、野獸、魔像
  - 精英怪：騎士、法師、刺客
  - BOSS：龍、惡魔、墮落英靈

- **戰鬥機制**：
  - 回合制
  - 每回合選擇一位從者行動
  - 3 種行動：普通攻擊、技能、寶具
  - NP（寶具值）系統：
    - 普通攻擊 +20 NP
    - 受到攻擊 +10 NP
    - 達到 100 可使用寶具

##### 2. ?? 商店（15%）
- **商品**：
  - 治療藥水（30 金）：恢復隊伍 30% HP
  - 全體治療（80 金）：恢復隊伍 100% HP
  - 攻擊遺物（120 金）：攻擊力 +10%
  - 防禦遺物（120 金）：防禦力 +10%
  - 移除卡牌（50 金）：移除基礎攻擊卡

##### 3. ? 寶箱（15%）
- **獎勵**：
  - 50-200 金幣
  - 隨機遺物
  - 召喚券（稀有）

##### 4. ?? 休息點（10%）
- **選項**：
  - 休息：恢復 50% HP
  - 鍛鍊：隨機從者經驗 +100
  - 冥想：所有從者 NP +30

---

### 三、戰鬥系統

#### 回合流程
1. **玩家回合**
   - 選擇一位從者
   - 選擇行動：
     - ??? 普通攻擊
     - ?? 技能（如果有）
     - ?? 寶具（NP ? 100）
   - 執行傷害計算

2. **敵人回合**
   - 所有存活敵人依序攻擊
   - 隨機目標或最低 HP 目標

3. **勝利條件**
   - 所有敵人 HP = 0 → 獲得獎勵，進入下一層
   - 隊伍全滅 → Run 結束，返回主選單

#### 傷害計算
```csharp
// 基礎傷害
int baseDamage = attacker.Attack;

// 職階相性（三騎士循環）
float classBonus = ClassAdvantage.GetMultiplier(attackerClass, defenderClass);
// Saber vs Lancer: 1.5x
// Lancer vs Archer: 1.5x
// Archer vs Saber: 1.5x
// 其他: 1.0x

// 隨機波動 (0.9 ~ 1.1)
float random = Random(0.9f, 1.1f);

// 防禦減傷
int defense = defender.Defense;

// 最終傷害
int damage = (int)((baseDamage * classBonus * random) - defense);
damage = Math.Max(1, damage); // 最少 1 點傷害
```

#### 寶具系統
- **NP 累積**：
  - 普通攻擊 +20 NP
  - 受擊 +10 NP
  - 擊殺敵人 +30 NP

- **寶具效果**：
  - 單體寶具：對單一敵人造成 300% 傷害
  - 範圍寶具：對所有敵人造成 150% 傷害
  - 輔助寶具：隊伍攻擊 +50% 持續 2 回合

---

### 四、遺物系統（Relics）

#### 遺物稀有度
- **普通**（Common）：+5% 屬性
- **稀有**（Uncommon）：+10% 屬性
- **稀有**（Rare）：+15% 屬性 / 特殊效果
- **史詩**（Epic）：強力特殊效果
- **傳說**（Legendary）：改變玩法的效果

#### 遺物範例
| 遺物名稱 | 稀有度 | 效果 |
|---------|--------|------|
| 騎士徽章 | Common | 攻擊 +5% |
| 魔力迴路 | Uncommon | NP 獲得量 +10% |
| 令咒碎片 | Rare | 每層開始時恢復 20% HP |
| 聖杯碎片 | Epic | 所有從者寶具等級 +1 |
| 時鐘塔許可 | Epic | 可以重置一次商店 |
| 阿瓦隆護鞘 | Legendary | 隊伍受到致命傷時保留 1 HP（每層 1 次）|
| 王之財寶鑰匙 | Legendary | 戰鬥開始時獲得 3 種隨機遺物效果 |

---

### 五、永久升級系統

#### 升級項目（使用聖晶石）
| 升級項目 | 效果 | 最大等級 | 消耗 |
|---------|------|---------|------|
| 隊伍人數 | 最大隊伍 +1 | Lv.3（5人隊） | 10/20/30 聖晶石 |
| 起始金幣 | 起始金幣 +50 | Lv.5 | 5/10/15/20/25 聖晶石 |
| 起始 HP | 起始 HP +10% | Lv.10 | 3/6/9...30 聖晶石 |
| NP 獲得 | NP 獲得量 +5% | Lv.10 | 5/10/15...50 聖晶石 |
| 商店折扣 | 商店價格 -5% | Lv.5 | 10/20/30/40/50 聖晶石 |

---

### 六、資料持久化

#### 玩家資料（永久）
```json
{
  "userId": 123456789,
  "userName": "Master",
  "summonTickets": 10,
  "saintQuartz": 0,
  "ownedServants": [
    {
      "collectionNo": 1,
      "name": "阿爾托莉亞",
      "className": "Saber",
      "rarity": 5,
      "level": 1,
      "npLevel": 1,
      "experience": 0
    }
  ],
  "highestFloor": 0,
  "totalRuns": 0,
  "totalKills": 0,
  "permanentUpgrades": {
    "max_team_size": 0,
    "starting_gold": 0
  }
}
```

#### Run 資料（臨時，頻道內）
```json
{
  "channelId": 987654321,
  "playerId": 123456789,
  "currentFloor": 1,
  "team": [從者實例],
  "currentHp": 5000,
  "maxHp": 5000,
  "gold": 100,
  "relics": [遺物],
  "currentEncounter": {戰鬥狀態}
}
```

---

## ??? 錯誤處理與日誌

### 必須記錄的日誌
```csharp
Console.WriteLine($"[HolyGrailTower] 玩家 {userId} 開始爬塔");
Console.WriteLine($"[HolyGrailTower] 第 {floor} 層戰鬥開始");
Console.WriteLine($"[HolyGrailTower] {servantName} 使用寶具造成 {damage} 傷害");
Console.WriteLine($"[HolyGrailTower] 玩家 {userId} 於第 {floor} 層失敗");
Console.WriteLine($"[HolyGrailTower] 錯誤: {ex.Message}");
```

### 錯誤處理
- **戰鬥中途離開**：自動儲存 Run 狀態
- **重複開始**：檢查頻道是否已有 Run
- **無效操作**：返回錯誤訊息，不崩潰
- **API 失敗**：使用預設資料，記錄日誌

---

## ?? 指令列表

### 基礎指令
- `/聖杯塔註冊` - 註冊成為御主
- `/聖杯塔資訊` - 查看資訊
- `/聖杯塔召喚` - 抽卡（消耗 1 召喚券）
- `/聖杯塔圖鑑` - 查看從者圖鑑
- `/聖杯塔每日` - 領取每日獎勵（+3 召喚券）

### 爬塔指令
- `/開始爬塔` - 開始新的爬塔挑戰
- `/取消爬塔` - 放棄當前爬塔（只有本人可用）
- `/爬塔狀態` - 重新顯示當前狀態

### 按鈕互動
- 從者選擇按鈕：`hgw_tower_select_{userId}_{collectionNo}`
- 開始挑戰按鈕：`hgw_tower_start_{userId}`
- 戰鬥按鈕：`hgw_tower_attack_{channelId}_{servantIndex}`
- 寶具按鈕：`hgw_tower_np_{channelId}_{servantIndex}`
- 商店按鈕：`hgw_tower_shop_{channelId}_{itemId}`
- 休息按鈕：`hgw_tower_rest_{channelId}`

---

## ?? 平衡性設定

### 敵人強度曲線
```csharp
int floor = currentFloor;

// 普通怪
int normalHp = 100 + floor * 20;
int normalAtk = 20 + floor * 5;

// 精英怪（每 5 層）
int eliteHp = 300 + floor * 50;
int eliteAtk = 40 + floor * 8;

// BOSS（每 10 層）
int bossHp = 500 + floor * 100;
int bossAtk = 60 + floor * 12;
```

### 金幣經濟
- 起始金幣：100
- 戰鬥獎勵：20-50 金幣
- 寶箱獎勵：50-200 金幣
- 商店價格：30-150 金幣

### 經驗值系統（未來）
- 戰鬥勝利：經驗 = 層數 × 10
- 升級需求：`(level ^ 2) * 100`
- 滿級：100

---

## ?? 實作優先順序

### Phase 1（核心功能）
- [x] 玩家註冊系統
- [x] 抽卡系統
- [x] 從者圖鑑
- [x] 每日獎勵
- [ ] 爬塔啟動與隊伍選擇
- [ ] 基礎戰鬥系統
- [ ] 普通遭遇

### Phase 2（完整遊玩）
- [ ] 商店系統
- [ ] 寶箱系統
- [ ] 休息點系統
- [ ] 遺物系統
- [ ] 精英戰 / BOSS 戰
- [ ] 戰鬥日誌

### Phase 3（進階功能）
- [ ] 永久升級
- [ ] 排行榜
- [ ] 成就系統
- [ ] 每日挑戰

---

## ?? 已知問題與修復

### 問題 1：戰鬥無法取消
**原因**：沒有實作 `/取消爬塔` 指令  
**修復**：加入取消邏輯，清除 `_runs` 字典中的資料

### 問題 2：戰鬥 Bug 導致 Embed 錯誤
**原因**：空值處理不足、屬性計算錯誤  
**修復**：
- 所有 Embed 建立前檢查空值
- 加入 `try-catch` 捕捉錯誤
- 記錄完整錯誤日誌

### 問題 3：抽卡無限制
**原因**：沒有檢查召喚券數量  
**修復**：在 `SummonServantAsync` 開頭檢查票券

### 問題 4：重複開始戰鬥
**原因**：沒有檢查頻道是否已有 Run  
**修復**：在 `StartTowerRunAsync` 開頭檢查 `_runs.ContainsKey(channelId)`

---

## ?? 測試清單

- [ ] 註冊新玩家
- [ ] 重複註冊（應顯示已註冊）
- [ ] 抽卡（扣除召喚券）
- [ ] 抽卡不足（應顯示錯誤）
- [ ] 重複從者（寶具等級提升）
- [ ] 每日獎勵（24 小時內只能領一次）
- [ ] 開始爬塔（選擇從者）
- [ ] 戰鬥流程（攻擊 → 敵人回合 → 勝利）
- [ ] 戰鬥失敗（隊伍全滅）
- [ ] 取消爬塔
- [ ] 重複開始（應顯示錯誤）
- [ ] 商店購買
- [ ] 寶箱開啟
- [ ] 休息點選擇

---

## ?? UI 設計範例

### 抽卡結果 Embed
```
?? 召喚結果
【Master】召喚了：

?? 阿爾托莉亞?潘德拉剛
★★★★★
? NEW!

剩餘召喚券：9
圖鑑：1 位從者
```

### 戰鬥狀態 Embed
```
?? 第 5 層 - 戰鬥！

?? 骷髏戰士
HP: 200/200 | ATK: 45

?? 狂戰士
HP: 180/180 | ATK: 50

?????????????????
你的隊伍：
?? 阿爾托莉亞 HP: 1800/2000 [NP: 60/100]
?? 吉爾伽美什 HP: 1500/1700 [NP: 80/100]
?? 庫夫林 HP: 1200/1400 [NP: 40/100]
```

---

這份文檔涵蓋了完整的系統設計，請先審查確認方向正確後再開始實作！??
