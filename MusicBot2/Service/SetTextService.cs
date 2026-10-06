using StackExchange.Redis;

public class SetTextService
{
    private readonly IDatabase _db;
    private const string HashKey     = "setText";
    private const string SteamHashKey = "steamLinks";

    public SetTextService(string redisConnectionString)
    {
        var redis = ConnectionMultiplexer.Connect(redisConnectionString);
        _db = redis.GetDatabase();
    }

    // ── 關鍵字比對（不含 Steam 邏輯）────────────────────
    public async Task<string?> Match(string input)
    {
        var entries = await _db.HashGetAllAsync(HashKey);
        foreach (var entry in entries)
        {
            if (input.Contains(entry.Name.ToString(), StringComparison.OrdinalIgnoreCase))
                return entry.Value.ToString();
        }
        return null;
    }

    // ── Steam 連結重複偵測 ────────────────────────────
    // 回傳 null  → 第一次出現，已記錄
    // 回傳 value → 重複！value = (origChannelId, origMessageId, origUserId)
    public async Task<(ulong ChannelId, ulong MessageId, ulong UserId)?> CheckSteamLinkAsync(
        string messageContent, ulong channelId, ulong messageId, ulong userId)
    {
        var url = ExtractSteamUrl(messageContent);
        if (url == null) return null;

        var existing = await _db.HashGetAsync(SteamHashKey, url);
        if (existing.HasValue && !existing.IsNullOrEmpty)
        {
            var parts = existing.ToString().Split(':');
            if (parts.Length == 3 &&
                ulong.TryParse(parts[0], out var origCh) &&
                ulong.TryParse(parts[1], out var origMsg) &&
                ulong.TryParse(parts[2], out var origUser))
            {
                return (origCh, origMsg, origUser);
            }
        }

        // 第一次，記錄下來
        await _db.HashSetAsync(SteamHashKey, url, $"{channelId}:{messageId}:{userId}");
        return null;
    }

    private static string? ExtractSteamUrl(string content)
    {
        const string prefix = "https://store.steampowered.com/";
        var start = content.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var end = content.IndexOfAny(new[] { ' ', '\n', '\r', '<', '>' }, start);
        return end < 0 ? content[start..] : content[start..end];
    }

    // ── 取得所有資料 ──────────────────────────────────
    public async Task<IReadOnlyDictionary<string, string>> GetAll()
    {
        var entries = await _db.HashGetAllAsync(HashKey);
        return entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
    }

    // ── 新增 / 更新 / 刪除 ────────────────────────────
    public async Task Set(string key, string value)
    {
        if (string.IsNullOrEmpty(value))
            await _db.HashDeleteAsync(HashKey, key);
        else
            await _db.HashSetAsync(HashKey, key, value);
    }
}