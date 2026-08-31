# ?? HolyGrailWarService Bug 修復清單

## 已知問題

### 1. ? 戰鬥無法取消
**問題**：頻道內開始戰鬥後無法取消，導致卡住  
**錯誤訊息**：`此頻道已有戰鬥進行中！`

**修復方案**：
```csharp
// 在 SlashCommandHandler 加入
[SlashCommand("fate取消戰鬥", "取消當前頻道的戰鬥")]
public async Task HgwCancelBattleAsync()
{
    var result = _holyGrailWarService.CancelBattle(Context.Channel.Id, Context.User.Id);
    await RespondAsync(result);
}

// 在 HolyGrailWarService 加入
public string CancelBattle(ulong channelId, ulong userId)
{
    if (!_battles.TryGetValue(channelId, out var battle))
        return "? 此頻道沒有進行中的戰鬥";

    // 只有發起者或開發者可以取消
    bool isDev = userId == 開發者ID;
    if (battle.Player1Id != userId && battle.Player2Id != userId && !isDev)
        return "? 你不是此戰鬥的參與者";

    _battles.Remove(channelId);
    Console.WriteLine($"[HolyGrailWar] 頻道 {channelId} 的戰鬥已取消");
    return "? 戰鬥已取消";
}
```

---

### 2. ? Embed 錯誤導致崩潰
**問題**：戰鬥中 Embed 建立失敗，整個訊息變成錯誤訊息  
**原因**：空值、屬性計算錯誤

**修復方案**：
```csharp
private Embed BuildBattleEmbed(HgwBattle battle)
{
    try
    {
        // 空值檢查
        if (battle == null)
            return CommonHelper.BuildErrorResponse("戰鬥資料遺失").Item2;

        var s1 = battle.Servant1;
        var s2 = battle.Servant2;

        if (s1 == null || s2 == null)
            return CommonHelper.BuildErrorResponse("從者資料遺失").Item2;

        // 安全的稀有度顯示
        string rarityStars1 = string.Concat(Enumerable.Repeat("★", s1.Rarity));
        string rarityStars2 = string.Concat(Enumerable.Repeat("★", s2.Rarity));

        var embedBuilder = new EmbedBuilder()
            .WithTitle($"?? 聖杯戰爭 - 第 {battle.TurnCount} 回合")
            .WithColor(new Color(0xE74C3C))
            .AddField($"{GetClassEmoji(s1.ClassName)} {s1.ServantName ?? "未知從者"}",
                $"{rarityStars1}\n" +
                $"?? HP: {s1.CurrentHp}/{s1.MaxHp}\n" +
                $"?? ATK: {s1.Attack} | ??? DEF: {s1.Defense}",
                inline: true)
            .AddField("VS", "?", inline: true)
            .AddField($"{GetClassEmoji(s2.ClassName)} {s2.ServantName ?? "未知從者"}",
                $"{rarityStars2}\n" +
                $"?? HP: {s2.CurrentHp}/{s2.MaxHp}\n" +
                $"?? ATK: {s2.Attack} | ??? DEF: {s2.Defense}",
                inline: true)
            .WithFooter($"{battle.Player1Name} vs {battle.Player2Name ?? "NPC"}")
            .WithCurrentTimestamp();

        return embedBuilder.Build();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[HolyGrailWar] BuildBattleEmbed 錯誤: {ex.Message}");
        Console.WriteLine($"[HolyGrailWar] Stack Trace: {ex.StackTrace}");
        return CommonHelper.BuildErrorResponse($"建立戰鬥介面時發生錯誤: {ex.Message}").Item2;
    }
}
```

---

### 3. ? 抽卡無限制
**問題**：可以無限抽卡，不消耗魔力  
**原因**：沒有檢查魔力是否足夠

**修復方案**：
```csharp
public async Task<(Embed embed, ComponentBuilder component)> SummonServantAsync(ulong userId, string userName)
{
    try
    {
        await EnsureInitAsync();

        if (!_players.TryGetValue(userId, out var player))
            return (CommonHelper.BuildErrorResponse("你還不是御主！").Item2, new ComponentBuilder());

        // ? 檢查魔力
        const int SUMMON_COST = 30;
        if (player.Mana < SUMMON_COST)
        {
            Console.WriteLine($"[HolyGrailWar] 玩家 {userId} 魔力不足 ({player.Mana}/{SUMMON_COST})");
            return (CommonHelper.BuildErrorResponse($"魔力不足！需要 {SUMMON_COST} 點（當前：{player.Mana}）").Item2, 
                new ComponentBuilder());
        }

        // ? 扣除魔力
        player.Mana -= SUMMON_COST;
        Console.WriteLine($"[HolyGrailWar] 玩家 {userId} 消耗 {SUMMON_COST} 魔力，剩餘 {player.Mana}");

        // ... 抽卡邏輯 ...

        SavePlayer(player);
        return (embed, new ComponentBuilder());
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[HolyGrailWar] SummonServant 錯誤: {ex.Message}");
        return (CommonHelper.BuildErrorResponse($"召喚失敗: {ex.Message}").Item2, new ComponentBuilder());
    }
}
```

---

### 4. ? 沒有戰鬥日誌
**問題**：戰鬥過程無法追蹤，Debug 困難

