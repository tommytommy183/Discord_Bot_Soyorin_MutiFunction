# CLAUDE.md — newSoyo Discord Bot

## 專案概述

以《BanG Dream! It's MyGO!!!!!》角色**長崎爽世**為主題的 Discord Bot，C# / .NET 8，部署於 Railway（Docker）。

---

## 開發環境

```bash
# 本地執行
dotnet run --project MusicBot2/MusicBot2.csproj

# Docker 建構
docker build -t musicbot2:latest .
```

**環境變數 / appsettings.json**（不要 commit）：
- `Discord:Token`
- `ElevenLabs:ApiKey`
- `GoogleAIStudio:ApiKey` / `ApiKey2`
- `OpenRouter:ApiKey`
- `Redis:ConnectionString`

---

## 專案架構

```
MusicBot2/
├── Program.cs               # 主程式：DI 設置、Discord 事件、按鈕 interaction 路由（~2000 行）
├── SlashCommands/
│   └── SlashCommandHandler.cs  # 所有 slash command 定義與處理
├── Service/                 # 各功能獨立 service（Singleton）
│   ├── PokeGameService.cs   # Pokemon 系統核心（2443 行）
│   ├── OpenRouterService.cs # LLM API（對戰判斷、AI 聊天）
│   ├── ElevenLabsService.cs # TTS
│   ├── TRPGService.cs       # 文字 RPG
│   ├── SetTextService.cs    # 關鍵字自動回覆
│   └── ...（其餘遊戲服務）
├── Models/
│   └── PokeVM.cs            # Pokemon 相關資料模型
├── Helpers/
│   └── CommonHelper.cs      # 錯誤訊息建立、通用工具
└── appsettings.json
```

---

## 核心慣例

### 1. Service 方法回傳格式

幾乎所有 service 方法都回傳：
```csharp
Task<(Embed embed, ComponentBuilder component)>
```
部分需要傳遞狀態的方法加第三個值，例如：
```csharp
Task<(Embed embed, ComponentBuilder component, int targetPokemonIndex)>
```

### 2. 錯誤處理

統一使用 `CommonHelper.BuildErrorResponse()`（`Helpers/CommonHelper.cs`）：
```csharp
catch (Exception ex)
{
    return (CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}").Item2, new ComponentBuilder());
}
```
錯誤 embed 固定標題為「❌ 诶诶 叫豬頭馬又看一下啦」。

### 3. Redis + 記憶體雙重儲存

所有需要持久化的 service 都有 Redis 主儲存 + 記憶體 fallback：
```csharp
private readonly IDatabase _redisDb;
private readonly bool _useRedis;
private static readonly Dictionary<ulong, T> _memoryFallback = new();

// 讀取
if (_useRedis) { /* Redis */ } else { /* Memory */ }
```

Redis 連線設定：`ConnectTimeout=10000`、`AbortOnConnectFail=false`、`ConnectRetry=3`。

### 4. Button Interaction 路由（Program.cs）

按 CustomId 前綴分派，格式固定：
```csharp
if (component.Data.CustomId.StartsWith("mine_"))      // 踩地雷: mine_{userId}_{x}_{y}
else if (component.Data.CustomId.StartsWith("cube_")) // 魔術方塊: cube_{action}_{clockwise}
else if (component.Data.CustomId.StartsWith("2048_")) // 2048: 2048_{direction}
else if (component.Data.CustomId.StartsWith("poke_exchange_")) // Pokemon 交換流程
// ...
```

**新增按鈕 interaction** 時：
1. 在 Program.cs 加對應的 `else if` 分支
2. CustomId 命名遵循 `{功能前綴}_{exchangeKey 或參數}`

### 5. DI 注冊（Program.cs）

所有 service 都是 `Singleton`，在 `ConfigureServices()` 中注冊：
```csharp
_services = new ServiceCollection()
    .AddSingleton<PokeGameService>(sp => new PokeGameService(redisConn, aiService, _client))
    .AddSingleton<MineGameService>()
    // ...
    .BuildServiceProvider();
```
需要 API key 或其他依賴的 service 用 factory lambda。

---

## Pokemon 系統重點

### 交換流程（3 階段）

`PokeGameService.HandleExchangeResponseAsync()` 負責全部邏輯：

| 階段 | 觸發條件 | 說明 |
|------|----------|------|
| Stage 1 | B（TargetId）且 `!TargetSelected` 且 `!targetPokemonIndex.HasValue` | B 接受/拒絕請求 |
| Stage 2 | B（TargetId）且 `!TargetSelected` 且 `targetPokemonIndex.HasValue` | B 選擇要換的 Pokemon，`TargetSelected` 設為 true |
| Stage 3 | A（RequesterId）且 `TargetSelected` | A 確認或取消交換 |

**注意**：Stage 1 必須有 `!targetPokemonIndex.HasValue` 條件，否則 Stage 2 永遠不會執行。

### Button ID 格式（交換系統）

```
poke_exchange_accept_{exchangeKey}   // B 接受
poke_exchange_reject_{exchangeKey}   // B 拒絕
poke_exchange_select_{exchangeKey}_{i}  // B 選擇第 i 隻
poke_exchange_confirm_{exchangeKey}  // A 確認
poke_exchange_cancel_{exchangeKey}   // A 取消
```

`exchangeKey` 格式：`{requesterId}_{targetId}`

### 進化系統

- 勝利 +2 點，失敗 +1 點，3 點觸發進化
- `PokeGamePokemon.CanEvolve`、`NextEvolutionId`、`EvolutionPoints`

### AI 對戰判斷

使用 `OpenRouterService.GenerateSimpleTextAsync()` 模擬對戰，解析最後一行「勝者：[玩家名稱]」決定勝負；若 AI 未明確指定，fallback 為總數值比較。

---

## Redis Key 命名

```
pokegame:player:{userId}      # 玩家資料（JSON）
pokegame:matchmaking          # 1v1 配對池（Hash）
pokegame:matchmaking:2v2      # 2v2 配對池（Hash）
pokegame:teamfight:boss       # 團戰 Boss（JSON）
pokegame:legendary:ids        # 傳說/神話 Pokemon ID 清單（JSON）
```

---

## 新增功能 Checklist

1. **新 Service**：實作 `Task<(Embed, ComponentBuilder)>` 介面，在 `Program.cs ConfigureServices()` 注冊。
2. **新 Slash Command**：在 `SlashCommandHandler.cs` 加 `[SlashCommand]` attribute 方法。
3. **新 Button**：在 `Program.cs InteractionCreated()` 加 `else if (CustomId.StartsWith("前綴_"))` 分支。
4. **錯誤處理**：catch block 統一用 `CommonHelper.BuildErrorResponse()`。
5. **持久化**：依照 Redis + 記憶體 fallback 雙重儲存模式。
