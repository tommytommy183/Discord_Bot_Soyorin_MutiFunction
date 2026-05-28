using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MusicBot2.Models
{
    public class GeminiRequestVM
    {
        public string SystemInstruction { get; set; }
        public string UserMessage { get; set; }

        public float Temperature { get; set; } = 0.85f;
        public float TopP { get; set; } = 0.9f;
        public int TopK { get; set; } = 40;
        public int MaxOutputTokens { get; set; } = 256;
    }

    public class GeminiApiRequest
    {
        //user 打出的訊息
        public Content[] contents { get; set; }
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        //AI的角色設定
        public SystemInstruction systemInstruction { get; set; }
        //生成參數設定 (溫度、topP、最大輸出Token數等)
        public GenerationConfig generationConfig { get; set; }
        //安全設定 (內容過濾等)
        public List<SafetySettings> safetySettings { get; set; }
    }

    public class Content
    {
        public string role { get; set; } = "user";
        public Part[] parts { get; set; }
    }

    public class Part
    {
        public string text { get; set; }
    }

    public class SystemInstruction
    {
        public Part[] parts { get; set; }
    }

    public class GenerationConfig
    {
        //控制生成文本的隨機程度，值越高生成的文本越多樣化
        public float temperature { get; set; }
        //控制生成文本的多樣性，值越高生成的文本越多樣化
        public float topP { get; set; }
        //限制只從機率最高的 K 個 token 中採樣
        public int topK { get; set; }
        //限制生成文本的最大長度，以Token為單位
        public int maxOutputTokens { get; set; }
        //停止生成的序列
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[] stopSequences { get; set; }
    }
    public class SafetySettings
    {
        //內容過濾的類別，例如暴力、成人內容、仇恨言論等
        public string category { get; set; }
        //過濾的閾值，通常是0到1之間的值，表示過濾的嚴格程度
        public string threshold { get; set; }
    }
}