**修復方案**：
```csharp
public async Task<(Embed embed, ComponentBuilder component)> ExecuteBattleActionAsync(
    ulong channelId, ulong userId, BattleAction action)
{
    try
    {
        Console.WriteLine($"[HolyGrailWar] 頻道 {channelId} - 玩家 {userId} 執行動作: {action}");

        if (!_battles.TryGetValue(channelId, out var battle))
        {
            Console.WriteLine($"[HolyGrailWar] 找不到頻道 {channelId} 的戰鬥");
            return (CommonHelper.BuildErrorResponse("找不到進行中的戰鬥").Item2, new ComponentBuilder());
        }

        // 確認是當前玩家的回合
        bool isPlayer1Turn = battle.IsPlayer1Turn;
        ulong currentPlayer = isPlayer1Turn ? battle.Player1Id : battle.Player2Id;

        if (currentPlayer != userId)
        {
            Console.WriteLine($"[HolyGrailWar] 玩家 {userId} 嘗試在非自己回合行動");
            return (CommonHelper.BuildErrorResponse("現在不是你的回合！").Item2, new ComponentBuilder());
        }

        var attacker = isPlayer1Turn ? battle.Servant1 : battle.Servant2;
        var defender = isPlayer1Turn ? battle.Servant2 : battle.Servant1;

        Console.WriteLine($"[HolyGrailWar] {attacker.ServantName} 攻擊 {defender.ServantName}");

        // 計算傷害
        int damage = CalculateDamage(attacker, defender, action);
        defender.CurrentHp = Math.Max(0, defender.CurrentHp - damage);

        Console.WriteLine($"[HolyGrailWar] 造成 {damage} 傷害，{defender.ServantName} 剩餘 HP: {defender.CurrentHp}");

        // 檢查戰鬥結束
        if (defender.CurrentHp <= 0)
        {
            Console.WriteLine($"[HolyGrailWar] {defender.ServantName} 被擊敗！");
            return await FinishBattleAsync(channelId, !isPlayer1Turn);
        }

        // 切換回合
        battle.IsPlayer1Turn = !battle.IsPlayer1Turn;
        battle.TurnCount++;
        Console.WriteLine($"[HolyGrailWar] 回合結束，進入第 {battle.TurnCount} 回合");

        var embed = BuildBattleEmbed(battle);
        var component = BuildBattleButtons(channelId, battle);
        return (embed, component);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[HolyGrailWar] ExecuteBattleAction 嚴重錯誤: {ex.Message}");
        Console.WriteLine($"[HolyGrailWar] Stack Trace: {ex.StackTrace}");
        return (CommonHelper.BuildErrorResponse($"戰鬥執行錯誤: {ex.Message}").Item2, new ComponentBuilder());
    }
}
```

---

### 5. ? 重複開始戰鬥
**問題**：同一頻道可以重複開始戰鬥

**修復方案**：
```csharp
public async Task<(Embed embed, ComponentBuilder component)> StartBattleAsync(
    ulong channelId, ulong player1Id, string player1Name, ulong? player2Id, string player2Name)
{
    try
    {
        Console.WriteLine($"[HolyGrailWar] 玩家 {player1Name} ({player1Id}) 嘗試開始戰鬥");

        // ? 檢查是否已有戰鬥
        if (_battles.ContainsKey(channelId))
        {
            Console.WriteLine($"[HolyGrailWar] 頻道 {channelId} 已有戰鬥進行中");
            return (CommonHelper.BuildErrorResponse(
                "此頻道已有戰鬥進行中！\n" +
                "請使用 `/fate取消戰鬥` 取消後再試。").Item2, 
                new ComponentBuilder());
        }

        // ... 戰鬥邏輯 ...

        Console.WriteLine($"[HolyGrailWar] 戰鬥開始成功 - {player1Name} vs {player2Name ?? "NPC"}");
        return (embed, component);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[HolyGrailWar] StartBattle 錯誤: {ex.Message}");
        return (CommonHelper.BuildErrorResponse($"開始戰鬥失敗: {ex.Message}").Item2, new ComponentBuilder());
    }
}
```

---

## ?? 立即修復步驟

### Step 1: 加入日誌系統
在所有關鍵方法加入：
```csharp
Console.WriteLine($"[HolyGrailWar] {操作描述}");
```

### Step 2: 加入 try-catch
所有 public 方法包裹：
```csharp
try
{
    // 原邏輯
}
catch (Exception ex)
{
    Console.WriteLine($"[HolyGrailWar] 錯誤: {ex.Message}");
    return ErrorResponse;
}
```

### Step 3: 空值檢查
所有 Embed 建立前：
```csharp
if (obj == null || obj.Property == null)
    return CommonHelper.BuildErrorResponse("資料遺失");
```

### Step 4: 加入取消功能
```csharp
public string CancelBattle(ulong channelId, ulong userId)
{
    if (!_battles.TryGetValue(channelId, out var battle))
        return "? 此頻道沒有進行中的戰鬥";

    _battles.Remove(channelId);
    Console.WriteLine($"[HolyGrailWar] 戰鬥已取消");
    return "? 戰鬥已取消";
}
```

### Step 5: 檢查重複開始
```csharp
if (_battles.ContainsKey(channelId))
    return ErrorResponse("已有戰鬥進行中");
```

---

## ? 測試清單

- [ ] 開始戰鬥
- [ ] 執行攻擊
- [ ] 取消戰鬥
- [ ] 重複開始（應顯示錯誤）
- [ ] 魔力不足（應無法召喚）
- [ ] Embed 顯示正常
- [ ] 日誌正常輸出

---

這份文件列出了所有需要修復的 bug 和對應的解決方案！???
