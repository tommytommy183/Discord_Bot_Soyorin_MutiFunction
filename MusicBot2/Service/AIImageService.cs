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

        // Cloudflare Workers 的 formData() 不接受引號包的 boundary，需要手動去掉引號
        private static MultipartFormDataContent CreateForm()
        {
            var boundary = "----CFBoundary" + Guid.NewGuid().ToString("N");
            var form = new MultipartFormDataContent(boundary);
            // 移除 .NET 自動加的引號：boundary="abc" → boundary=abc
            form.Headers.Remove("Content-Type");
            form.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");
            return form;
        }

        // 純文字產圖（沿用舊邏輯）
        public async Task<Stream> GenerateImageAsync(string prompt)
        {
            try
            {
                Console.WriteLine($"[AIImage] 純文字產圖 prompt={prompt[..Math.Min(80, prompt.Length)]}");
                var form = CreateForm();
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

            var fallback = await CallPollinationsAsync(prompt);
            if (fallback == null)
                Console.WriteLine("[AIImage] 所有管道都失敗，回傳 null");
            return fallback;
        }

        // 帶參考圖片產圖（byte[]）
        public async Task<Stream> GenerateImageWithReferenceAsync(string prompt, byte[] imageBytes, string filename = "reference.png", string contentType = "image/png")
        {
            try
            {
                Console.WriteLine($"[AIImage] 帶圖產圖 imageSize={imageBytes.Length} prompt={prompt[..Math.Min(60, prompt.Length)]}");
                var form = CreateForm();
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

        // prompt 裡提到的角色名稱 → key 對照表
        private static readonly Dictionary<string, string> NameToKey = new(StringComparer.OrdinalIgnoreCase)
        {
            // MyGO!!!!!
            { "soyo", "soyo" }, { "nagasaki soyo", "soyo" }, { "爽世", "soyo" }, { "長崎爽世", "soyo" },
            { "tomori", "tomori" }, { "komatsubara tomori", "tomori" }, { "高松燈", "tomori" }, { "燈", "tomori" },
            { "anon", "anon" }, { "chihaya anon", "anon" }, { "千早愛音", "anon" }, { "愛音", "anon" },
            { "rikki", "rikki" }, { "shiina rikki", "rikki" }, { "椎名立希", "rikki" }, { "立希", "rikki" },
            { "raana", "raana" }, { "yoyogi raana", "raana" }, { "要楽奈", "raana" }, { "楽奈", "raana" },
            // Ave Mujica
            { "sakiko", "sakiko" }, { "togawa sakiko", "sakiko" }, { "倉田祥子", "sakiko" }, { "祥子", "sakiko" },
            { "mutsumi", "mutsumi" }, { "wakaba mutsumi", "mutsumi" }, { "若葉睦", "mutsumi" }, { "睦", "mutsumi" },
            { "uika", "uika" }, { "misumi uika", "uika" }, { "三角初華", "uika" }, { "初華", "uika" },
            { "umiri", "umiri" }, { "yahata umiri", "umiri" }, { "八幡海鈴", "umiri" }, { "海鈴", "umiri" },
            { "nyamu", "nyamu" }, { "yuutenji nyamu", "nyamu" }, { "にゃむ", "nyamu" },
            // Mugendai Mewtype
            { "arale", "arale" }, { "nakamachi arale", "arale" }, { "中街アラレ", "arale" },
            { "nonoka", "nonoka" }, { "miyanaga nonoka", "nonoka" }, { "宮永ノノカ", "nonoka" },
            { "ritsu", "ritsu" }, { "minetsuki ritsu", "ritsu" }, { "峰月リツ", "ritsu" },
            { "miyako", "miyako" }, { "fuji miyako", "miyako" }, { "藤みやこ", "miyako" },
            { "yuno", "yuno" }, { "sengoku yuno", "yuno" }, { "仙石ユノ", "yuno" },
            // millsage
            { "hotaru", "hotaru" }, { "shiomi hotaru", "hotaru" }, { "塩見ほたる", "hotaru" },
            { "natsume", "natsume" }, { "izawa natsume", "natsume" }, { "伊澤なつめ", "natsume" },
            { "nagi", "nagi" }, { "kotohira nagi", "nagi" }, { "琴平凪", "nagi" },
            { "mahoro", "mahoro" }, { "hamasaki mahoro", "mahoro" }, { "浜崎まほろ", "mahoro" },
            { "houka", "houka" }, { "izumi houka", "houka" }, { "和泉ほうか", "houka" },
        };

        // 從 prompt 中偵測提到哪些角色 key（去重、保序）
        public static List<string> DetectCharacterKeys(string prompt)
        {
            var found = new List<string>();
            var seen = new HashSet<string>();
            // 由長到短匹配，避免 "nagasaki soyo" 被 "soyo" 先截走
            foreach (var kv in NameToKey.OrderByDescending(x => x.Key.Length))
            {
                if (prompt.Contains(kv.Key, StringComparison.OrdinalIgnoreCase) && seen.Add(kv.Value))
                    found.Add(kv.Value);
            }
            return found;
        }

        // 用多張預存角色圖產圖（Soyo 優先排第一）
        public async Task<Stream> GenerateCharactersImageAsync(string prompt, List<string> characterKeys)
        {
            // 確保 soyo 在最前
            var ordered = characterKeys.Contains("soyo")
                ? new[] { "soyo" }.Concat(characterKeys.Where(k => k != "soyo")).ToList()
                : characterKeys;

            var images = new List<(byte[] bytes, string filename)>();
            foreach (var key in ordered)
            {
                if (!CharacterFiles.TryGetValue(key, out var filename)) continue;
                var path = Path.Combine(CharacterImagesDir, filename);
                if (!File.Exists(path)) continue;
                images.Add((await File.ReadAllBytesAsync(path), filename));
            }

            if (images.Count == 0)
                return await GenerateImageAsync(prompt);

            return await GenerateImageWithMultipleReferencesAsync(prompt, images)
                   ?? await GenerateImageAsync(prompt);
        }

        // 帶多張參考圖產圖
        public async Task<Stream> GenerateImageWithMultipleReferencesAsync(string prompt, List<(byte[] bytes, string filename)> images)
        {
            try
            {
                Console.WriteLine($"[AIImage] 多圖產圖 images={images.Count} prompt={prompt[..Math.Min(60, prompt.Length)]}");
                var form = CreateForm();
                form.Add(new StringContent(prompt), "prompt");
                for (int i = 0; i < images.Count; i++)
                {
                    var (bytes, fname) = images[i];
                    var ct = fname.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
                    var imgContent = new ByteArrayContent(bytes);
                    imgContent.Headers.ContentType = new MediaTypeHeaderValue(ct);
                    form.Add(imgContent, $"image_{i}", fname);
                }
                using var req = new HttpRequestMessage(HttpMethod.Post, CfWorkerUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CfApiKey);
                req.Content = form;
                using var response = await _httpClient.SendAsync(req);
                if (response.IsSuccessStatusCode)
                {
                    var bytes2 = await response.Content.ReadAsByteArrayAsync();
                    Console.WriteLine($"[AIImage] Worker(多圖) 成功 {bytes2.Length} bytes");
                    return new MemoryStream(bytes2);
                }
                var errBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[AIImage] Worker(多圖) error {(int)response.StatusCode}: {errBody[..Math.Min(200, errBody.Length)]}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIImage] Worker(多圖) 失敗: {ex.Message}");
                return null;
            }
        }

        // 用預存角色圖產圖（單張，保留相容）
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
            try
            {
                string encodedPrompt = WebUtility.UrlEncode(prompt);
                string url = $"https://image.pollinations.ai/prompt/{encodedPrompt}?model=flux";
                Console.WriteLine("[AIImage] Pollinations fallback");
                using var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[AIImage] Pollinations 失敗 {(int)response.StatusCode}");
                    return null;
                }
                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                return new MemoryStream(bytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIImage] Pollinations 例外: {ex.Message}");
                return null;
            }
        }
    }
}
