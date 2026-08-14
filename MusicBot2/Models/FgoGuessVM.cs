using System.Text.Json.Serialization;

namespace MusicBot2.Models
{
    // ── Atlas Academy basic_servant 清單條目 ──────────────────────────────
    public class FgoBasicServant
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("collectionNo")]
        public int CollectionNo { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("className")]
        public string ClassName { get; set; }

        [JsonPropertyName("rarity")]
        public int Rarity { get; set; }

        [JsonPropertyName("face")]
        public string Face { get; set; }
    }

    // ── 從 nice/TW/servant/{id} 擷取所需欄位 ─────────────────────────────
    public class FgoNiceServant
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("collectionNo")]
        public int CollectionNo { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("className")]
        public string ClassName { get; set; }

        [JsonPropertyName("atkBase")]
        public int AtkBase { get; set; }

        [JsonPropertyName("hpBase")]
        public int HpBase { get; set; }

        [JsonPropertyName("cards")]
        public List<string> Cards { get; set; } = new();

        [JsonPropertyName("extraAssets")]
        public FgoExtraAssets ExtraAssets { get; set; }

        [JsonPropertyName("noblePhantasms")]
        public List<FgoNoblePhantasm> NoblePhantasms { get; set; } = new();

        [JsonPropertyName("skills")]
        public List<FgoSkill> Skills { get; set; } = new();
    }

    public class FgoExtraAssets
    {
        [JsonPropertyName("charaGraph")]
        public FgoAssetMap CharaGraph { get; set; }

        [JsonPropertyName("faces")]
        public FgoAssetMap Faces { get; set; }
    }

    public class FgoAssetMap
    {
        // 各升階圖片："1","2","3","4" → URL
        [JsonPropertyName("ascension")]
        public Dictionary<string, string> Ascension { get; set; }

        [JsonPropertyName("costume")]
        public Dictionary<string, string> Costume { get; set; }
    }

    public class FgoNoblePhantasm
    {
        [JsonPropertyName("num")]
        public int Num { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("ruby")]
        public string Ruby { get; set; }

        [JsonPropertyName("card")]
        public string Card { get; set; }

        [JsonPropertyName("icon")]
        public string Icon { get; set; }

        [JsonPropertyName("detail")]
        public string Detail { get; set; }

        [JsonPropertyName("functions")]
        public List<FgoNpFunction> Functions { get; set; } = new();
    }

    public class FgoNpFunction
    {
        [JsonPropertyName("funcType")]
        public string FuncType { get; set; }

        [JsonPropertyName("targetType")]
        public string TargetType { get; set; }

        [JsonPropertyName("funcPopupText")]
        public string FuncPopupText { get; set; }

        [JsonPropertyName("svals")]
        public List<FgoFuncValue> Svals { get; set; } = new();

        [JsonPropertyName("buffs")]
        public List<FgoBuff> Buffs { get; set; } = new();
    }

    public class FgoSkill
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("num")]
        public int Num { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("detail")]
        public string Detail { get; set; }

        [JsonPropertyName("icon")]
        public string Icon { get; set; }

        [JsonPropertyName("coolDown")]
        public List<int> CoolDown { get; set; } = new();

        [JsonPropertyName("functions")]
        public List<FgoNpFunction> Functions { get; set; } = new();
    }

    public class FgoFuncValue
    {
        [JsonPropertyName("Value")]
        public int Value { get; set; }

        [JsonPropertyName("Value2")]
        public int Value2 { get; set; }

        [JsonPropertyName("Turn")]
        public int Turn { get; set; }

        [JsonPropertyName("Count")]
        public int Count { get; set; }

        [JsonPropertyName("Rate")]
        public int Rate { get; set; }
    }

    public class FgoBuff
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    // ── 遊戲狀態 ───────────────────────────────────────────────────────────
    public enum FgoGuessMode { Silhouette, NoblePhantasm, Ascension }

    public class FgoGuessState
    {
        public ulong ChannelId { get; set; }
        public FgoGuessMode Mode { get; set; }
        public int AnswerCollectionNo { get; set; }
        public string AnswerName { get; set; }        // 正確角色名
        public string AnswerNpName { get; set; }      // 正確寶具名（NP 模式）
        public int AnswerAscensionStage { get; set; } // 正確階段 1-4（Ascension 模式）
        public string CharaImageUrl { get; set; }     // 角色圖（NP / Ascension 模式顯示用）
        public List<string> Options { get; set; } = new();  // 選項（按鈕文字）
        public bool IsAnswered { get; set; } = false;
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
    }
}
