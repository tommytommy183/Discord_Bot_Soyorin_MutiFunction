# 🥒 長崎爽世 Discord Bot

> 月之森女子學園高中一年級學生  
> 樂隊 CRYCHIC 與 MyGO!!!!! 的貝斯手

一個以《BanG Dream! It's MyGO!!!!!》角色 **長崎爽世** 為主題打造的 Discord Bot，集結音樂播放、AI 聊天、多種遊戲、Pokemon 系統等豐富功能。部署於 [Railway](https://railway.app) 雲端平台。

---

## 📋 目錄

- [功能一覽](#-功能一覽)
- [指令說明](#-指令說明)
- [技術架構](#-技術架構)
- [專案結構](#-專案結構)
- [部署說明](#-部署說明)
- [注意事項](#-注意事項)

---

## ✨ 功能一覽

### 🎵 音樂播放
- YouTube、Bilibili 音訊串流
- 播放清單（Queue）管理
- 跳過、循環、自動推薦相關歌曲

### 🔊 語音功能
| 功能 | 說明 |
|------|------|
| Ear Rape 模式 | 音量突破極限的特效 |
| Text-To-Speech | 透過 ElevenLabs API 進行高品質語音合成 |
| RVC 語音轉換 | 本地 RVC 模型語音變聲（開發中）|

### 🤖 AI 聊天
透過 OpenRouter 串接大型語言模型，支援以下人格，由 @提及或關鍵字觸發：

| 人格 | 說明 |
|------|------|
| `soyo` | 主線人格 |
| `搜幽林` | 森林系探索人格 |
| `crychic` | 感性系人格 |
| `長期` | 長期穩定型 |
| `爽世` | 爽朗版爽世 |
| `爽食` | 美食愛好者 |
| `素食` | 素食主義者 |

每個頻道擁有獨立的對話記憶，不互相干擾。

### 🎮 遊戲系統（18+ 種）
| 遊戲 | 說明 |
|------|------|
| LOL 英雄技能查詢 | 查詢英雄技能資訊 |
| LOL 猜英雄技能 | 根據技能描述猜英雄 |
| Wordle（猜單字）| 英文猜字遊戲 |
| 踩地雷 | 5×5 經典模式 / 自訂大小（最大 20×20）|
| 魔術方塊 | Discord 內互動解謎 |
| 2048 | 經典數字合成遊戲 |
| 抽鬼牌 | 單人 / 多人模式 |
| 1A2B | 數字猜謎，支援 Modal 輸入 |
| 猜動漫角色 | 根據提示猜測角色（Jikan API）|
| 猜 Pokemon | 測試寶可夢知識 |

### 🐱 Pokemon 系統
- 每日抓取 Pokemon
- 1v1 / 2v2 對戰
- Boss Raid 戰鬥
- Pokemon 命名
- Pokemon 交換交易（含 UI 互動）
- 傳說、幻之 Pokemon 追蹤
- Redis 持久化玩家資料

### 💬 社群功能
| 功能 | 說明 |
|------|------|
| 關鍵字自動回覆 | 自訂觸發文字與回覆內容（Redis 儲存）|
| 送光系統 | 匿名傳送訊息給指定成員 |
| 投票系統 | 快速建立投票 |
| 殘酷二選一 | 面對人生最艱難的選擇 |
| 自動輪播狀態 | Bot 自動更換 Discord 狀態（11 種）|

---

## 📌 指令說明

絕大多數功能透過 Discord **Slash Command (`/`)** 操作，少部分舊版指令仍支援 `$$` 前綴。

---

## 🚀 技術架構

### 核心框架
| 技術 | 用途 |
|------|------|
| .NET 8.0 (C#) | 主要開發語言 |
| Discord.Net 3.20.1 | Discord Bot 框架，支援 Voice |
| StackExchange.Redis | 資料持久化（玩家資料、對話記憶、遊戲狀態）|
| SkiaSharp | 2D 圖形渲染 |
| NAudio | 音訊處理 |
| FFmpeg + yt-dlp | 影音下載與轉檔 |

### 外部 API

本專案共使用 **23 個 API Domain**，詳細列表如下：

#### 🤖 AI / LLM 服務
| API | Domain | 用途 |
|-----|--------|------|
| Google AI Studio (Gemini) | `generativelanguage.googleapis.com` | 主 LLM，支援 Gemini 2.0/2.5 系列模型 |
| OpenRouter | `openrouter.ai` | 多家大模型 LLM（DeepSeek、Qwen 等） |
| Groq Whisper | `api.groq.com` | 語音轉文字服務 |

#### 🎙️ TTS (文字轉語音)
| API | Domain | 用途 |
|-----|--------|------|
| ElevenLabs | `api.elevenlabs.io` | 高品質 TTS 服務 |
| Fish Audio | `api.fish.audio` | TTS 服務 |
| 本地 RVC | `localhost:8000` | 本地語音轉換和 TTS |

#### 🎮 遊戲相關
| API | Domain | 用途 |
|-----|--------|------|
| PokéAPI | `pokeapi.co` | 寶可夢資料查詢 |
| Valorant API | `valorant-api.com` | Valorant 角色與武器資料 |
| League of Legends (Data Dragon) | `ddragon.leagueoflegends.com` | 英雄聯盟英雄資料 |
| 2Pick App | `2pick.app` | 二選一遊戲服務 |

#### 📺 動漫 & 知識庫
| API | Domain | 用途 |
|-----|--------|------|
| Jikan (MyAnimeList) | `api.jikan.moe` | 動漫資料查詢 |
| Wikipedia (MediaWiki) | `*.wikipedia.org` | 多語言維基百科查詢（中/日/英） |

#### 🖼️ 圖片生成
| API | Domain | 用途 |
|-----|--------|------|
| LoremFlickr | `loremflickr.com` | 隨機圖片生成 |
| Pollinations AI | `image.pollinations.ai` | AI 圖片生成（Flux 模型） |

#### 🎲 趣味 API
| API | Domain | 用途 |
|-----|--------|------|
| Chuck Norris Jokes | `api.chucknorris.io` | 隨機笑話 |
| Cat Facts | `catfact.ninja` | 貓咪小知識 |
| Dog CEO | `dog.ceo` | 隨機狗狗圖片 |
| Hitokoto (一言) | `v1.hitokoto.cn` | 隨機句子/名言 |
| Random Duck | `random-d.uk` | 隨機鴨子圖片 |
| Random Fox | `randomfox.ca` | 隨機狐狸圖片 |
| Useless Facts | `uselessfacts.jsph.pl` | 無用小知識 |

#### 📹 其他
| API | Domain | 用途 |
|-----|--------|------|
| YouTube | `youtube.com` | 音樂播放（透過 yt-dlp） |
| Steam Store | `store.steampowered.com` | Steam 連結偵測 |

> 📄 完整 API 文檔請參考：[API_DOMAINS_SUMMARY.md](API_DOMAINS_SUMMARY.md)

### 部署
- **平台**：Railway.app（Docker 容器化）
- **語音加密**：libdave + libsodium
- **重啟策略**：失敗時自動重啟（最多 10 次）

---

## 📁 專案結構

```
newSoyo/
├── MusicBot2/
│   ├── Program.cs              # 主程式入口（~2000 行）
│   ├── Service/                # 18 個功能服務類別
│   │   ├── OpenRouterService.cs    # AI 對話
│   │   ├── PokeGameService.cs      # Pokemon 對戰系統
│   │   ├── ElevenLabsService.cs    # TTS
│   │   ├── MineGameService.cs      # 踩地雷
│   │   ├── RubiksCubeService.cs    # 魔術方塊
│   │   ├── Game2048Service.cs      # 2048
│   │   ├── Game1A2BService.cs      # 1A2B
│   │   ├── OldMaidService.cs       # 抽鬼牌
│   │   ├── PokeService.cs          # 猜 Pokemon
│   │   ├── JikanAnimeService.cs    # 猜動漫角色
│   │   ├── GetChampService.cs      # LOL 技能查詢
│   │   ├── TRPGService.cs          # 文字 RPG
│   │   ├── SetTextService.cs       # 關鍵字自動回覆
│   │   └── ...
│   ├── Models/                 # 12 個資料模型
│   ├── Helpers/                # 工具輔助類別
│   ├── SlashCommands/          # Discord Slash Command 處理器
│   ├── TxtFolder/              # 設定資料
│   └── appsettings.json        # API 金鑰與設定（勿上傳）
├── Dockerfile                  # 三階段 Docker 建構
├── railway.json                # Railway 部署設定
└── README.md
```

---

## 🐳 部署說明

### 環境變數 / appsettings.json
```json
{
  "Discord": { "Token": "..." },
  "ElevenLabs": { "ApiKey": "..." },
  "GoogleAIStudio": { "ApiKey": "...", "ApiKey2": "..." },
  "OpenRouter": { "ApiKey": "..." },
  "Redis": { "ConnectionString": "..." }
}
```

### Docker 建構流程
1. 編譯 libdave（Discord 語音加密）
2. 建構 .NET 8 應用程式
3. 組合 Runtime 映像（含 FFmpeg、Python 3、yt-dlp）

### 本地開發
```bash
cd MusicBot2
dotnet run
```

---

## ⚠️ 注意事項

- 本 Bot 為個人開發專案，部分功能仍在持續開發中
- 目前完全用愛發電，在 Railway 上運行
- 遇到 Bug 或有功能建議歡迎提出 Issue
