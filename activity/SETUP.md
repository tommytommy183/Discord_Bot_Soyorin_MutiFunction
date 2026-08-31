# Pokemon 爬塔 Discord Activity — 設定說明

## 1. Discord Developer Portal 設定

1. 前往 https://discord.com/developers/applications → 選你的 Application
2. 左側 **Activities** → 開啟 **Activities Enabled**
3. **URL Mappings** 加入：
   - Prefix: `/`  →  Target: `你的前端 URL`（e.g. `https://poketower.pages.dev`）
   - Prefix: `/api`  →  Target: `你的 C# bot URL:5000`（e.g. `https://你的railway域名:5000`）

## 2. 前端部署（Cloudflare Pages 免費）

```bash
cd activity
npm run build
# 把 dist/ 資料夾上傳到 Cloudflare Pages
```

或連結 GitHub repo，Cloudflare 自動 CI/CD。

設定 Build command: `npm run build`，Output directory: `dist`

**環境變數（Cloudflare Pages Settings）：**
```
VITE_DISCORD_CLIENT_ID=你的_Discord_Application_ID
```

## 3. C# Bot 環境變數

在 Railway（或 appsettings.json）加：
```
DISCORD_CLIENT_ID=你的Application_ID
DISCORD_CLIENT_SECRET=你的Application_Secret
DISCORD_REDIRECT_URI=https://discord.com/api/oauth2/authorize
ACTIVITY_API_PORT=5000
```

## 4. 遊戲流程

1. 玩家先在 Discord 頻道輸入 `/pokemon爬塔` → Bot 顯示隊伍選擇
2. 玩家選好隊伍 → 進入語音頻道
3. 點頻道旁邊的 **🚀 活動** → 選 **Pokemon 爬塔**
4. Activity 載入 → 自動連到已經開始的爬塔 run
5. 在遊戲視窗裡點按鈕進行遊戲（不需要再打 Discord 指令）

## 5. 本地開發

```bash
# 啟動 C# bot（含 API）
dotnet run --project MusicBot2/MusicBot2.csproj

# 啟動前端 dev server（另一個終端）
cd activity && npm run dev
# 前端在 http://localhost:3000
```

> 本地開發時 Discord SDK 無法正常 OAuth（需要 HTTPS + Discord proxy），
> 建議用 ngrok 或 cloudflare tunnel 做 HTTPS tunnel。
