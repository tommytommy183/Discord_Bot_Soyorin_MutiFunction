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
        public string signature { get; set; }
        public string extension { get; set; }
        public string image_id { get; set; }
        public List<string> favorites { get; set; }
        public string dominant_color { get; set; }
        public string source { get; set; }
        public string uploaded_at { get; set; }
        public bool is_nsfw { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public int byte_size { get; set; }
        public string url { get; set; }
        public string preview_url { get; set; }
        public List<WaifuImTag> tags { get; set; }
    }

    public class WaifuImTag
    {
        public int tag_id { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public bool is_nsfw { get; set; }
    }
}
