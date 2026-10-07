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
        // 格式：髮色髮型 → 眼睛 → 核心服裝，控制在 150~200 字以內
        public static readonly Dictionary<string, string> CharacterVisuals = new()
        {
            // MyGO!!!!!
            { "soyo",    "light honey blonde wavy shoulder-length hair no bangs with one strand over forehead, pale blue almond eyes, navy school uniform blazer with white collar trim and gray neck ribbon, navy A-line knee skirt with white hem stripes, navy knee socks, anime style" },
            { "tomori",  "short chin-length messy lavender-purple hair with wispy bangs, amber-gold almond eyes, lavender-gray school blazer over white shirt and dark green striped necktie, dark green plaid pleated miniskirt, dark green knee socks, anime style" },
            { "anon",    "long straight pastel pink hair with light side-swept bangs, large pale mint-green eyes, lavender-gray school blazer over white shirt and dark green striped necktie, dark green and black plaid pleated miniskirt, dark green knee socks, anime style" },
            { "rikki",   "dark brown shoulder-length straight hair with soft side fringe, deep muted brown eyes, light brown school uniform dress with navy Peter Pan collar, red ribbon bow with gold emblem at neckline, long navy-cuffed sleeves, navy knee socks, anime style" },
            { "raana",   "short white-silver bob with straight blunt bangs and bluish undertones, heterochromia right eye warm amber and left eye cool lavender-blue, oversized dark gray distressed off-shoulder t-shirt over long white sleeves underneath, gray ankle boots, anime style" },
            { "rana",    "short white-silver bob with straight blunt bangs and bluish undertones, heterochromia right eye warm amber and left eye cool lavender-blue, oversized dark gray distressed off-shoulder t-shirt over long white sleeves underneath, gray ankle boots, anime style" },
            // Ave Mujica
            { "sakiko",  "pale lavender-blue hair in long twin tails with side locks and soft straight parted bangs, bright amber orange eyes, cream puffed-sleeve blouse with black bow tie at neckline, gray and white plaid high-waisted A-line pleated skirt, white knee socks with pink cuffs, anime style" },
            { "mutsumi", "pale mint-green long straight hair with wispy bangs and long side locks, sage-green almond eyes, dark purple-gray dress with cream bib inset of black buttons and lace trim, teal ribbon bow at collar, black choker necklace, anime style" },
            { "uika",    "straight honey blonde shoulder-length hair with straight-cut bangs and longer side strands, large expressive purple eyes, plain white short-sleeved top, high-waisted muted beige-gray midi skirt with belt loops and pockets, dark gray baseball cap, anime style" },
            { "umiri",   "dark charcoal gray layered bob with subtle purple undertones and textured side-swept bangs, light blue-gray luminous eyes, black cropped leather biker jacket over vibrant crimson red crop top, light gray high-waisted mini skirt, bright pink choker necklace, black combat platform boots, anime style" },
            { "nyamu",   "dusty lavender-purple short messy bob with wispy bangs and short side locks, muted pink-violet eyes, dark charcoal gray sleeveless mini dress with gray-purple ruffled off-shoulder overlay on spaghetti straps, black choker necklace, black strappy heels, anime style" },
            // Mugendai Mewtype
            { "arale",   "long bright light-blonde straight hair with side-swept fringe covering right eye and side locks framing face, large violet-pink eyes, muted teal-blue denim jacket over white t-shirt, matching teal-blue denim mid-thigh skirt, small pink hair clip above left eye, anime style" },
            { "nonoka",  "long pale silver-lavender wavy hair with wispy bangs and side locks, large bright purple-blue eyes and cheerful open expression, oversized soft pastel pink cardigan over white t-shirt, light cream-yellow knee-length shorts, anime style" },
            { "ritsu",   "short bright cyan-blue neat bob with short wispy bangs, large expressive light blue eyes, dark navy long-sleeved collared shirt under sleeveless cream V-neck knit sweater vest with small emblem, short beige-to-pink gradient pleated skirt, white calf socks, dark brown loafers, anime style" },
            { "miyako",  "medium vibrant purple short bob with soft voluminous waves and side-swept bangs with curly side locks, deep violet eyes, dark elegant dress with black cold-shoulder bodice and ruffled dark sleeves over violet under-sleeves, long dark purple-to-black gradient ankle skirt, anime style" },
            { "yuno",    "straight bright pink shoulder-length hair with wispy bangs and small left-side braid tied dark and cowlick ahoge on top, pinkish-red large eyes, black round-framed glasses, oversized pale lavender-blue-to-purple gradient zip-up hoodie with red drawstrings, dark blue and white striped short pleated skirt, anime style" },
            // millsage
            { "hotaru",  "long stark white straight hair with straight-cut bangs and small dark hair clips on right side, large pale lavender-blue wide eyes, white high-collared long-sleeved pleated dress with puffed shoulders and ruffled cuffs, dark charcoal gray shawl draped over shoulders, anime style" },
            { "natsume", "muted light sage green shoulder-length wavy bob with side-parted fringe and wispy side strands, striking pale violet eyes with faint blush, dark forest green plaid blazer with white grid over white inner top, gold choker-style and longer pendant chain necklaces, dark slate gray trousers, anime style" },
            { "nagi",    "light grayish-brown long straight hair with wispy straight bangs and long framing side locks, light olive green calm eyes, muted gray off-shoulder long-sleeved crop top with criss-cross lace-up ribbons on outer arms, high-waisted dark charcoal gray pants with heart-clasp metallic belt, silver chain necklaces, anime style" },
            { "mahoro",  "short muted lavender-blue choppy bob with straight bangs and small white hair clips on one side, bright light blue eyes, white zip-up cropped hoodie jacket over dark gray horizontal-striped fitted turtleneck with small white bow at collar, pale sage green pleated midi skirt, anime style" },
            { "houka",   "pastel pink long straight hair with straight wispy bangs, black bow headband on right and black bow hair clips on left side locks, light pink gentle eyes, off-white tweed houndstooth blazer over dark mauve fitted turtleneck, matching off-white pleated skirt with thin black belt and gold clasp, multi-strand white pearl necklace, anime style" },
            // Other
            { "viola",   "long dark forest green straight hair with full straight-cut bangs and small rounded hair bun on left side of head, dark purple almond eyes, dark charcoal gray high-zip-collar uniform jacket with pale cream yoke panel and reddish-orange piping trim, dark gray pleated high-waisted mini skirt, gray crew socks, pale grayish-green loafers, anime style" },
            { "nori",    "bright golden yellow hair in thick braided side pigtails with short straight bangs and single curved ahoge sticking up, large warm amber-gold eyes, three small stacked heart marks on left cheek (pink, white, dark), dark charcoal gray uniform top with white collar and dark ribbon bow at neck, yellow headband, brown bear-shaped hair clip, anime style" },
            { "fibi",    "chibi super-deformed proportions with large round head, long light blonde hair with blunt-cut short bangs and side locks framing face, large round expressive purple eyes with glossy highlights, wide-brimmed white hat with dark band and blue cross hair clip, white long-sleeve tunic under dark vest, bright blue scarf across chest, white thigh-high stockings with dark horizontal stripes, anime chibi style" },
            { "nuonuo",  "pale mint-green bob with straight blunt-cut bangs and shoulder-length wavy side locks, heavy-lidded sleepy pale blue eyes with soft pink blush on cheeks, white top with red ribbon accents at neckline and prominent large red bow at chest, red arm sleeves with lighter reddish bands, soft chibi anime style" },
            { "fishbone","light honey blonde shoulder-length layered hair with soft texture and straight wispy bangs, large expressive light blue-gray eyes with long delicate eyelashes, oversized slouchy muted lavender-purple ribbed turtleneck sweater with dropped shoulders, dark charcoal gray pleated mini skirt, opaque black tights, brown leather loafers, small hair clip on left side, anime style" },
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

            // fallback 時加角色外觀描述
            // 控制每個角色最多 120 字，避免多角色造成 prompt 爆長
            var visuals = ordered
                .Where(k => CharacterVisuals.ContainsKey(k))
                .Select(k =>
                {
                    var visual = CharacterVisuals[k].Trim();

                    if (visual.Length > 300)
                        visual = visual[..350];

                    return visual;
                })
                .ToList();

            var enrichedPrompt = visuals.Any()
                ? $"{prompt}, featuring {string.Join(", ", visuals)}"
                : prompt;

            // 最終保護，不要讓 fallback prompt 過長
            if (enrichedPrompt.Length > 1800)
                enrichedPrompt = enrichedPrompt[..1800];

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
