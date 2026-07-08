using MusicBot2.Helpers;
using MusicBot2.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class UselessApiService
    {
        private readonly HttpClient _httpClient;

        public UselessApiService() 
        {
            _httpClient = new HttpClient();
        }

        public async Task<string> GetUselessApiAsync(int type)
        {
            if(type == 0)
            {
                var random = new Random();
                type = random.Next(1, 9); 
            }


            switch(type)
                {
                case 1:
                    return await GetChuckNorrisJokeAsync();
                case 2:
                    return await GetCatFactAsync();
                case 3:
                    return await GetDogPicAsync();
                case 4:
                    return await GetHitokotoAsync();
                case 5:
                    return await GetDuckPicAsync();
                case 6:
                    return await GetFoxPicAsync();
                case 7:
                    return await GetUselessFactsAsync();
                case 8:
                    return await GetHitokotoAnimeAsync();
                case 9:
                    return await GetHitokotoGameAsync();

                default:
                    return "爆炸摟";
            }
        }

        public async Task<string> GetChuckNorrisJokeAsync()
        {
            var response = await _httpClient.GetAsync("https://api.chucknorris.io/jokes/random");
            if (!response.IsSuccessStatusCode)
                return "爆炸瞜";
            var responseContent = await response.Content.ReadAsStringAsync();
            var joke = JsonConvert.DeserializeObject<ChuckNorrisJoke>(responseContent);
            return joke?.value ?? "爆炸摟";
        }

        public async Task<string> GetCatFactAsync()
        {
            var response = await _httpClient.GetAsync("https://catfact.ninja/fact");
            if (!response.IsSuccessStatusCode)
                return "爆炸摟";
            var responseContent = await response.Content.ReadAsStringAsync();
            var res = JsonConvert.DeserializeObject<CatFact>(responseContent);
            return res?.fact ?? "爆炸摟";
        }

        public async Task<string> GetDogPicAsync()
        {
            var response = await _httpClient.GetAsync("https://dog.ceo/api/breeds/image/random");
            if (!response.IsSuccessStatusCode)
                return "爆炸摟";
            var responseContent = await response.Content.ReadAsStringAsync();
            var res = JsonConvert.DeserializeObject<DogCEO>(responseContent);
            return res?.message ?? "爆炸摟";
        }
        public async Task<string> GetHitokotoAsync()
        {
            var response = await _httpClient.GetAsync("https://v1.hitokoto.cn/");
            if (!response.IsSuccessStatusCode)
                return "爆炸摟";
            var responseContent = await response.Content.ReadAsStringAsync();
            var res = JsonConvert.DeserializeObject<Hitokoto>(responseContent);
            return res?.hitokoto + "..." + res?.from_who;
        }
        public async Task<string> GetDuckPicAsync()
        {
            var response = await _httpClient.GetAsync("https://random-d.uk/api/random");
            if (!response.IsSuccessStatusCode)
                return "爆炸摟";
            var responseContent = await response.Content.ReadAsStringAsync();
            var res = JsonConvert.DeserializeObject<Duck>(responseContent);
            return res?.url ?? "爆炸摟";
        }
        public async Task<string> GetFoxPicAsync()
        {
            var response = await _httpClient.GetAsync("https://randomfox.ca/floof/");
            if (!response.IsSuccessStatusCode)
                return "爆炸摟";
            var responseContent = await response.Content.ReadAsStringAsync();
            var res = JsonConvert.DeserializeObject<Fox>(responseContent);
            return res?.image ?? "爆炸摟";
        }

        public async Task<string> GetUselessFactsAsync()
        {
            var response = await _httpClient.GetAsync("https://uselessfacts.jsph.pl/api/v2/facts/random");
            if (!response.IsSuccessStatusCode)
                return "爆炸摟";
            var responseContent = await response.Content.ReadAsStringAsync();
            var res = JsonConvert.DeserializeObject<UselessFact>(responseContent);
            return res?.text ?? "爆炸摟";
        }

        public async Task<string> GetHitokotoAnimeAsync()
        {
            var response = await _httpClient.GetAsync("https://v1.hitokoto.cn/?c=a");
            if (!response.IsSuccessStatusCode)
                return "爆炸摟";
            var responseContent = await response.Content.ReadAsStringAsync();
            var res = JsonConvert.DeserializeObject<Hitokoto>(responseContent);
            return res?.hitokoto + "..." + res?.from_who;
        }

        public async Task<string> GetHitokotoGameAsync()
        {
            var response = await _httpClient.GetAsync("https://v1.hitokoto.cn/?c=c");
            if (!response.IsSuccessStatusCode)
                return "爆炸摟";
            var responseContent = await response.Content.ReadAsStringAsync();
            var res = JsonConvert.DeserializeObject<Hitokoto>(responseContent);
            return res?.hitokoto + "..." + res?.from_who;
        }
    }
}
