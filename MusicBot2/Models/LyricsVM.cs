using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Models
{
    public class LyricsResponse
    {
        public int id { get; set; }
        public string trackName { get; set; }
        public string artistName { get; set; }
        public string albumName { get; set; }
        public double duration { get; set; }
        public bool instrumental { get; set; }
        public string plainLyrics { get; set; }
        public string syncedLyrics { get; set; }
    }
}
