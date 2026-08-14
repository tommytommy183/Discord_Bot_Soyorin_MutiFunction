# GroqWhisperService 運作分析與優化

## ?? 功能概述

`GroqWhisperService` 是一個**語音轉文字 (STT)** 服務，使用 Groq 的 Whisper API 來辨識 Discord 語音頻道中的語音內容。

---

## ?? 完整運作流程

### 1. **啟動監聽** (`StartListeningAsync`)

```
用戶調用 StartListeningAsync()
    ↓
Bot 連接到語音頻道 (selfDeaf: false, selfMute: false)
    ↓
創建 VoiceChannelListener 實例
    ↓
訂閱 StreamCreated 事件 (等待用戶開始說話)
    ↓
回傳 IAudioClient
```

**重要參數：**
- `selfDeaf: false` - Bot 可以聽到其他人說話
- `selfMute: false` - Bot 沒有靜音（但不發聲）

---

### 2. **語音接收流程** (`OnStreamCreated`)

當有人在語音頻道開始說話時：

```
Discord 觸發 StreamCreated 事件
    ↓
為該用戶創建:
  - OpusDecoder (48kHz, 2聲道)
  - AudioStreamBuffer (音訊緩衝區)
    ↓
開始循環讀取音訊幀 (RTPFrame)
```

#### **音訊處理循環**

```csharp
while (沒有取消) {
    // 等待下一個音訊幀，最多等 800ms
    等待 ReadFrameAsync() 或 Delay(800ms)

    if (800ms 超時 - 沒有新音訊) {
        // 認為是一句話結束
        if (緩衝區音訊 >= 400ms) {
            → 送去辨識 (ProcessAudioBufferAsync)
        } else {
            → 忽略（太短）
        }
        清空緩衝區
        continue
    }

    if (收到新音訊幀) {
        Opus 解碼 → PCM
        寫入緩衝區

        // 保護機制
        if (緩衝區 >= 8秒) {
            → 強制送去辨識
            清空緩衝區
        }
    }
}
```

**關鍵時間參數：**
- `800ms` - 靜音超時（判斷一句話結束）
- `400ms` - 最短語音長度（太短會被忽略）
- `8秒` - 最長單次語音（防止無限累積）
- `0.25秒` (48000 bytes) - 最終檢查的最短長度

---

### 3. **語音辨識流程** (`ProcessAudioBufferAsync`)

```
接收到音訊緩衝區
    ↓
檢查長度 (< 48000 bytes = 0.25秒 → 忽略)
    ↓
調用 TranscribeAudioAsync()
    ├─ 包裝 PCM 為 WAV 格式
    ├─ 調用 Groq Whisper API
    │   - Model: whisper-large-v3
    │   - Language: zh (中文)
    └─ 解析 JSON 回應
    ↓
檢查辨識結果是否為空
    ↓
檢查是否包含觸發詞
    - "soyo" (不分大小寫)
    - "爽世"
    - "搜幽"
    ↓
如果有觸發詞 → 調用 onSpeechRecognized 回調
```

---

## ?? 原有問題診斷

### **問題 1：觸發條件過於嚴格**

```csharp
// ? 原代碼
if (text.ToLower().Contains("soyo") ||
    text.Contains("爽世") ||
    text.Contains("搜幽"))
{
    await _onSpeechRecognized(text, user);
}
// 沒有任何 log 輸出！
```

**問題：**
- ? 只有說「soyo」、「爽世」、「搜幽」才會觸發
- ? 說其他內容完全不會記錄
- ? 無法知道辨識是否成功

### **問題 2：缺少 Logging**

```csharp
// ? 原代碼
if (audioData == null || audioData.Length < 48000) {
    return;  // 靜默失敗，沒有任何提示
}

if (string.IsNullOrWhiteSpace(text)) {
    return;  // 靜默失敗，沒有任何提示
}
```

**問題：**
- ? 不知道音訊是否太短被忽略
- ? 不知道辨識是否失敗
- ? 不知道是否檢測到觸發詞

### **問題 3：錯誤處理不完整**

```csharp
// ? 原代碼
catch (Exception ex)
{
    // 空的！完全沒有 log
}
```

**問題：**
- ? 異常被吞掉，無法診斷問題
- ? 不知道是網路問題、API 問題還是解碼問題

---

