using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Models
{
    public class ChuckNorrisJoke
    {
        public string id { get; set; }
        public string value { get; set; }
    }

    public class CatFact
    {
        public string fact { get; set; }
    }

    public class DogCEO
    {
        public string message { get; set; }
    }

    public class Hitokoto
    {
        public string hitokoto { get; set; }
        public string from_who { get; set; }
    }

    public class Duck
    {
        public string url { get; set; }
    }

    public class Fox
    {
        public string image { get; set; }
    }
}
