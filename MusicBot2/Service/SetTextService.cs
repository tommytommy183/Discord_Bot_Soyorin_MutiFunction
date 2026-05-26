using StackExchange.Redis;

public class SetTextService
{
    private readonly IDatabase _db;
    private const string HashKey = "setText";

    public SetTextService(string redisConnectionString)
    {
        var redis = ConnectionMultiplexer.Connect(redisConnectionString);
        _db = redis.GetDatabase();
    }

    // ── 關鍵字比對 ────────────────────────────────────
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