## ? 優化方案

### **優化 1：添加完整 Logging**

```csharp
// ? 優化後
private async Task ProcessAudioBufferAsync(ulong userId, AudioStreamBuffer buffer)
{
    var audioData = buffer.GetAudioData();

    // 音訊太短
    if (audioData == null || audioData.Length < 48000) {
        Console.WriteLine($"[GroqWhisper] 音訊太短 ({audioData?.Length ?? 0} bytes < 48000)，略過");
        return;
    }

    Console.WriteLine($"[GroqWhisper] 開始辨識音訊：{audioData.Length} bytes ({buffer.Duration.TotalSeconds:F2}秒)");

    var text = await _service.TranscribeAudioAsync(audioData);

    if (string.IsNullOrWhiteSpace(text)) {
        Console.WriteLine($"[GroqWhisper] 辨識結果為空");
        return;
    }

    Console.WriteLine($"[GroqWhisper] 辨識成功：{text}");

    bool containsTrigger = text.ToLower().Contains("soyo") ||
                          text.Contains("爽世") ||
                          text.Contains("搜幽");

    if (containsTrigger) {
        Console.WriteLine($"[GroqWhisper] 檢測到觸發詞，準備回應");
        var guild = (_voiceChannel as SocketVoiceChannel)?.Guild;
        var user = guild?.GetUser(userId);

        if (user != null) {
            await _onSpeechRecognized(text, user);
        } else {
            Console.WriteLine($"[GroqWhisper] 找不到用戶 ID: {userId}");
        }
    } else {
        Console.WriteLine($"[GroqWhisper] 未檢測到觸發詞 (soyo/爽世/搜幽)，不觸發回應");
    }
}
```

### **優化 2：改善錯誤處理**

```csharp
// ? 優化後
catch (Exception ex)
{
    Console.WriteLine($"[GroqWhisper ProcessAudio Error] {ex.Message}\n{ex.StackTrace}");
}
```

### **優化 3：添加更多追蹤點**

```csharp
// 啟動監聽
public async Task StartAsync()
{
    Console.WriteLine($"[GroqWhisper] 開始監聽頻道 {_voiceChannel.Name}");
    _audioClient.StreamCreated += OnStreamCreated;
}

// 用戶開始說話
private Task OnStreamCreated(ulong userId, AudioInStream stream)
{
    Console.WriteLine($"[GroqWhisper] 用戶 {userId} 開始說話");
    // ...
}

// 用戶停止說話
if (completed != pendingRead)
{
    if (buffer.Duration >= TimeSpan.FromMilliseconds(400))
    {
        Console.WriteLine($"[GroqWhisper] 用戶 {userId} 停止說話 ({buffer.Duration.TotalSeconds:F2}秒)");
        await ProcessAudioBufferAsync(userId, buffer);
    }
    else
    {
        Console.WriteLine($"[GroqWhisper] 用戶 {userId} 音訊片段太短 ({buffer.Duration.TotalMilliseconds:F0}ms < 400ms)，略過");
    }
}
```

---

## ?? 現在的 Log 輸出範例

### **情境 1：成功辨識並觸發**

```
[GroqWhisper] 開始監聽頻道 General
[GroqWhisper] 用戶 123456789 開始說話
[GroqWhisper] 用戶 123456789 停止說話 (2.35秒)
[GroqWhisper] 開始辨識音訊：450000 bytes (2.35秒)
[GroqWhisper] 辨識成功：嗨 soyo 你好嗎
[GroqWhisper] 檢測到觸發詞，準備回應
```

### **情境 2：辨識成功但無觸發詞**

```
[GroqWhisper] 用戶 123456789 開始說話
[GroqWhisper] 用戶 123456789 停止說話 (1.50秒)
[GroqWhisper] 開始辨識音訊：288000 bytes (1.50秒)
[GroqWhisper] 辨識成功：今天天氣真好
[GroqWhisper] 未檢測到觸發詞 (soyo/爽世/搜幽)，不觸發回應
```

### **情境 3：音訊太短**

```
[GroqWhisper] 用戶 123456789 開始說話
[GroqWhisper] 用戶 123456789 音訊片段太短 (350ms < 400ms)，略過
```

### **情境 4：辨識失敗**

