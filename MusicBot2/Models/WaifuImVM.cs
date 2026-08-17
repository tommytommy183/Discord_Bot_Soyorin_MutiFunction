using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Models
{
    public class WaifuImResponse
    {
        public List<WaifuImImage> items { get; set; }
    }

    public class WaifuImImage
    {
        public string url { get; set; }
        public bool isNsfw { get; set; }
        public string dominantColor { get; set; }
        public List<WaifuImTag> tags { get; set; }
        public string source { get; set; }
    }

    public class WaifuImTag
    {
        public int id { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public bool is_nsfw { get; set; }
    }

    public class WaifuImTagsResponse
    {
        public Dictionary<string, List<WaifuImTagDetail>> versatile { get; set; }
    }

    public class WaifuImTagDetail
    {
        public int tag_id { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public bool is_nsfw { get; set; }
    }
}
