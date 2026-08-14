# ?? 聖杯戰爭 RPG 系統

## ?? 專案簡介

基於 Fate/Grand Order 世界觀的 Discord RPG 遊戲系統，玩家可以：
- 註冊成為御主
- 召喚 FGO 從者
- 進行回合制戰鬥
- 培養從者（未來更新）
- 參加聖杯戰爭錦標賽（未來更新）

**資料來源**: Atlas Academy API（FGO 台服繁中）

---

## ? 核心功能

### ?? 已實現 (Phase 1)

#### 玩家系統
- ? 註冊成為御主（初始 100 魔力 + 3 令咒）
- ? 玩家資訊查詢
- ? 每日獎勵系統（24 小時冷卻）
- ? 資料持久化（JSON）

#### 召喚系統
- ? 卡池召喚（30 魔力/次）
- ? 稀有度機率：SSR 1%, SR 4%, R 20%, UC 30%, C 45%
- ? 從者資料整合（名稱、職階、寶具、圖片）
- ? 從者列表查看
- ? 出戰從者選擇

#### 戰鬥系統
- ? PvE（對戰 NPC）
- ? PvP（玩家對戰）
- ? 回合制戰鬥
- ? 職階相剋系統（14 種職階）
- ? 三種行動：攻擊、寶具、防禦
- ? NP 充能與釋放機制
- ? 爆擊系統
- ? 戰鬥日誌

#### 其他功能
- ? 從者治療（10 魔力）
- ? 戰績統計
- ? Discord 按鈕互動

---

## ?? 快速開始

### 指令列表

| 指令 | 說明 | 消耗 |
|------|------|------|
| `/hgw註冊` | 註冊成為御主 | 免費 |
| `/hgw資訊` | 查看個人資訊 | 免費 |
| `/hgw每日` | 領取每日獎勵 | 免費 |
| `/hgw召喚` | 召喚新從者 | ?? 30 |
| `/hgw從者` | 查看從者列表 | 免費 |
| `/hgw選擇 [ID]` | 選擇出戰從者 | 免費 |
| `/hgw治療 [ID]` | 治療從者 | ?? 10 |
| `/hgw戰鬥 [@對手]` | 開始戰鬥（留空為 PvE） | 免費 |

### 新手流程
```
1. /hgw註冊          → 成為御主
2. /hgw召喚          → 獲得第一位從者
3. /hgw戰鬥          → 挑戰 NPC
4. /hgw每日          → 每天領獎勵
5. /hgw從者          → 查看收藏
```

詳細請見 → [快速開始指南](QUICK_START.md)

---

## ?? 遊戲機制

### 職階相剋
```
Saber    → Lancer   (1.5x)
Archer   → Saber    (1.5x)
Lancer   → Archer   (1.5x)
Rider    → Caster   (1.5x)
Caster   → Assassin (1.5x)
Assassin → Rider    (1.5x)
Berserker → All     (1.5x 攻擊 / 0.67x 防禦)
```

### 戰鬥行動
- **?? 攻擊**: 造成傷害，NP +20
- **?? 寶具**: 需要 100 NP，造成 3 倍傷害
- **??? 防禦**: 恢復 10% HP，NP +10

### 資源系統
- **魔力來源**: 每日 +50 / 戰鬥勝利 +20
- **魔力消耗**: 召喚 -30 / 治療 -10

---

## ?? 技術架構

### 技術棧
- **框架**: .NET 8.0 / C# 12.0
- **Discord 庫**: Discord.NET
- **資料儲存**: JSON 文件
- **API**: Atlas Academy (FGO 資料)

### 檔案結構
```
MusicBot2/
├── Service/
│   ├── HolyGrailWarService.cs      # 主要遊戲邏輯
│   └── FgoGuessService.cs          # FGO 猜謎遊戲
├── Models/
│   ├── HolyGrailWarVM.cs           # 資料模型
│   └── FgoGuessVM.cs               # FGO 猜謎模型
├── SlashCommands/
│   └── SlashCommandHandler.cs      # Discord 指令處理
├── Data/
│   └── HolyGrailWar/               # 玩家資料（JSON）
└── Program.cs                       # 主程式入口
```

### 核心類別
- `HgwPlayer`: 玩家資料
- `HgwServant`: 從者實例
- `HgwBattle`: 戰鬥狀態
- `ClassAdvantage`: 職階相剋邏輯
- `HgwBattleResult`: 戰鬥結果

詳細請見 → [開發者指南](DEVELOPER_GUIDE.md)

---

## ?? 未來計劃

### Phase 2: 成長系統 (規劃中)
- [ ] 從者升級（經驗值）
- [ ] 技能系統（主動/被動）
- [ ] 靈基再臨（最大 4 階段）
- [ ] 寶具升級（Lv.1 → Lv.5）
- [ ] 聖杯轉臨（突破等級上限）

### Phase 3: 進階內容 (規劃中)
- [ ] 聖杯戰爭錦標賽（7 人淘汰賽）
- [ ] 劇情關卡（PvE 挑戰）
- [ ] 組隊戰鬥（3v3）
- [ ] 概念禮裝系統
- [ ] 公會系統
- [ ] 全球排行榜

### Phase 4: 社交功能 (規劃中)
- [ ] 好友系統
- [ ] 支援從者借用
- [ ] 交易市場
- [ ] 每日任務
- [ ] 成就系統

---

## ?? 截圖預覽

### 召喚畫面
```
? 召喚成功！
【玩家】 召喚了新的從者！

?? Artoria Pendragon
★★★★★
寶具：約束勝利之劍

HP: 2000 | ATK: 200 | DEF: 50
剩餘魔力：70 | 從者總數：1
```

### 戰鬥畫面
```
?? 回合 1

?? Artoria Pendragon 攻擊 Gilgamesh，造成 250 傷害 (職階相剋！)
NP +20 (20/100)

?? Artoria Pendragon     VS     ?? Gilgamesh
HP: 2000/2000                    HP: 1750/2000
NP: 20/100                       NP: 0/100

輪到 玩家1 行動
[?? 攻擊] [??? 防禦] [?? 寶具 (20/100)]
```

---

## ?? 文件導覽

- ?? [使用說明](HOLY_GRAIL_WAR_GUIDE.md) - 完整遊戲指南
- ?? [快速開始](QUICK_START.md) - 5 分鐘上手
- ?? [開發者指南](DEVELOPER_GUIDE.md) - 技術文件

---

## ?? 貢獻

歡迎提出建議或回報 Bug！

---

## ?? 授權

此專案基於 MIT License

---

## ?? 致謝

- **Atlas Academy**: 提供完整的 FGO API
- **TYPE-MOON**: Fate 系列版權所有
- **Discord.NET**: 優秀的 Discord 機器人框架

---

## ?? 聯繫方式

如有問題請聯繫 Bot 管理員

---

<div align="center">

**?? 願聖杯的榮耀與你同在 ?**

Made with ?? for FGO fans

</div>