```
[GroqWhisper] 用戶 123456789 停止說話 (1.20秒)
[GroqWhisper] 開始辨識音訊：230400 bytes (1.20秒)
[GroqWhisper] 辨識結果為空
```

---

## ?? 為什麼不會記錄？診斷清單

現在有了完整的 logging，您可以通過以下方式診斷問題：

### ? **檢查清單**

1. **是否看到「開始監聽頻道」？**
   - ? 沒有 → `StartListeningAsync` 沒被調用或失敗
   - ? 有 → 繼續下一步

2. **是否看到「用戶 XXX 開始說話」？**
   - ? 沒有 → Discord 沒有偵測到語音輸入
     - 檢查 Discord 權限
     - 檢查 bot 是否 selfDeaf
   - ? 有 → 繼續下一步

3. **是否看到「用戶 XXX 停止說話」？**
   - ? 沒有 → 語音太短（< 400ms）
   - ? 有 → 繼續下一步

4. **是否看到「開始辨識音訊」？**
   - ? 沒有 → 音訊太短（< 0.25秒 / 48000 bytes）
   - ? 有 → 繼續下一步

5. **是否看到「辨識成功：XXX」？**
   - ? 沒有 → Groq API 問題
     - 檢查 API Key
     - 檢查網路連線
     - 檢查 API 配額
   - ? 有 → 繼續下一步

6. **是否看到「檢測到觸發詞」？**
   - ? 沒有 → 辨識的文字不包含 soyo/爽世/搜幽
   - ? 有 → 應該會觸發回應

---

## ?? 如何移除觸發詞限制？

如果您想記錄**所有對話**，而不只是包含「soyo」的對話，可以這樣修改：

```csharp
// 移除觸發詞檢查，所有辨識成功的語音都會觸發
Console.WriteLine($"[GroqWhisper] 辨識成功：{text}");

// 直接觸發，不檢查觸發詞
var guild = (_voiceChannel as SocketVoiceChannel)?.Guild;
var user = guild?.GetUser(userId);

if (user != null)
{
    await _onSpeechRecognized(text, user);
}
else
{
    Console.WriteLine($"[GroqWhisper] 找不到用戶 ID: {userId}");
}
```

---

## ?? 技術細節

### **音訊格式轉換**

Discord 傳輸的音訊格式：
```
Opus 編碼 → OpusDecoder → PCM (48kHz, 16-bit, 立體聲)
```

Groq Whisper 需要的格式：
```
PCM → 包裝 WAV Header → audio/wav
```

### **WAV Header 結構**

```csharp
RIFF header (4 bytes): "RIFF"
File size (4 bytes): 36 + pcmData.Length
Format (4 bytes): "WAVE"
Subchunk1 header (4 bytes): "fmt "
Subchunk1 size (4 bytes): 16
Audio format (2 bytes): 1 (PCM)
Channels (2 bytes): 2 (立體聲)
Sample rate (4 bytes): 48000
Byte rate (4 bytes): 192000
Block align (2 bytes): 4
Bits per sample (2 bytes): 16
Subchunk2 header (4 bytes): "data"
Subchunk2 size (4 bytes): pcmData.Length
Data: pcmData
```

### **計算公式**

```csharp
// 每秒音訊大小
BYTES_PER_SECOND = 48000 (採樣率) × 2 (聲道) × 2 (16-bit = 2 bytes)
                 = 192000 bytes/秒

// 音訊時長
Duration = buffer.Length / 192000 秒
```

---

## ? 建置驗證

```bash
dotnet build
# 結果：? 建置成功
```

---

## ?? 使用建議

1. **測試語音辨識**
   - 加入語音頻道
   - 清楚地說「嗨 soyo」或「爽世你好」
   - 觀察 Console 輸出

2. **查看 Log 定位問題**
   - 如果看到「辨識成功」但沒有觸發 → 檢查是否包含觸發詞
   - 如果看到「音訊太短」→ 說話時間延長一點
   - 如果看到「辨識結果為空」→ 檢查 Groq API

3. **調整參數**（如果需要）
   - 修改 `400ms` 最短語音長度
   - 修改 `800ms` 靜音超時
   - 修改 `8秒` 最長單次語音
   - 添加或移除觸發詞

---

**優化日期**: 2026-07-27  
**優化人員**: GitHub Copilot  
**影響範圍**: GroqWhisperService 語音辨識功能
