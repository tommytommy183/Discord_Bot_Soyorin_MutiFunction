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
        bool isSteamLinkExists = false;
        //如果輸入包含 Steam 商店連結，先檢查是否有對應的關鍵字存在
        if (input.Contains("https://store.steampowered.com/"))
        {
            foreach (var entry in entries)
            {
                //有的話就回傳對應的值
                if (input.Contains(entry.Name.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    isSteamLinkExists = true;
                    return entry.Value.ToString();
                }
            }
            //沒有的話就設置這次的連結
            if(!isSteamLinkExists)
            {
                await Set(input, "耖你媽，傳過了");
            }
        }
        else
        {
            foreach (var entry in entries)
            {
                if (input.Contains(entry.Name.ToString(), StringComparison.OrdinalIgnoreCase))
                    return entry.Value.ToString();
            }
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