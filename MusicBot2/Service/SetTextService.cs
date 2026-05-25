using ElevenLabs.Voices;
using System.Text.Json;

public class SetTextService
{
    private readonly string _filePath;
    private Dictionary<string, string> _data = new();

    public SetTextService(string basePath = "")
    {
        _filePath = Path.Combine(basePath, "TxtFolder", "SetText.json");
        Load();
    }

    // ── 讀取 ──────────────────────────────────────────
    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            _data = new Dictionary<string, string>();
            return;
        }

        var json = File.ReadAllText(_filePath);
        _data = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
    }

    // ── 關鍵字比對：輸入一段訊息，找出對應回覆 ─────────
    // 比對方式：訊息中只要包含 key（不分大小寫）就觸發
    public string? Match(string input)
    {
        foreach (var (key, value) in _data)
        {
            if (input.Contains(key, StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return null; // 沒有符合的關鍵字
    }


    // ── 取得所有資料 ──────────────────────────────────
    public IReadOnlyDictionary<string, string> GetAll() => _data;

    // ── 新增 / 更新一筆 ──────────────────────────────
    public void Set(string key, string value)
    {
        _data[key] = value;
        if(string.IsNullOrEmpty(value))
        {
            _data.Remove(key);
        }
        Save();
    }

    // ── 刪除一筆 ─────────────────────────────────────
    private bool Remove(string key)
    {
        var removed = _data.Remove(key);
        if (removed) Save();
        return removed;
    }

    // ── 寫回 JSON ────────────────────────────────────
    private void Save()
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_data,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}