using Discord.WebSocket;
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

        public async Task<string> GetRandomFoodApIAsync(SocketGuildUser user, int foodType)
        {

            if(foodType == 0)
            {
                Random random = new Random();
                foodType = random.Next(1, 10);
            }

            List<Food> chosenFoodList = foodList.Where(f => f.type == foodType).ToList();

            Food chosenFood = chosenFoodList[new Random().Next(chosenFoodList.Count)];


            if (foodType == 10)
            {
                return $"今晚，{user.DisplayName} 想來點他最喜歡的食物: {chosenFood.food}，如果 {user.DisplayName} 沒有買來直播開吃的話，他就再也不會抓到閃的pokemon";
            }
            return $"今晚，{user.DisplayName} 想來點 {chosenFood.food}";
        }

        public async Task<string> GetUselessApiAsync(int type)
        {
            if (type == 0)
            {
                var random = new Random();
                type = random.Next(1, 9);
            }


            switch (type)
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








        private readonly List<Food> foodList = new List<Food>
            {
                #region 台式
                new Food { type = 1, food = "滷肉飯" },
                new Food { type = 1, food = "雞肉飯" },
                new Food { type = 1, food = "火雞肉飯" },
                new Food { type = 1, food = "牛肉麵" },
                new Food { type = 1, food = "陽春麵" },
                new Food { type = 1, food = "乾麵" },
                new Food { type = 1, food = "麻醬麵" },
                new Food { type = 1, food = "炸醬麵" },
                new Food { type = 1, food = "擔仔麵" },
                new Food { type = 1, food = "切仔麵" },
                new Food { type = 1, food = "蚵仔麵線" },
                new Food { type = 1, food = "肉羹麵" },
                new Food { type = 1, food = "鱔魚意麵" },
                new Food { type = 1, food = "鍋燒意麵" },

                new Food { type = 1, food = "雞排" },
                new Food { type = 1, food = "鹽酥雞" },
                new Food { type = 1, food = "鹹水雞" },
                new Food { type = 1, food = "滷味" },
                new Food { type = 1, food = "甜不辣" },
                new Food { type = 1, food = "米血糕" },
                new Food { type = 1, food = "豬血糕" },
                new Food { type = 1, food = "花枝丸" },

                new Food { type = 1, food = "蚵仔煎" },
                new Food { type = 1, food = "臭豆腐" },
                new Food { type = 1, food = "肉圓" },
                new Food { type = 1, food = "刈包" },
                new Food { type = 1, food = "大腸麵線" },
                new Food { type = 1, food = "胡椒餅" },
                new Food { type = 1, food = "蔥油餅" },
                new Food { type = 1, food = "蔥抓餅" },
                new Food { type = 1, food = "水煎包" },
                new Food { type = 1, food = "鍋貼" },
                new Food { type = 1, food = "水餃" },

                new Food { type = 1, food = "控肉飯" },
                new Food { type = 1, food = "排骨飯" },
                new Food { type = 1, food = "雞腿便當" },
                new Food { type = 1, food = "魚排便當" },
                new Food { type = 1, food = "香腸飯" },
                new Food { type = 1, food = "鴨肉飯" },
                new Food { type = 1, food = "鵝肉飯" },
                new Food { type = 1, food = "魯白菜" },

                new Food { type = 1, food = "肉粽" },
                new Food { type = 1, food = "碗粿" },
                new Food { type = 1, food = "米糕" },
                new Food { type = 1, food = "筒仔米糕" },
                new Food { type = 1, food = "油飯" },
                new Food { type = 1, food = "蘿蔔糕" },
                new Food { type = 1, food = "飯糰" },

                new Food { type = 1, food = "蛋餅" },
                new Food { type = 1, food = "燒餅油條" },
                new Food { type = 1, food = "鹹豆漿" },
                new Food { type = 1, food = "豆漿" },

                new Food { type = 1, food = "珍珠奶茶" },
                new Food { type = 1, food = "青草茶" },
                new Food { type = 1, food = "冬瓜茶" },

                new Food { type = 1, food = "芋圓" },
                new Food { type = 1, food = "豆花" },
                new Food { type = 1, food = "仙草" },
                new Food { type = 1, food = "愛玉" },
                new Food { type = 1, food = "剉冰" },

                new Food { type = 1, food = "大腸包小腸" },
                new Food { type = 1, food = "棺材板" },
                new Food { type = 1, food = "雞蛋糕" },
                new Food { type = 1, food = "車輪餅" },
                new Food { type = 1, food = "地瓜球" },
                new Food { type = 1, food = "炸湯圓" },

                #endregion

                #region 中式
                new Food { type = 2, food = "麻婆豆腐" },
                new Food { type = 2, food = "宮保雞丁" },
                new Food { type = 2, food = "糖醋排骨" },
                new Food { type = 2, food = "回鍋肉" },
                new Food { type = 2, food = "魚香茄子" },
                new Food { type = 2, food = "京醬肉絲" },
                new Food { type = 2, food = "水煮魚" },
                new Food { type = 2, food = "酸菜魚" },
                new Food { type = 2, food = "剁椒魚頭" },
                new Food { type = 2, food = "東坡肉" },
                new Food { type = 2, food = "紅燒肉" },
                new Food { type = 2, food = "梅干扣肉" },
                new Food { type = 2, food = "白切雞" },
                new Food { type = 2, food = "口水雞" },
                new Food { type = 2, food = "醉雞" },

                new Food { type = 2, food = "北京烤鴨" },
                new Food { type = 2, food = "烤乳豬" },
                new Food { type = 2, food = "叉燒" },
                new Food { type = 2, food = "燒鵝" },

                new Food { type = 2, food = "小籠包" },
                new Food { type = 2, food = "生煎包" },
                new Food { type = 2, food = "叉燒包" },
                new Food { type = 2, food = "燒賣" },
                new Food { type = 2, food = "春捲" },
                new Food { type = 2, food = "蔥油餅" },
                new Food { type = 2, food = "餡餅" },
                new Food { type = 2, food = "韭菜盒子" },

                new Food { type = 2, food = "炒飯" },
                new Food { type = 2, food = "揚州炒飯" },
                new Food { type = 2, food = "炒麵" },
                new Food { type = 2, food = "刀削麵" },
                new Food { type = 2, food = "炸醬麵" },
                new Food { type = 2, food = "蘭州拉麵" },
                new Food { type = 2, food = "重慶小麵" },

                new Food { type = 2, food = "酸辣湯" },
                new Food { type = 2, food = "佛跳牆" },
                new Food { type = 2, food = "八寶粥" },
                new Food { type = 2, food = "皮蛋瘦肉粥" },

                new Food { type = 2, food = "麻辣香鍋" },
                new Food { type = 2, food = "乾鍋蝦" },
                new Food { type = 2, food = "辣子雞" },
                new Food { type = 2, food = "毛血旺" },

                new Food { type = 2, food = "羊肉串" },
                new Food { type = 2, food = "新疆大盤雞" },
                new Food { type = 2, food = "手抓飯" },

                new Food { type = 2, food = "白灼蝦" },
                new Food { type = 2, food = "蒜蓉蝦" },
                new Food { type = 2, food = "清蒸魚" },
                new Food { type = 2, food = "砂鍋魚頭" },

                new Food { type = 2, food = "紅油抄手" },
                new Food { type = 2, food = "酸辣粉" },
                new Food { type = 2, food = "涼麵" },
                new Food { type = 2, food = "涼皮" },

                new Food { type = 2, food = "海南雞飯" },
                new Food { type = 2, food = "燒臘飯" },
                new Food { type = 2, food = "臘味飯" },

                new Food { type = 2, food = "佛手排骨" },
                new Food { type = 2, food = "鹽焗雞" },
                new Food { type = 2, food = "客家小炒" },
                new Food { type = 2, food = "梅菜扣肉" },
                #endregion

                #region 日式
                new Food { type = 3, food = "壽司" },
                new Food { type = 3, food = "握壽司" },
                new Food { type = 3, food = "生魚片" },
                new Food { type = 3, food = "鮭魚生魚片" },
                new Food { type = 3, food = "海鮮丼" },
                new Food { type = 3, food = "生魚片丼飯" },
                new Food { type = 3, food = "親子丼" },
                new Food { type = 3, food = "牛丼" },
                new Food { type = 3, food = "豬排丼" },
                new Food { type = 3, food = "天丼" },

                new Food { type = 3, food = "拉麵" },
                new Food { type = 3, food = "豚骨拉麵" },
                new Food { type = 3, food = "醬油拉麵" },
                new Food { type = 3, food = "味噌拉麵" },
                new Food { type = 3, food = "鹽味拉麵" },
                new Food { type = 3, food = "沾麵" },
                new Food { type = 3, food = "烏龍麵" },
                new Food { type = 3, food = "蕎麥麵" },

                new Food { type = 3, food = "天婦羅" },
                new Food { type = 3, food = "炸豬排" },
                new Food { type = 3, food = "可樂餅" },
                new Food { type = 3, food = "唐揚雞" },
                new Food { type = 3, food = "日式炸雞" },
                new Food { type = 3, food = "炸蝦" },

                new Food { type = 3, food = "日式咖哩" },
                new Food { type = 3, food = "咖哩豬排飯" },
                new Food { type = 3, food = "蛋包飯" },
                new Food { type = 3, food = "炒烏龍麵" },

                new Food { type = 3, food = "大阪燒" },
                new Food { type = 3, food = "章魚燒" },
                new Food { type = 3, food = "文字燒" },
                new Food { type = 3, food = "關東煮" },

                new Food { type = 3, food = "壽喜燒" },
                new Food { type = 3, food = "涮涮鍋" },
                new Food { type = 3, food = "日式火鍋" },

                new Food { type = 3, food = "燒肉" },
                new Food { type = 3, food = "日式烤肉" },
                new Food { type = 3, food = "串燒" },
                new Food { type = 3, food = "烤飯糰" },

                new Food { type = 3, food = "茶泡飯" },
                new Food { type = 3, food = "釜飯" },
                new Food { type = 3, food = "鰻魚飯" },
                new Food { type = 3, food = "鰻魚丼" },

                new Food { type = 3, food = "味玉拉麵" },
                new Food { type = 3, food = "雞白湯拉麵" },
                new Food { type = 3, food = "豚汁" },

                new Food { type = 3, food = "御好燒" },
                new Food { type = 3, food = "日式便當" },
                new Food { type = 3, food = "幕之內便當" },

                new Food { type = 3, food = "麻糬" },
                new Food { type = 3, food = "銅鑼燒" },
                new Food { type = 3, food = "鯛魚燒" },
                new Food { type = 3, food = "抹茶甜點" },
                new Food { type = 3, food = "日式布丁" },
                #endregion

                #region 韓式
                new Food { type = 4, food = "韓式炸雞" },
                new Food { type = 4, food = "韓式辣雞" },
                new Food { type = 4, food = "韓式炸雞翅" },

                new Food { type = 4, food = "石鍋拌飯" },
                new Food { type = 4, food = "韓式拌飯" },
                new Food { type = 4, food = "紫菜飯捲" },
                new Food { type = 4, food = "韓式飯捲" },
                new Food { type = 4, food = "泡菜炒飯" },

                new Food { type = 4, food = "韓式烤肉" },
                new Food { type = 4, food = "韓牛烤肉" },
                new Food { type = 4, food = "豬五花烤肉" },
                new Food { type = 4, food = "烤五花肉" },

                new Food { type = 4, food = "部隊鍋" },
                new Food { type = 4, food = "泡菜鍋" },
                new Food { type = 4, food = "大醬鍋" },
                new Food { type = 4, food = "豆腐鍋" },
                new Food { type = 4, food = "海鮮鍋" },

                new Food { type = 4, food = "辣炒年糕" },
                new Food { type = 4, food = "韓式年糕" },
                new Food { type = 4, food = "魚板" },
                new Food { type = 4, food = "韓式魚板湯" },

                new Food { type = 4, food = "韓式冷麵" },
                new Food { type = 4, food = "炸醬麵" },
                new Food { type = 4, food = "韓式拌麵" },
                new Food { type = 4, food = "刀削冷麵" },

                new Food { type = 4, food = "海鮮煎餅" },
                new Food { type = 4, food = "泡菜煎餅" },
                new Food { type = 4, food = "綠豆煎餅" },

                new Food { type = 4, food = "韓式餃子" },
                new Food { type = 4, food = "韓式水餃" },

                new Food { type = 4, food = "人參雞" },
                new Food { type = 4, food = "蔘雞湯" },
                new Food { type = 4, food = "牛骨湯" },
                new Food { type = 4, food = "海帶湯" },
                new Food { type = 4, food = "大醬湯" },

                new Food { type = 4, food = "韓式炸醬飯" },
                new Food { type = 4, food = "韓式咖哩飯" },
                new Food { type = 4, food = "韓式豬腳" },
                new Food { type = 4, food = "韓式燉雞" },

                new Food { type = 4, food = "韓式泡菜" },
                new Food { type = 4, food = "韓式小菜拼盤" },
                new Food { type = 4, food = "韓式醃蘿蔔" },

                new Food { type = 4, food = "韓式吐司" },
                new Food { type = 4, food = "韓式蛋糕" },
                new Food { type = 4, food = "韓式冰品" },
                #endregion

                #region 西式
                new Food { type = 5, food = "牛排" },
                new Food { type = 5, food = "菲力牛排" },
                new Food { type = 5, food = "肋眼牛排" },
                new Food { type = 5, food = "紐約客牛排" },
                new Food { type = 5, food = "戰斧牛排" },

                new Food { type = 5, food = "漢堡" },
                new Food { type = 5, food = "起司漢堡" },
                new Food { type = 5, food = "牛肉漢堡" },
                new Food { type = 5, food = "雞肉漢堡" },
                new Food { type = 5, food = "潛艇堡" },

                new Food { type = 5, food = "披薩" },
                new Food { type = 5, food = "夏威夷披薩" },
                new Food { type = 5, food = "瑪格麗特披薩" },
                new Food { type = 5, food = "海鮮披薩" },

                new Food { type = 5, food = "義大利麵" },
                new Food { type = 5, food = "肉醬義大利麵" },
                new Food { type = 5, food = "白醬義大利麵" },
                new Food { type = 5, food = "青醬義大利麵" },
                new Food { type = 5, food = "海鮮義大利麵" },
                new Food { type = 5, food = "焗烤義大利麵" },

                new Food { type = 5, food = "燉飯" },
                new Food { type = 5, food = "奶油燉飯" },
                new Food { type = 5, food = "海鮮燉飯" },
                new Food { type = 5, food = "松露燉飯" },
                new Food { type = 5, food = "鐵板燒" },
                new Food { type = 5, food = "炸雞" },
                new Food { type = 5, food = "美式炸雞" },
                new Food { type = 5, food = "炸雞翅" },
                new Food { type = 5, food = "雞柳條" },

                new Food { type = 5, food = "薯條" },
                new Food { type = 5, food = "洋蔥圈" },
                new Food { type = 5, food = "起司薯條" },

                new Food { type = 5, food = "三明治" },
                new Food { type = 5, food = "火腿起司三明治" },
                new Food { type = 5, food = "俱樂部三明治" },
                new Food { type = 5, food = "可頌三明治" },

                new Food { type = 5, food = "焗烤" },
                new Food { type = 5, food = "焗烤飯" },
                new Food { type = 5, food = "焗烤海鮮" },

                new Food { type = 5, food = "濃湯" },
                new Food { type = 5, food = "玉米濃湯" },
                new Food { type = 5, food = "南瓜濃湯" },

                new Food { type = 5, food = "歐姆蛋" },
                new Food { type = 5, food = "班尼迪克蛋" },

                new Food { type = 5, food = "德國豬腳" },
                new Food { type = 5, food = "香腸拼盤" },
                new Food { type = 5, food = "烤肋排" },
                new Food { type = 5, food = "BBQ烤肉" },

                new Food { type = 5, food = "墨西哥捲餅" },
                new Food { type = 5, food = "塔可" },
                new Food { type = 5, food = "凱薩沙拉" },

                new Food { type = 5, food = "可麗餅" },
                new Food { type = 5, food = "鬆餅" },
                #endregion

                #region 港式
                new Food { type = 6, food = "燒賣" },
                new Food { type = 6, food = "蝦餃" },
                new Food { type = 6, food = "鳳爪" },
                new Food { type = 6, food = "腸粉" },
                new Food { type = 6, food = "蘿蔔糕" },
                new Food { type = 6, food = "流沙包" },
                new Food { type = 6, food = "叉燒包" },
                new Food { type = 6, food = "奶皇包" },
                new Food { type = 6, food = "叉燒酥" },
                new Food { type = 6, food = "蛋黃酥" },

                new Food { type = 6, food = "叉燒" },
                new Food { type = 6, food = "蜜汁叉燒" },
                new Food { type = 6, food = "燒鵝" },
                new Food { type = 6, food = "燒肉" },
                new Food { type = 6, food = "臘味飯" },
                new Food { type = 6, food = "臘腸煲仔飯" },
                new Food { type = 6, food = "煲仔飯" },

                new Food { type = 6, food = "港式炒飯" },
                new Food { type = 6, food = "楊州炒飯" },
                new Food { type = 6, food = "乾炒牛河" },
                new Food { type = 6, food = "炒河粉" },
                new Food { type = 6, food = "星洲炒米" },

                new Food { type = 6, food = "雲吞麵" },
                new Food { type = 6, food = "牛腩麵" },
                new Food { type = 6, food = "車仔麵" },
                new Food { type = 6, food = "撈麵" },

                new Food { type = 6, food = "港式奶茶" },
                new Food { type = 6, food = "鴛鴦奶茶" },
                new Food { type = 6, food = "絲襪奶茶" },
                new Food { type = 6, food = "檸檬茶" },

                new Food { type = 6, food = "菠蘿包" },
                new Food { type = 6, food = "菠蘿油" },
                new Food { type = 6, food = "蛋撻" },
                new Food { type = 6, food = "雞蛋仔" },
                new Food { type = 6, food = "西多士" },

                new Food { type = 6, food = "港式粥品" },
                new Food { type = 6, food = "艇仔粥" },
                new Food { type = 6, food = "皮蛋瘦肉粥" },

                new Food { type = 6, food = "避風塘炒蟹" },
                new Food { type = 6, food = "椒鹽排骨" },
                new Food { type = 6, food = "白灼蝦" },
                new Food { type = 6, food = "蒸魚" },

                new Food { type = 6, food = "港式煲湯" },
                new Food { type = 6, food = "老火湯" },

                new Food { type = 6, food = "楊枝甘露" },
                new Food { type = 6, food = "芝麻糊" },
                new Food { type = 6, food = "龜苓膏" },
                new Food { type = 6, food = "杏仁豆腐" },
                #endregion

                #region 東南亞
                new Food { type = 7, food = "海南雞飯" },
                new Food { type = 7, food = "肉骨茶" },
                new Food { type = 7, food = "叻沙" },
                new Food { type = 7, food = "新加坡炒粿條" },
                new Food { type = 7, food = "炒粿條" },
                new Food { type = 7, food = "星洲米粉" },
                new Food { type = 7, food = "新加坡咖哩雞" },

                new Food { type = 7, food = "馬來咖哩" },
                new Food { type = 7, food = "咖哩叻沙" },
                new Food { type = 7, food = "椰漿飯" },
                new Food { type = 7, food = "沙嗲" },
                new Food { type = 7, food = "馬來烤雞" },
                new Food { type = 7, food = "娘惹料理" },
                new Food { type = 7, food = "娘惹糕" },

                new Food { type = 7, food = "印尼炒飯" },
                new Food { type = 7, food = "印尼炒麵" },
                new Food { type = 7, food = "巴東牛肉" },
                new Food { type = 7, food = "印尼沙嗲" },
                new Food { type = 7, food = "加多加多" },
                new Food { type = 7, food = "雞肉串燒" },

                new Food { type = 7, food = "菲律賓燉肉" },
                new Food { type = 7, food = "菲律賓烤乳豬" },
                new Food { type = 7, food = "阿斗波燉肉" },
                new Food { type = 7, food = "菲律賓炒飯" },

                new Food { type = 7, food = "咖哩魚頭" },
                new Food { type = 7, food = "辣椒螃蟹" },
                new Food { type = 7, food = "黑胡椒蟹" },
                new Food { type = 7, food = "海南雞" },

                new Food { type = 7, food = "椰奶雞湯" },
                new Food { type = 7, food = "椰香飯" },
                new Food { type = 7, food = "南洋咖哩" },
                new Food { type = 7, food = "咖哩魚" },

                new Food { type = 7, food = "越南春捲" },
                new Food { type = 7, food = "東南亞春捲" },

                new Food { type = 7, food = "沙嗲雞肉串" },
                new Food { type = 7, food = "烤雞翅" },
                new Food { type = 7, food = "香茅烤雞" },

                    // 泰式
                new Food { type = 7, food = "泰式打拋豬" },
                new Food { type = 7, food = "泰式綠咖哩" },
                new Food { type = 7, food = "泰式紅咖哩" },
                new Food { type = 7, food = "冬蔭功" },
                new Food { type = 7, food = "泰式炒河粉" },
                new Food { type = 7, food = "月亮蝦餅" },
                new Food { type = 7, food = "泰式檸檬魚" },
                new Food { type = 7, food = "泰式椒麻雞" },
                new Food { type = 7, food = "泰式奶茶" },
                new Food { type = 7, food = "泰式涼拌海鮮" },
                new Food { type = 7, food = "青木瓜沙拉" },
                new Food { type = 7, food = "泰式烤雞" },

                // 越式
                new Food { type = 7, food = "越南河粉" },
                new Food { type = 7, food = "越式牛肉河粉" },
                new Food { type = 7, food = "越式雞肉河粉" },
                new Food { type = 7, food = "越式春捲" },
                new Food { type = 7, food = "越式法國麵包" },
                new Food { type = 7, food = "越式烤肉飯" },
                new Food { type = 7, food = "越式咖哩" },
                new Food { type = 7, food = "越南煎餅" },

                // 新加坡 / 馬來西亞 / 印尼 / 菲律賓
                new Food { type = 7, food = "海南雞飯" },
                new Food { type = 7, food = "肉骨茶" },
                new Food { type = 7, food = "叻沙" },
                new Food { type = 7, food = "椰漿飯" },
                new Food { type = 7, food = "沙嗲" },
                new Food { type = 7, food = "巴東牛肉" },
                new Food { type = 7, food = "印尼炒飯" },
                new Food { type = 7, food = "印尼炒麵" },
                new Food { type = 7, food = "菲律賓燉肉" },
                new Food { type = 7, food = "阿斗波燉肉" },
                new Food { type = 7, food = "辣椒螃蟹" },

                new Food { type = 7, food = "椰香糕" },
                new Food { type = 7, food = "芒果糯米甜點" },
                new Food { type = 7, food = "椰奶西米露" },
                #endregion

                #region 鍋物
                new Food { type = 8, food = "麻辣火鍋" },
                new Food { type = 8, food = "鴛鴦火鍋" },
                new Food { type = 8, food = "涮涮鍋" },
                new Food { type = 8, food = "石頭火鍋" },
                new Food { type = 8, food = "昆布鍋" },
                new Food { type = 8, food = "海鮮鍋" },
                new Food { type = 8, food = "牛肉鍋" },
                new Food { type = 8, food = "豬肉鍋" },
                new Food { type = 8, food = "羊肉鍋" },

                new Food { type = 8, food = "酸菜白肉鍋" },
                new Food { type = 8, food = "薑母鴨" },
                new Food { type = 8, food = "羊肉爐" },
                new Food { type = 8, food = "麻油雞鍋" },
                new Food { type = 8, food = "燒酒雞" },

                new Food { type = 8, food = "臭臭鍋" },
                new Food { type = 8, food = "大腸臭臭鍋" },
                new Food { type = 8, food = "泡菜鍋" },
                new Food { type = 8, food = "牛奶鍋" },
                new Food { type = 8, food = "起司鍋" },
                new Food { type = 8, food = "南瓜鍋" },

                new Food { type = 8, food = "蕃茄鍋" },
                new Food { type = 8, food = "酸辣鍋" },
                new Food { type = 8, food = "麻奶鍋" },
                new Food { type = 8, food = "咖哩鍋" },

                new Food { type = 8, food = "海鮮豆腐鍋" },
                new Food { type = 8, food = "蛤蜊鍋" },
                new Food { type = 8, food = "魚頭鍋" },
                new Food { type = 8, food = "砂鍋魚頭" },

                new Food { type = 8, food = "個人小火鍋" },
                new Food { type = 8, food = "鴨血鍋" },
                new Food { type = 8, food = "麻辣鴨血鍋" },
                new Food { type = 8, food = "牛肉壽喜鍋" },

                new Food { type = 8, food = "韓式部隊鍋" },
                new Food { type = 8, food = "韓式泡菜鍋" },
                new Food { type = 8, food = "日式壽喜燒" },
                new Food { type = 8, food = "日式涮涮鍋" },
                #endregion

                #region 甜點
                new Food { type = 9, food = "蛋糕" },
                new Food { type = 9, food = "巧克力蛋糕" },
                new Food { type = 9, food = "起司蛋糕" },
                new Food { type = 9, food = "生乳蛋糕" },
                new Food { type = 9, food = "戚風蛋糕" },
                new Food { type = 9, food = "千層蛋糕" },
                new Food { type = 9, food = "提拉米蘇" },

                new Food { type = 9, food = "布丁" },
                new Food { type = 9, food = "焦糖布丁" },
                new Food { type = 9, food = "奶酪" },
                new Food { type = 9, food = "果凍" },

                new Food { type = 9, food = "冰淇淋" },
                new Food { type = 9, food = "霜淇淋" },
                new Food { type = 9, food = "聖代" },
                new Food { type = 9, food = "雪花冰" },
                new Food { type = 9, food = "剉冰" },
                new Food { type = 9, food = "芒果冰" },

                new Food { type = 9, food = "豆花" },
                new Food { type = 9, food = "仙草凍" },
                new Food { type = 9, food = "愛玉" },
                new Food { type = 9, food = "芋圓" },
                new Food { type = 9, food = "地瓜圓" },

                new Food { type = 9, food = "甜甜圈" },
                new Food { type = 9, food = "鬆餅" },
                new Food { type = 9, food = "可麗餅" },
                new Food { type = 9, food = "法式吐司" },
                new Food { type = 9, food = "舒芙蕾" },

                new Food { type = 9, food = "馬卡龍" },
                new Food { type = 9, food = "泡芙" },
                new Food { type = 9, food = "可頌甜點" },
                new Food { type = 9, food = "馬芬蛋糕" },

                new Food { type = 9, food = "麻糬" },
                new Food { type = 9, food = "大福" },
                new Food { type = 9, food = "銅鑼燒" },
                new Food { type = 9, food = "鯛魚燒" },
                new Food { type = 9, food = "雞蛋糕" },
                new Food { type = 9, food = "車輪餅" },

                new Food { type = 9, food = "杏仁豆腐" },
                new Food { type = 9, food = "楊枝甘露" },
                new Food { type = 9, food = "芝麻糊" },
                new Food { type = 9, food = "紅豆湯" },
                new Food { type = 9, food = "花生湯" },

                new Food { type = 9, food = "可可塔" },
                new Food { type = 9, food = "水果塔" },
                new Food { type = 9, food = "檸檬塔" },
                new Food { type = 9, food = "肉桂捲" },
                new Food { type = 9, food = "布朗尼" },
            #endregion

                #region 神秘料理
                new Food { type = 10, food = "臭豆腐冰淇淋" },
                new Food { type = 10, food = "皮蛋冰淇淋" },
                new Food { type = 10, food = "香菜冰淇淋" },
                new Food { type = 10, food = "辣椒冰淇淋" },
                new Food { type = 10, food = "芥末冰淇淋" },

                new Food { type = 10, food = "榴槤披薩" },
                new Food { type = 10, food = "榴槤火鍋" },
                new Food { type = 10, food = "榴槤壽司" },
                new Food { type = 10, food = "榴槤漢堡" },

                new Food { type = 10, food = "珍珠奶茶拉麵" },
                new Food { type = 10, food = "珍珠奶茶炒飯" },
                new Food { type = 10, food = "珍珠奶茶火鍋" },
                new Food { type = 10, food = "珍珠奶茶披薩" },

                new Food { type = 10, food = "巧克力炸雞" },
                new Food { type = 10, food = "巧克力培根" },
                new Food { type = 10, food = "巧克力牛肉漢堡" },

                new Food { type = 10, food = "鹹蛋黃冰淇淋" },
                new Food { type = 10, food = "花生醬炒麵" },
                new Food { type = 10, food = "棉花糖披薩" },
                new Food { type = 10, food = "棉花糖漢堡" },

                new Food { type = 10, food = "香蕉披薩" },
                new Food { type = 10, food = "香蕉咖哩" },
                new Food { type = 10, food = "香蕉炒飯" },

                new Food { type = 10, food = "冰淇淋拉麵" },
                new Food { type = 10, food = "冰淇淋火鍋" },
                new Food { type = 10, food = "冰淇淋漢堡" },

                new Food { type = 10, food = "麻辣巧克力" },
                new Food { type = 10, food = "辣味蛋糕" },
                new Food { type = 10, food = "辣椒巧克力" },

                new Food { type = 10, food = "咖哩珍珠奶茶" },
                new Food { type = 10, food = "咖哩冰淇淋" },
                new Food { type = 10, food = "咖哩甜甜圈" },

                new Food { type = 10, food = "泡菜蛋糕" },
                new Food { type = 10, food = "泡菜披薩" },
                new Food { type = 10, food = "泡菜冰淇淋" },

                new Food { type = 10, food = "臭豆腐漢堡" },
                new Food { type = 10, food = "臭豆腐披薩" },
                new Food { type = 10, food = "臭豆腐壽司" },

                new Food { type = 10, food = "章魚燒蛋糕" },
                new Food { type = 10, food = "甜甜圈漢堡" },
                new Food { type = 10, food = "薯條奶昔" },

                new Food { type = 10, food = "黑暗料理拼盤" },
                new Food { type = 10, food = "隨機食材炒飯" },
                new Food { type = 10, food = "廚餘風味炒麵" }

                #endregion
        };
    }
}
