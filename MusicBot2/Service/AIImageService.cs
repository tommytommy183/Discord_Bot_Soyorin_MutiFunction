using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class AIImageService
    {
        private readonly HttpClient _httpClient;
        public AIImageService()
        {
            _httpClient = new HttpClient();
        }
        //https://loremflickr.com/800/600/anime
        //GET https://image.pollinations.ai/prompt/一隻可愛的柴犬坐在草地上

        public async Task<Stream> GenerateImageAsync(string prompt, string model)
        {
            string encodedPrompt = WebUtility.UrlEncode(prompt);
            string url = "";

            switch (model)
            {
                case "loremflickr":
                    url = $"https://loremflickr.com/800/600/{encodedPrompt}";
                    break;
                case "pollinations":
                    url = $"https://image.pollinations.ai/prompt/{encodedPrompt}?model=flux";
                    break;
                default:
                    break;
            }

            using var response = await _httpClient.GetAsync(url);

            response.EnsureSuccessStatusCode();


            byte[] bytes = await response.Content.ReadAsByteArrayAsync();


            var ms = new MemoryStream(bytes);

            ms.Position = 0;

            return ms;
        }


    }
}
