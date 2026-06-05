using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Models
{
    public class Pick2GameRequest
    {
        public int element_count { get; set; }
        public string password { get; set; }
        public string post_serial { get; set; }
    }
    
    // API 回應模型
    public class Pick2GameResponse
    {
        public string game_serial { get; set; }
        public GameData data { get; set; }
    }

    public class GameData
    {
        public string hash { get; set; }
        public string title { get; set; }
        public int current_round { get; set; }
        public int of_round { get; set; }
        public int remain_elements { get; set; }
        public int total_elements { get; set; }
        public List<Pick2Element> elements { get; set; }
    }

    public class Pick2Element
    {
        public int id { get; set; }
        public string source_url { get; set; }
        public string thumb_url { get; set; }
        public string mediumthumb_url { get; set; }
        public string lowthumb_url { get; set; }
        public string imgur_url { get; set; }
        public string title { get; set; }
        public string type { get; set; }
        public string video_start_second { get; set; }
        public string video_end_second { get; set; }
        public string video_source { get; set; }
        public string video_id { get; set; }
        public string video_duration_second { get; set; }
        public string is_eliminated { get; set; }
        public string is_ready { get; set; }
        public string win_count { get; set; }
    }


    public class GetElementsResponse
    {
        public string game_serial { get; set; }
        public int total_count { get; set; }
        public List<Pick2Element> data { get; set; }
    }

    // 遊戲狀態類別
    public class Pick2GameState
    {
        public ulong ChannelId { get; set; }
        public string GameSerial { get; set; }
        public string Title { get; set; }
        public List<Pick2Element> AllElements { get; set; }
        public List<Pick2Element> CurrentBracket { get; set; }  // 當前輪次的所有選手
        public List<Pick2Element> NextBracket { get; set; }  // 下一輪的勝者
        public List<Pick2Element> CurrentOptions { get; set; }  // 當前比賽的兩個選手
        public int CurrentMatchIndex { get; set; }  // 當前比賽場次（0-based）
        public int TotalMatches { get; set; }  // 本輪總場次
        public int CurrentRound { get; set; }  // 當前是第幾輪（1=初賽, 2=第二輪...）
        public Dictionary<int, HashSet<ulong>> Votes { get; set; }
        public ulong ImageMessageId { get; set; }  // 圖片訊息的 ID
        public ulong VoteMessageId { get; set; }   // 投票訊息的 ID
    }
}
