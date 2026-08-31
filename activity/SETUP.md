# Pokemon 爬塔 Web App — 設定說明

## 1. Discord Developer Portal 設定

1. 前往 https://discord.com/developers/applications → 選你的 Application
2. 左側 **OAuth2** → **Redirects** → 加入：
   ```
   https://poketower-activity.pages.dev
   ```
3. **不需要**開 Activities，這是普通 OAuth2 Web App

---

## 2. 前端部署（Cloudflare Pages）

```bash
cd activity
npx wrangler pages deploy dist --project-name=poketower-activity
```

**Cloudflare Pages 環境變數（Settings → Environment variables）：**
```
VITE_DISCORD_CLIENT_ID = 你的_Application_ID
VITE_REDIRECT_URI      = https://poketower-activity.pages.dev
```

> 加完環境變數後要 redeploy 一次才會生效：重新 push 或手動在 CF Pages trigger deploy。

---

## 3. Bot（Northflank）環境變數

```
DISCORD_CLIENT_ID     = 你的_Application_ID
DISCORD_CLIENT_SECRET = 你的_Application_Secret
DISCORD_REDIRECT_URI  = https://poketower-activity.pages.dev
ACTIVITY_API_PORT     = 5000
```

> `DISCORD_REDIRECT_URI` 必須和 Discord Portal 填的完全相同。

---

## 4. 遊戲流程

1. 玩家在 Discord 頻道輸入 `/pokemon爬塔`
2. Bot 顯示隊伍選擇按鈕
3. 玩家選完隊伍 → Bot 送出 Embed，附上「▶️ 開始遊戲」按鈕
4. 點連結 → 瀏覽器打開 `https://poketower-activity.pages.dev?channel=CHANNEL_ID`
5. 頁面顯示「用 Discord 登入」按鈕 → 點下去 → Discord OAuth 彈窗
6. 授權 → 回到遊戲頁，可以開始遊戲 🎮

---

## 5. 本地開發

```bash
# 啟動 C# bot（含 API on port 5000）
dotnet run --project MusicBot2/MusicBot2.csproj

# 啟動前端 dev server（另開終端）
cd activity && npm run dev
# http://localhost:3000?channel=你的測試頻道ID

# 本地 OAuth 測試需要 HTTPS，用 ngrok：
# ngrok http 3000
# 把 ngrok URL 加到 Discord Portal OAuth2 Redirects
```
