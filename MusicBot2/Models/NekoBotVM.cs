using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Models
{
    public class NekoBotImageGenResponse
    {
        public string message { get; set; }
        public bool success { get; set; }
    }

    public class NekoBotImageResponse
    {
        public string message { get; set; }
        public bool success { get; set; }
        public int color { get; set; }
    }
}
