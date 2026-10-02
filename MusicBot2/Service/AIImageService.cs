using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.IO;

namespace MusicBot2.Service
{
    public class AIImageService
    {
        private readonly HttpClient _httpClient;
        private const string CfWorkerUrl = "https://broken-queen-beaa.tommytommy183.workers.dev";
        private const string CfApiKey = "8f7c2d91e6a44b0f9c3e8d72a1f65b9c4e7d2a8f";

        // CharacterImages 資料夾路徑（相對於執行目錄）
        private static readonly string CharacterImagesDir =
            Path.Combine(AppContext.BaseDirectory, "CharacterImages");

        // 角色 key → 檔名對應
        public static readonly System.Collections.Generic.Dictionary<string, string> CharacterFiles = new()
        {
            // MyGO!!!!!
            { "soyo",    "soyo.png"    },
            { "tomori",  "tomori.png"  },
            { "anon",    "anon.png"    },
            { "rikki",   "rikki.png"   },
            { "raana",   "raana.png"   },
            // Ave Mujica
            { "sakiko",  "sakiko.png"  },
            { "mutsumi", "mutsumi.png" },
            { "uika",    "uika.png"    },
            { "umiri",   "umiri.png"   },
            { "nyamu",   "nyamu.png"   },
            // Mugendai Mewtype
            { "arale",   "arale.png"   },
            { "nonoka",  "nonoka.png"  },
            { "ritsu",   "ritsu.png"   },
            { "miyako",  "miyako.png"  },
            { "yuno",    "yuno.png"    },
            // millsage
            { "hotaru",  "hotaru.png"  },
            { "natsume", "natsume.png" },
            { "nagi",    "nagi.png"    },
            { "mahoro",  "mahoro.png"  },
            { "houka",   "houka.png"   },
        };

        // 角色 key → 中文名（讓 Soyo 系統提示知道）
        public static readonly System.Collections.Generic.Dictionary<string, string> CharacterNames = new()
        {
            // MyGO!!!!!
            { "soyo",    "長崎爽世"  },
            { "tomori",  "高松燈"    },
            { "anon",    "千早愛音"  },
            { "rikki",   "椎名立希"  },
            { "raana",   "要楽奈"    },
            // Ave Mujica
            { "sakiko",  "倉田祥子"  },
            { "mutsumi", "乙坂睦"    },
            { "uika",    "三角初華"  },
            { "umiri",   "八幡海鈴"  },
            { "nyamu",   "にゃむ"    },
            // Mugendai Mewtype
            { "arale",   "中街アラレ"  },
            { "nonoka",  "宮永ノノカ"  },
            { "ritsu",   "峰月リツ"    },
            { "miyako",  "藤みやこ"    },
            { "yuno",    "仙石ユノ"    },
            // millsage
            { "hotaru",  "塩見ほたる"  },
            { "natsume", "伊澤なつめ"  },
            { "nagi",    "琴平凪"      },
            { "mahoro",  "浜崎まほろ"  },
            { "houka",   "和泉ほうか"  },
        };

        public AIImageService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        }

        // 純文字產圖（沿用舊邏輯）
        public async Task<Stream> GenerateImageAsync(string prompt)
        {
            try
            {
                Console.WriteLine($"[AIImage] 純文字產圖 prompt={prompt[..Math.Min(80, prompt.Length)]}");
                var form = new MultipartFormDataContent();
                form.Add(new StringContent(prompt), "prompt");
                using var req = new HttpRequestMessage(HttpMethod.Post, CfWorkerUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CfApiKey);
                req.Content = form;
                using var response = await _httpClient.SendAsync(req);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    Console.WriteLine($"[AIImage] Worker 成功 {bytes.Length} bytes");
                    return new MemoryStream(bytes);
                }
                Console.WriteLine($"[AIImage] Worker error {(int)response.StatusCode}，fallback");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIImage] Worker 失敗: {ex.Message}，fallback");
            }

            return await CallPollinationsAsync(prompt);
        }

        // 帶參考圖片產圖（byte[]）
        public async Task<Stream> GenerateImageWithReferenceAsync(string prompt, byte[] imageBytes, string filename = "reference.png", string contentType = "image/png")
        {
            try
            {
                Console.WriteLine($"[AIImage] 帶圖產圖 imageSize={imageBytes.Length} prompt={prompt[..Math.Min(60, prompt.Length)]}");
                var form = new MultipartFormDataContent();
                form.Add(new StringContent(prompt), "prompt");
                var imgContent = new ByteArrayContent(imageBytes);
                imgContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                form.Add(imgContent, "image", filename);
                using var req = new HttpRequestMessage(HttpMethod.Post, CfWorkerUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CfApiKey);
                req.Content = form;
                using var response = await _httpClient.SendAsync(req);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    Console.WriteLine($"[AIImage] Worker(圖) 成功 {bytes.Length} bytes");
                    return new MemoryStream(bytes);
                }
                var errBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[AIImage] Worker(圖) error {(int)response.StatusCode}: {errBody[..Math.Min(200, errBody.Length)]}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIImage] Worker(圖) 失敗: {ex.Message}");
                return null;
            }
        }

        // 用預存角色圖產圖
        public async Task<Stream> GenerateCharacterImageAsync(string characterKey, string prompt)
        {
            if (!CharacterFiles.TryGetValue(characterKey.ToLower(), out var filename))
            {
                Console.WriteLine($"[AIImage] 找不到角色 key={characterKey}，改用純文字");
                return await GenerateImageAsync(prompt);
            }

            var path = Path.Combine(CharacterImagesDir, filename);
            if (!File.Exists(path))
            {
                Console.WriteLine($"[AIImage] 角色圖不存在 {path}，改用純文字");
                return await GenerateImageAsync(prompt);
            }

            var imageBytes = await File.ReadAllBytesAsync(path);
            var result = await GenerateImageWithReferenceAsync(prompt, imageBytes, filename);
            return result ?? await GenerateImageAsync(prompt); // fallback
        }

        private async Task<Stream> CallPollinationsAsync(string prompt)
        {
            string encodedPrompt = WebUtility.UrlEncode(prompt);
            string url = $"https://image.pollinations.ai/prompt/{encodedPrompt}?model=flux";
            Console.WriteLine("[AIImage] Pollinations fallback");
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            return new MemoryStream(bytes);
        }
    }
}
