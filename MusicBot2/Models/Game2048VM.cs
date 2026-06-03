using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Models
{
    public class Game2048VM
    {
        public int Size { get; set; } = 4;
        public int[,] Grid { get; set; }
        public int Score { get; set; } = 0;
        public int LargestNum { get; set; } = 2;
        public bool GameOver { get; set; } = false;
        public bool Won { get; set; } = false;
        public ulong ChannelId { get; set; }

        public Game2048VM()
        {
            Grid = new int[Size, Size];
        }
    }
}
