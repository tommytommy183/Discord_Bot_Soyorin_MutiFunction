using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Models
{
    public class Game1A2BSession
    {
        public string channelId { get; set; }    // 遊戲所在的頻道 ID
        public string Answer { get; set; }      // 正確答案
        public int Attempts { get; set; } = 0;
        public List<string> History { get; set; } = new();
        public ulong MessageId { get; set; }    // 綁定的 Embed 訊息 ID
    }
}
