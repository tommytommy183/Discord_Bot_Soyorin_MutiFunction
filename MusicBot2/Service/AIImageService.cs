using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
            { "rana",   "raana.png"   },
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

            //Other
            { "viola", "viola.png" },
            { "nori","nori.png"},
            { "fibi","fibi.png"},
            { "nuonuo","nuonuo.png"},
            { "fishbone","fishbone.png"},
        };

        // 角色 key → 英文外觀描述（fallback 純文字產圖用）
        public static readonly Dictionary<string, string> CharacterVisuals = new()
        {
            // MyGO!!!!!
            { "soyo",    "Nagasaki Soyo, anime girl, long straight light brown hair, gentle elegant smile, soft eyes, BanG Dream MyGO character" },
            { "tomori",  "Takamatsu Tomori, anime girl, short dark brown hair, red eyes, BanG Dream MyGO character" },
            { "anon",    "Chihaya Anon, anime girl, pink hair, energetic cute expression, BanG Dream MyGO character" },
            { "rikki",   "Shiina Rikki, anime girl, short dark hair, cool expression, BanG Dream MyGO character" },
            { "raana",   "Yoyogi Raana, anime girl, long dark hair, mysterious expression, BanG Dream MyGO character" },
            // Ave Mujica
            { "sakiko",  "Togawa Sakiko, anime girl, long dark hair, elegant serious expression, BanG Dream Ave Mujica character" },
            { "mutsumi", "Wakaba Mutsumi, anime girl, short light hair, gentle smile, BanG Dream Ave Mujica character" },
            { "uika",    "Misumi Uika, anime girl, long pink-white hair, cheerful expression, BanG Dream Ave Mujica character" },
            { "umiri",   "Yahata Umiri, anime girl, short dark hair, calm expression, BanG Dream Ave Mujica character" },
            { "nyamu",   "Yuutenji Nyamu, anime girl, twin-tail hair, playful expression, BanG Dream Ave Mujica character" },
        };

        // 角色 key → 中文名（讓 Soyo 系統提示知道）
        public static readonly Dictionary<string, string> CharacterNames = new()
        {
            // MyGO!!!!!
            { "soyo",    "長崎そよ"  },
            { "tomori",  "高松燈"    },
            { "anon",    "千早愛音"  },
            { "rikki",   "椎名立希"  },
            { "raana",   "要楽奈"    },

            // Ave Mujica
            { "sakiko",  "豊川祥子"  },
            { "mutsumi", "若葉睦"    },
            { "uika",    "三角初華"  },
            { "umiri",   "八幡海鈴"  },
            { "nyamu",   "祐天寺にゃむ" },

            // Mugendai Mewtype
            { "arale",   "仲町あられ" },
            { "nonoka",  "宮永ののか" },
            { "ritsu",   "峰月律"    },
            { "miyako",  "藤都子"    },
            { "yuno",    "千石ユノ"  },

            // millsage
            { "hotaru",  "汐見蛍"    },
            { "natsume", "伊沢なつめ" },
            { "nagi",    "琴平凪"    },
            { "mahoro",  "浜崎まほろ" },
            { "houka",   "和泉朋花"  },

            //Other
            { "viola", "薇歐拉" },
            { "nori","海苔"},
            { "fibi","菲比"},
            { "nuonuo", "糯糯" },
            { "fishbone", "魚骨頭" },
        };

        public AIImageService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        }

        // 手動組 raw multipart bytes，完全繞過 .NET MultipartFormDataContent 的 boundary 引號問題
        private static (ByteArrayContent body, string contentType) BuildRawMultipart(
            string prompt, List<(byte[] bytes, string filename, string mimeType)> images = null)
        {
            var boundary = "----CFBoundary" + Guid.NewGuid().ToString("N");
            var nl = "\r\n";
            var bodyBytes = new List<byte>();

            void AppendText(string s) => bodyBytes.AddRange(Encoding.UTF8.GetBytes(s));
            void AppendBytes(byte[] b) => bodyBytes.AddRange(b);

            // prompt field
            AppendText($"--{boundary}{nl}");
            AppendText($"Content-Disposition: form-data; name=\"prompt\"{nl}{nl}");
            AppendText(prompt);
            AppendText(nl);

            // image fields
            if (images != null)
            {
                for (int i = 0; i < images.Count; i++)
                {
                    var (imgBytes, fname, mime) = images[i];
                    var fieldName = images.Count == 1 ? "image" : $"image_{i}";
                    AppendText($"--{boundary}{nl}");
                    AppendText($"Content-Disposition: form-data; name=\"{fieldName}\"; filename=\"{fname}\"{nl}");
                    AppendText($"Content-Type: {mime}{nl}{nl}");
                    AppendBytes(imgBytes);
                    AppendText(nl);
                }
            }

            AppendText($"--{boundary}--{nl}");

            var content = new ByteArrayContent(bodyBytes.ToArray());
            return (content, $"multipart/form-data; boundary={boundary}");
        }

        private async Task<Stream> PostToWorkerAsync(string prompt, List<(byte[] bytes, string filename, string mimeType)> images = null)
        {
            // 有圖時加安全關鍵字，降低 Cloudflare AI content filter 誤判機率
            if (images != null && images.Count > 0 && !prompt.Contains("safe for work", StringComparison.OrdinalIgnoreCase))
                prompt += ", safe for work, wholesome, family friendly, sfw, non-explicit";

            var (body, ct) = BuildRawMultipart(prompt, images);
            body.Headers.TryAddWithoutValidation("Content-Type", ct);
            using var req = new HttpRequestMessage(HttpMethod.Post, CfWorkerUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CfApiKey);
            req.Content = body;
            using var response = await _httpClient.SendAsync(req);
            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                Console.WriteLine($"[AIImage] Worker 成功 {bytes.Length} bytes");
                return new MemoryStream(bytes);
            }
            var err = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[AIImage] Worker error {(int)response.StatusCode}: {err[..Math.Min(200, err.Length)]}");
            return null;
        }

        // 純文字產圖
        public async Task<Stream> GenerateImageAsync(string prompt)
        {
            try
            {
                Console.WriteLine($"[AIImage] 純文字產圖 prompt={prompt[..Math.Min(80, prompt.Length)]}");
                var stream = await PostToWorkerAsync(prompt);
                if (stream != null) return stream;
                Console.WriteLine("[AIImage] Worker 失敗，fallback Pollinations");
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

        // 帶單張參考圖片產圖（byte[]）
        public async Task<Stream> GenerateImageWithReferenceAsync(string prompt, byte[] imageBytes, string filename = "reference.png", string contentType = "image/png")
        {
            try
            {
                Console.WriteLine($"[AIImage] 帶圖產圖 imageSize={imageBytes.Length} prompt={prompt[..Math.Min(60, prompt.Length)]}");
                var images = new List<(byte[], string, string)> { (imageBytes, filename, contentType) };
                return await PostToWorkerAsync(prompt, images);
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
            { "soyo", "soyo" }, { "nagasaki soyo", "soyo" }, { "長崎そよ", "soyo" }, { "そよ", "soyo" },
            { "tomori", "tomori" }, { "takamatsu tomori", "tomori" }, { "高松燈", "tomori" }, { "燈", "tomori" },
            { "anon", "anon" }, { "chihaya anon", "anon" }, { "千早愛音", "anon" }, { "愛音", "anon" },
            { "rikki", "rikki" }, { "shiina rikki", "rikki" }, { "椎名立希", "rikki" }, { "立希", "rikki" },
            { "raana", "raana" }, { "yoyogi raana", "raana" }, { "要楽奈", "raana" }, { "楽奈", "raana" },
            { "rana", "raana" },

            // Ave Mujica
            { "sakiko", "sakiko" }, { "togawa sakiko", "sakiko" }, { "豊川祥子", "sakiko" }, { "祥子", "sakiko" },
            { "mutsumi", "mutsumi" }, { "wakaba mutsumi", "mutsumi" }, { "若葉睦", "mutsumi" }, { "睦", "mutsumi" },
            { "uika", "uika" }, { "misumi uika", "uika" }, { "三角初華", "uika" }, { "初華", "uika" },
            { "umiri", "umiri" }, { "yahata umiri", "umiri" }, { "八幡海鈴", "umiri" }, { "海鈴", "umiri" },
            { "nyamu", "nyamu" }, { "yuutenji nyamu", "nyamu" }, { "祐天寺にゃむ", "nyamu" }, { "にゃむ", "nyamu" },

            // Mugendai Mewtype
            { "arale", "arale" }, { "nakamachi arale", "arale" }, { "仲町あられ", "arale" }, { "あられ", "arale" },
            { "nonoka", "nonoka" }, { "miyanaga nonoka", "nonoka" }, { "宮永ののか", "nonoka" }, { "ののか", "nonoka" },
            { "ritsu", "ritsu" }, { "minetsuki ritsu", "ritsu" }, { "峰月律", "ritsu" }, { "律", "ritsu" },
            { "miyako", "miyako" }, { "fuji miyako", "miyako" }, { "藤都子", "miyako" }, { "都子", "miyako" },
            { "yuno", "yuno" }, { "sengoku yuno", "yuno" }, { "千石ユノ", "yuno" }, { "ユノ", "yuno" },

            // millsage
            { "hotaru", "hotaru" }, { "shiomi hotaru", "hotaru" }, { "汐見蛍", "hotaru" }, { "蛍", "hotaru" },
            { "natsume", "natsume" }, { "izawa natsume", "natsume" }, { "伊沢なつめ", "natsume" }, { "なつめ", "natsume" },
            { "nagi", "nagi" }, { "kotohira nagi", "nagi" }, { "琴平凪", "nagi" }, { "凪", "nagi" },
            { "mahoro", "mahoro" }, { "hamasaki mahoro", "mahoro" }, { "浜崎まほろ", "mahoro" }, { "まほろ", "mahoro" },
            { "houka", "houka" }, { "izumi houka", "houka" }, { "和泉朋花", "houka" }, { "朋花", "houka" },


            //other
            { "viola", "viola" },{ "圍毆拉", "viola" },{ "薇歐拉", "viola" },
            { "nori","nori"},{ "海苔", "nori" },
            { "菲比","fibi"},{ "fibi", "fibi" },
            { "糯糯","nuonuo"},{ "nuonuo", "nuonuo" },
            { "魚骨頭","fishbone"},{ "fishbone", "fishbone" },{ "fish_bone", "fishbone" },{ "fish bone", "fishbone" },
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

            // fallback 時加角色外觀描述，讓純文字也能畫出正確角色
            var visuals = ordered
                .Where(k => CharacterVisuals.ContainsKey(k))
                .Select(k => CharacterVisuals[k]);
            var enrichedPrompt = visuals.Any()
                ? $"{prompt}, featuring {string.Join(" and ", visuals)}"
                : prompt;

            if (images.Count == 0)
                return await GenerateImageAsync(enrichedPrompt);

            return await GenerateImageWithMultipleReferencesAsync(prompt, images)
                   ?? await GenerateImageAsync(enrichedPrompt);
        }

        // 帶多張參考圖產圖
        public async Task<Stream> GenerateImageWithMultipleReferencesAsync(string prompt, List<(byte[] bytes, string filename)> images)
        {
            try
            {
                Console.WriteLine($"[AIImage] 多圖產圖 images={images.Count} prompt={prompt[..Math.Min(60, prompt.Length)]}");
                var imgList = images.Select(img =>
                {
                    var mime = img.filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
                    return (img.bytes, img.filename, mime);
                }).ToList();
                return await PostToWorkerAsync(prompt, imgList);
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
