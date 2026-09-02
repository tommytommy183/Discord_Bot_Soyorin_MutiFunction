using Newtonsoft.Json;
using System;

namespace MusicBot2.Helpers
{
    /// <summary>
    /// 將 ulong 序列化為 JSON 字串，避免 JavaScript（Upstash web UI 等工具）
    /// 因 Number.MAX_SAFE_INTEGER 限制造成 Discord Snowflake ID 精度遺失。
    /// 用法：在 ulong 欄位加上 [JsonConverter(typeof(UlongStringConverter))]
    /// </summary>
    public class UlongStringConverter : JsonConverter<ulong>
    {
        public override ulong ReadJson(JsonReader reader, Type objectType,
            ulong existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
                return ulong.Parse((string)reader.Value!);
            // 也接受直接存數字的舊資料（向下相容）
            return Convert.ToUInt64(reader.Value);
        }

        public override void WriteJson(JsonWriter writer, ulong value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }
    }
}
