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
}
