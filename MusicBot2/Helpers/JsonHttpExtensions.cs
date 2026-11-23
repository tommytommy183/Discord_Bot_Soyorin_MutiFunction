using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace MusicBot2.Helpers
{
    /// <summary>
    /// 直接從回應串流反序列化 JSON。
    /// 以前是 ReadAsStringAsync() 再 DeserializeObject：PokeAPI 的 /pokemon/{id} 約 300KB（大多是招式清單），
    /// 轉成字串後超過 85KB 會進大型物件堆積（LOH），一直配置很容易讓記憶體碎片化；
    /// 串流讀的話，模型沒有的欄位（例如 moves）會直接跳過，不會整包放進記憶體。
    /// </summary>
    public static class JsonHttpExtensions
    {
        private static readonly JsonSerializer _serializer = JsonSerializer.CreateDefault();

        public static async Task<T> ReadJsonAsync<T>(this HttpContent content)
        {
            using var stream = await content.ReadAsStreamAsync();
            using var sr = new StreamReader(stream);
            using var reader = new JsonTextReader(sr);
            return _serializer.Deserialize<T>(reader);
        }
    }
}
