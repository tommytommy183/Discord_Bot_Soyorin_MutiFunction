# 編譯問題修復記錄

## 問題描述

在 Docker 環境中編譯時出現以下錯誤：

```
error CS1012: Too many characters in character literal
error CS1009: Unrecognized escape sequence
error CS8086: A '}' character must be escaped (by doubling) in an interpolated string
error CS1010: Newline in constant
error CS1026: ) expected
```

**受影響的檔案：**
- `MusicBot2/Service/HolyGrailWarService.cs` (Line 211, 214, 263, 347, 618, 624)
- `MusicBot2/Service/FgoGuessService.cs` (Line 148, 237)

---

## 問題原因

### 根本原因
Unicode 星號字元 `★` 在 C# 中不能作為 `char` 類型使用，因為它佔用多個字節。

### 錯誤代碼
```csharp
string rarityStars = new string('★', servant.Rarity);  // ? 錯誤
```

`'★'` 是一個 Unicode 字元（U+2605），在 UTF-8 編碼中佔用 3 個字節，無法作為 C# 的 `char` 類型（16 位）。

---

## 修復方案

### 正確代碼
```csharp
string rarityStars = string.Concat(Enumerable.Repeat("★", servant.Rarity));  // ? 正確
```

### 修復內容

#### 1. HolyGrailWarService.cs
修復了 2 處：
- Line 211: `SummonServantAsync` 方法中的星號生成
- Line 263: `ListServants` 方法中的星號生成

#### 2. FgoGuessService.cs  
修復了 2 處：
- Line 136: `StartAscensionGameAsync` 方法中的星號生成
- Line 224: `StartNpGameAsync` 方法中的星號生成

---

## 技術說明

### C# char vs string

| 類型 | 大小 | 適用範圍 | 範例 |
|------|------|----------|------|
| `char` | 16 位 | 單一 UTF-16 字元 | `'A'`, `'1'`, `'\n'` |
| `string` | 可變 | 任意 Unicode 字串 | `"★"`, `"你好"`, `"Hello"` |

### Unicode 字元處理

**基本多文種平面（BMP）字元**（U+0000 ~ U+FFFF）
- 可以用 `char` 表示
- 例如：`'A'` (U+0041), `'中'` (U+4E2D)

**補充平面字元**（U+10000 ~ U+10FFFF）或**特殊符號**
- 必須用 `string` 表示
- 例如：`"★"` (U+2605), `"??"` (U+1F600)

---

## 替代方案

### 方案 1: 使用 string.Concat（推薦）
```csharp
string rarityStars = string.Concat(Enumerable.Repeat("★", count));
```
**優點**: 清晰、高效、可讀性好

### 方案 2: 使用 StringBuilder
```csharp
var sb = new StringBuilder();
for (int i = 0; i < count; i++)
    sb.Append("★");
string rarityStars = sb.ToString();
```
**優點**: 適合大量重複

### 方案 3: 使用 LINQ
```csharp
string rarityStars = string.Join("", Enumerable.Range(0, count).Select(_ => "★"));
```
**優點**: 函數式風格

---

## 預防措施

### 1. 編碼規範
? **避免使用**:
```csharp
char star = '★';                        // 編譯錯誤
string stars = new string('★', 5);      // 編譯錯誤
```

? **正確使用**:
```csharp
string star = "★";
string stars = string.Concat(Enumerable.Repeat("★", 5));
```

### 2. 常見 Emoji 和特殊符號
以下字元都必須用 `string` 而非 `char`：
- 星號: `"★"`, `"☆"`
- Emoji: `"??"`, `"??"`, `"??"`, `"??"`
- 中文符號: `"、"`, `"。"`, `"！"`
- 特殊箭頭: `"→"`, `"↓"`

### 3. 編譯環境差異

**Windows (本地開發)**:
- 可能預設使用 Windows-1252 或 UTF-8 with BOM
- Visual Studio 可能自動處理某些編碼問題
- **本地編譯成功不代表跨平台編譯成功**

**Linux (Docker)**:
- 預設使用 UTF-8 without BOM
- 更嚴格的字元類型檢查
- **建議在 Docker 中測試編譯**

---

## 驗證步驟

### 1. 本地編譯測試
```bash
dotnet build MusicBot2.csproj -c Release
```

### 2. Docker 編譯測試
```bash
docker build -t musicbot2-test .
```

### 3. 檢查字元使用
```powershell
# 搜尋可能有問題的字元使用
Select-String -Path "*.cs" -Pattern "new string\('[^A-Za-z0-9]" -Recurse
```

---

## 修復後的編譯結果

? **本地編譯**: 成功  
? **Docker 編譯**: 預期成功

---

## 相關檔案

- `MusicBot2/Service/HolyGrailWarService.cs`
- `MusicBot2/Service/FgoGuessService.cs`

---

## 參考資料

- [C# char vs string](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/char)
- [Unicode in .NET](https://docs.microsoft.com/en-us/dotnet/standard/base-types/character-encoding)
- [UTF-8 Encoding](https://en.wikipedia.org/wiki/UTF-8)

---

## 修復日期

2024-08-14

---

## 檢查清單

- [x] 修復 HolyGrailWarService.cs 中的字元錯誤
- [x] 修復 FgoGuessService.cs 中的字元錯誤
- [x] 本地編譯測試通過
- [x] 建立修復文件
- [ ] Docker 編譯測試（待驗證）

---

**建議**: 未來使用任何 Unicode 特殊字元時，優先使用 `string` 類型。
