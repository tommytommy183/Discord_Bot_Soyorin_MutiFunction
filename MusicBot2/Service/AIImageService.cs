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
            { "soyo",    "girl, delicate oval face shape, pointed chin, soft jawline, gently arched eyebrows, small upturned nose, small lips, pale skin tone, light honey blonde hair, shoulder-length, wavy texture, side part, no bangs, one strand falls across forehead, light blue eyes, almond-shaped, pale blue irises with subtle lighter blue rings, round pupils, short natural eyelashes, no visible eyeliner, faint blush on cheeks, navy blue school uniform blazer with wide collar, white trim on collar, light gray neck ribbon tied in a knot, navy blue short-sleeved dress, knee-length A-line skirt, white stripes along hem of skirt, navy blue knee-high socks, brown simple shoes, anime illustration style, clean lines, soft shading."    },
            { "tomori",  "An anime illustration of a slender teenage girl with pale skin, a slim face with a defined chin, thin eyebrows, a small straight nose, and a neutral mouth expression. She has medium-large almond-shaped eyes with light amber-gold irises and thin eyelashes. Her hair is short, chin-length, textured with soft messy layers, featuring wispy bangs covering her forehead and slightly messy side strands, colored in a muted lavender-purple shade. She is wearing a school uniform consisting of a light lavender-gray tailored blazer with long sleeves, white cuffs visible at the wrists, lapels, and two small gold buttons fastening the front. Underneath the blazer, she wears a white collared dress shirt and a dark green striped necktie. Her skirt is a pleated plaid miniskirt featuring a pattern of dark green, black, and thin yellow-green stripes. She wears dark forest green knee-high socks and simple black school shoes. The art style features clean line art, smooth cell shading, and soft highlights."  },
            { "anon",    "An anime illustration of a slender young female character with a soft oval face, gentle jawline, and fair skin tone. She has large pale mint-green eyes with detailed irises and thin defined eyebrows. Her long, straight pastel pink hair features slight side-swept bangs framing her forehead and cascades down past her shoulders. She is wearing a school uniform consisting of a light lavender-gray tailored blazer with two front buttons and notched lapels, layered over a crisp white collared shirt with a dark green striped necktie. Her lower garment is a pleated plaid miniskirt in shades of dark green and black. She wears dark forest green knee-high socks and dark shoes. The art style features clean line art and smooth cel shading with soft highlights."    },
            { "rikki",   "A young woman with a slightly rounded face, a defined jawline, and a small chin. Her eyebrows are thin and arch gently. Her nose is small and straight. Her mouth is small with a slight pout. Her skin tone is fair. Her eyes are a deep, muted brown. The eye shape is almond with a subtle upward tilt at the outer corners. The irises are a solid dark color with no visible pupil detail. Her eyelashes are short and dark. Her hair is dark brown, with a medium length that falls past her shoulders. The hairstyle is straight with subtle waves, parted on the side. She has a soft fringe that sweeps across her forehead. Her body proportions are slender and feminine. She wears a light brown school uniform dress with a white Peter Pan collar trimmed in navy blue. The dress has a pleated skirt. A red ribbon bow with a gold emblem is centered at the neckline. Her sleeves are long and cuffed with navy blue trim. She wears dark navy knee-high socks. Her shoes are brown loafers. She has no visible accessories. The art style is clean anime illustration with soft shading."   },
            { "raana",   "An anime illustration of a slender young female character with a delicate, rounded face, small pointed chin, and fair pale skin. She has short, bob-length white-silver hair with straight blunt bangs resting above her eyebrows, soft side locks framing her cheeks, and subtle bluish undertones in the hair strands. Her eyes are large and expressive with heterochromia, featuring a warm golden-amber iris on her right eye and a cool muted lavender-blue iris on her left eye, framed by thin dark eyelashes and delicate thin eyebrows. She wears a casual layered outfit consisting of an oversized, off-the-shoulder dark charcoal gray distressed t-shirt with a heavily ripped and frayed hemline, revealing a longer white under-layer garment beneath. Underneath the short sleeves of the dark t-shirt, she wears long fitted white sleeves that cover her arms. She wears dark charcoal gray ankle boots with simple flat soles. Clean line art, soft anime cell shading, muted color palette."   },
            { "rana",   "An anime illustration of a slender young female character with a delicate, rounded face, small pointed chin, and fair pale skin. She has short, bob-length white-silver hair with straight blunt bangs resting above her eyebrows, soft side locks framing her cheeks, and subtle bluish undertones in the hair strands. Her eyes are large and expressive with heterochromia, featuring a warm golden-amber iris on her right eye and a cool muted lavender-blue iris on her left eye, framed by thin dark eyelashes and delicate thin eyebrows. She wears a casual layered outfit consisting of an oversized, off-the-shoulder dark charcoal gray distressed t-shirt with a heavily ripped and frayed hemline, revealing a longer white under-layer garment beneath. Underneath the short sleeves of the dark t-shirt, she wears long fitted white sleeves that cover her arms. She wears dark charcoal gray ankle boots with simple flat soles. Clean line art, soft anime cell shading, muted color palette."   },
            // Ave Mujica
            { "sakiko",  "An anime illustration of a slender young girl with pale skin, a delicate round face shape, a soft chin, thin defined eyebrows, a small simple nose, and a small mouth. She has bright amber orange eyes with detailed irises and long subtle eyelashes. Her hair is a pale pastel lavender-blue color, worn in long twin tails with two side locks framing her face, and a soft straight fringe with parted bangs across her forehead. She is wearing a cream-colored long-sleeved blouse with puffed sleeves, a rounded collar, and a small black bow tie at the neckline. Below the blouse, she wears a high-waisted mid-length A-line pleated skirt with a gray and white plaid pattern and a dark waistband. She wears white knee-high socks with pink cuffs and dark black Mary Jane shoes with straps. The overall color palette consists of pale lavender-blue, cream, gray, white, and black accents. The art style features clean line art and soft cell shading."  },
            { "mutsumi", "Anime illustration of a young female character with a soft, rounded face shape, delicate pointed chin, slender jawline, thin light eyebrows, small neat nose, and a small neutral mouth. She has pale skin with a soft complexion. Her eyes are medium-sized, almond-shaped, with pale sage-green irises, subtle dark pupils, fine delicate eyelashes, and a gentle downward gaze. Her hair is pale mint-green, straight, worn down with medium-long length reaching past her shoulders, featuring straight wispy bangs across the forehead and two longer side locks framing the face. She wears a dark purple-gray long-sleeved dress with a white plaid pattern, featuring a scoop neckline with a cream-colored bib inset decorated with a vertical row of small black buttons and delicate lace trim along the curved edge. A dark teal ribbon bow is fastened at the center of the collar. A thin black choker necklace is worn around her neck. Clean line art, soft cel shading, delicate digital anime character design." },
            { "uika",    "Anime illustration of a young female character with a soft rounded face shape, delicate pointed chin, and fair skin tone. Large expressive purple eyes with detailed irises, thin refined eyebrows, and a small neutral mouth. Straight shoulder-length light honey blonde hair with straight-cut bangs framing the forehead and longer side strands resting against the cheeks. The character is wearing a fitted plain white short-sleeved top with a subtle notch neckline and rolled cuffs, paired with a high-waisted muted light beige-gray midi skirt featuring visible belt loops and structured front pockets. A solid dark charcoal gray baseball cap is worn on the head, partially covering the top of the hair. Clean line art with soft cel shading and a simple minimalist color palette."    },
            { "umiri",   "A full-body anime illustration of a female character with pale skin, a slim build, and proportional height. Her face has a delicate, softly pointed chin, a straight narrow nose, a subtle mouth, and thin, neatly arched dark eyebrows. She has large, luminous light blue-gray eyes with defined eyelashes and a gentle gaze. Her hair is medium-length, dark charcoal gray with subtle purple undertones, styled in a layered bob with textured side-swept bangs framing her face. She wears a black leather biker jacket cropped at the waist with lapels and silver hardware accents, layered over a vibrant crimson red crop top. Her bottom attire consists of a fitted, high-waisted muted light gray mini skirt secured with a dark buckled belt. Her footwear includes tall black combat boots with thick platform soles and laces. Accessories include a thick bright pink choker necklace around her neck. The art style features clean cel-shading, smooth digital rendering, and sharp outlines."   },
            { "nyamu",   "Anime illustration character with a slender, youthful build and pale skin tone. Features a softly rounded face with a delicate chin and thin, light purple eyebrows. Large, expressive eyes with muted pink-violet irises, slender dark eyelashes, and soft highlights. Short, messy bob haircut in a dusty lavender-purple color with wispy bangs framing the forehead and short side locks curving around the cheeks. Wears a two-tone sleeveless dress featuring a dark charcoal gray fitted mini-dress base with an attached off-the-shoulder ruffled overlay in a muted gray-purple fabric across the upper chest and shoulders, held by thin spaghetti straps. Wears a thin black choker necklace around the neck. Footwear consists of simple black strappy high-heeled shoes. Clean line art with soft cel shading and a muted color palette."   },
            // Mugendai Mewtype
            { "arale",   "An anime illustration of a young female character with a soft-featured, rounded face, narrow chin, and fair skin tone. She has large, detailed violet-pink eyes with bright highlights, long dark eyelashes, thin curved eyebrows, and a small nose and mouth. Her hair is long, straight, and bright light-blonde with warm golden tones, featuring a side-swept fringe that covers her right eye and side locks framing her face, cascading down past her shoulders. She wears a muted teal-blue denim jacket layered over a white t-shirt, paired with a matching teal-blue denim skirt that reaches mid-thigh. She wears white crew socks and dark charcoal gray low-top canvas sneakers with white rubber soles. A small pink hair clip is visible in her bangs above her left eye. The art style is clean anime digital illustration with smooth cel shading and soft color gradients."   },
            { "nonoka",  "An anime illustration of a slender young girl with a soft, youthful face shape, rounded jawline, and fair skin tone. She has large, bright purple-blue eyes with detailed irises, gentle thin eyebrows, a small nose, and a cheerful open-mouthed expression showing teeth. Her hair is long, pale silver-lavender with a soft sheen, featuring side locks framing her face, wispy bangs across her forehead, and wavy strands flowing around her shoulders. She is wearing a soft pastel pink oversized cardigan layered over a simple white t-shirt, paired with light cream-yellow knee-length shorts. Clean line art with soft cel-shading and a vibrant character color palette featuring pastel pink, pale silver-lavender, cream-yellow, and white."  },
            { "ritsu",   "Anime illustration style, clean line art with soft shading, full-body portrait of a young person with a slim, slender build. Oval face with a softly pointed chin and delicate jawline, thin straight eyebrows, a small simple nose, and a neutral mouth expression. Pale skin tone. Large expressive light blue eyes with detailed irises and prominent eyelashes. Short, textured bright cyan-blue hair styled in a neat bob with short wispy bangs across the forehead and soft locks framing the sides of the face. Wearing a dark navy blue long-sleeved collared shirt layered underneath a sleeveless cream-white V-neck knit sweater vest with a small embroidered emblem on the chest. Paired with a short pleated skirt featuring a gradient color transition from beige at the top to a muted pink-orange at the hem. Wearing white calf-length socks and dark brown loafers. Character color palette consists of cyan-blue, navy blue, cream-white, beige, pink-orange, and dark brown."   },
            { "miyako",  "An anime style illustration of a female character with pale skin, a rounded chin, soft jawline, delicate eyebrows, a small nose, and a gentle mouth expression. She has deep violet eyes with detailed irises and fine eyelashes. Her hair is a vibrant medium purple, styled in a short bob with soft, voluminous waves, side-swept bangs framing her face, and curly side locks. She has a slender build and petite proportions. She is wearing a dark, elegant dress featuring a black fitted bodice with cold-shoulder cutouts, ruffled dark sleeves over violet under-sleeves, and a long, flowing dark purple-to-black gradient skirt that reaches her ankles. She wears simple dark shoes with ankle straps and white socks. The color palette centers around deep purples, black, and white accents. Clean line art with soft cel shading and a smooth anime rendering style."  },
            { "yuno",    "An anime illustration of a slender young female character with fair skin, a delicate oval face, small chin, and soft slim jawline. She has straight bright pink hair styled into a shoulder-length cut with wispy bangs framing her forehead and a small braided side lock tied with a dark band over her left shoulder, with a small cowlick strand sticking up on top. Her eyes are large and bright with a pinkish-red hue and light-colored irises, framed by thin dark eyelashes and thin curved eyebrows. She wears black round-framed glasses perched on the bridge of her nose. She is wearing an oversized hooded zip-up sweatshirt in a pale lavender-blue color that gradients to a deeper muted purple at the bottom hem and sleeve cuffs, featuring red drawstring cords and a small dark emblem on the left chest. Underneath, she wears a white-and-dark striped shirt collar peeking out. She wears a short pleated skirt with horizontal dark blue and white stripes and side tie strings. She has slender legs and wears dark gray crew socks and dark charcoal gray boots. The art style features clean line art, soft cel shading, and a pastel character color palette."    },
            // millsage
            { "hotaru",  "An anime illustration of a young female character with a soft rounded face shape, delicate narrow chin, slender jawline, thin light eyebrows, small neutral nose, and a simple small mouth with pale lips. She has very pale fair skin tone. Her eyes are large, wide, pale lavender-blue with detailed irises, long delicate eyelashes, and subtle highlights. Her hair is long, stark white, straight with soft texture, featuring straight-cut bangs across the forehead, framing side locks, and two small dark hair clips on the right side. She has a slender build and youthful proportions. She is wearing a high-collared white long-sleeved dress with vertical subtle pleats, puffed shoulders, ruffled cuffs, and a small bow at the neckline, layered with a dark charcoal gray shawl draped over her shoulders. Clean line art, soft cel shading, monochrome and pale blue color palette."  },
            { "natsume", "An anime illustration of a young woman with a soft oval face shape, delicate pointed chin, a straight narrow nose, thin light-colored eyebrows, and a small neutral mouth. She has striking pale violet eyes with detailed luminous irises, slender upper eyelashes, and faint blushing across her cheeks. Her hair is a muted light sage green color, styled in a shoulder-length wavy bob with wispy side strands framing her face and a side-parted fringe with lighter highlights on the bangs. She has a slender build and fair skin tone. She wears an open dark forest green plaid blazer with a white grid pattern, featuring notched lapels, flap pockets, and long sleeves over a simple plain white inner top. Layered around her neck are two delicate gold chain necklaces, one choker-style and one longer chain featuring a small circular pendant. She wears dark slate gray trousers held up by a thin black belt with a gold buckle. The overall color palette consists of muted sage green, dark forest green, white, pale violet, and subtle gold accents. The art style is clean anime illustration with soft cel-shading and crisp line art." },
            { "nagi",    "An anime illustration of a young woman with a slender build and fair skin tone. Her face features a soft, rounded chin, a delicate straight nose, a thin neutral mouth, and thin dark brown eyebrows. Her eyes are light olive green with detailed irises, gentle dark eyelashes, and a calm, tired gaze. Her hair is light grayish-brown, worn straight with medium length reaching down her chest, featuring wispy straight bangs across her forehead and long side locks framing her face. She is wearing a muted gray off-the-shoulder crop top with long sleeves featuring criss-cross lace-up ribbons down the outer arms and a gathered front bodice. She wears high-waisted dark charcoal gray pants secured with a metallic-buckled belt featuring a small heart accent. Her accessories include a layered neckwear set consisting of thin black choker bands and silver chain necklaces with small pendants. The rendering style is clean line art with soft cel shading and a muted color palette."    },
            { "mahoro",  "A young female character with a soft rounded face shape, delicate narrow jawline, and fair skin tone. She has bright light blue eyes with detailed irises, gentle soft-arched thin eyebrows, a small straight nose, and a neutral mouth. Her hair is short bob-length, colored in muted lavender-blue, featuring straight choppy bangs across the forehead and short side locks framing the cheeks, adorned with small white hair clips on one side. She wears a white zip-up cropped hoodie jacket, layered over a dark charcoal gray fitted turtleneck shirt featuring thin horizontal stripes and a small white bow ornament near the collar, paired with a pale sage green pleated midi skirt. The art style is a clean anime illustration with soft cel shading and delicate line art."  },
            { "houka",   "An anime illustration of a female character with a soft, oval face shape, delicate pointed chin, thin light brown eyebrows, a small minimalist nose, and a gentle closed-mouth smile. She has light pink eyes with soft greyish-pink irises, thin dark upper eyelashes, and a calm, gentle gaze. Her hair is pastel pink, straight, with chest-length strands framing her face and delicate side locks, featuring straight-cut wispy bangs across her forehead. She wears a black headband with a small black bow accent on the right side, and small black bow hair clips on her left side locks. Her skin tone is pale and smooth with a soft matte finish. She wears a dark mauve-red fitted turtleneck top under an off-white tweed blazer with a subtle houndstooth pattern, notched lapels, and a single prominent button, paired with a matching off-white pleated skirt held by a thin black belt with a golden clasp. A multi-strand white pearl necklace rests closely around her high neckline. The art style features clean line art, soft cel shading, and a pastel color palette dominated by pale pink, off-white, and dark mauve."   },

            //Other
            { "viola", "Anime illustration of a young female character with a soft oval face, gentle pointed chin, slender jawline, thin dark eyebrows, a small delicate nose, and a neutral mouth. Her eyes are dark purple with rounded almond shapes, defined irises, dark pupils, and fine eyelashes. Her hair is dark forest green, styled in long straight locks reaching past her shoulders, featuring full straight-cut bangs across her forehead and a small rounded hair bun fastened on the left side of her head with loose stray strands curving outward. Her skin tone is pale ivory with light pink undertones. Her body build is slender and youthful with standard human proportions. She is wearing a dark charcoal gray uniform jacket with a high zipped collar, featuring a contrasting pale cream-colored yoke panel across the upper chest and shoulders outlined with thin reddish-orange piping, and long fitted sleeves. She wears a matching dark charcoal gray pleated mini skirt with a high waist, matching hem trim, and visible structured vertical panels. Her legs are bare, and she wears dark charcoal gray crew socks and pale grayish-green loafers. The rendering style is clean line art with soft cel shading and a muted color palette." },
            { "nori","An anime illustration portrait of a young female character with a rounded face shape, soft chin, and fair skin tone. She has large, warm amber-gold eyes with detailed irises, prominent eyelashes, thin dark eyebrows, and a small nose and mouth. Her hair is bright golden yellow, styled into thick braided pigtails on the sides with short straight bangs framing her forehead, a single curved ahoge strand sticking up from the top, and loose side locks. On her left cheek, she has distinctive small facial markings consisting of a pink heart, a white heart, and a dark heart shape. She wears a dark charcoal gray garment with a white collar and a dark ribbon bow at the neck. Her hair is adorned with a yellow headband and a dark brown bear-shaped hair clip with an oval yellow accent underneath. The art style features clean dark line art, minimal soft shading, and a bright character color palette dominated by warm golden yellow, charcoal gray, and soft pink flush on the cheeks."},
            { "fibi","Chibi animated character, super-deformed proportions with a large rounded head and small body, smooth pale skin tone. Round chibi face with simple dot features, wide open joyful mouth showing teeth, large expressive round purple eyes with glossy highlights and dark outlines, short blunt-cut blonde bangs with side locks framing the face. Long light blonde hair extending down the sides. Wearing a wide-brimmed white hat with a dark band and a small blue cross-shaped hair clip on the brim. Dressed in a short white tunic with long sleeves featuring dark cuffs, layered with a dark vest, a bright blue scarf or ribbon draped across the chest, and white thigh-high stockings or boots with dark horizontal stripes. Bold cartoon line art style with flat cel shading and thick dark outlines."},
            { "nuonuo","Anime illustration of a chibi-style female character with pale skin and a rounded, simplified face shape, featuring thin eyebrows, a tiny dot nose, and a straight, simple mouth line. She has heavy-lidded, sleepy-looking eyes with pale blue irises and dark pupils, framed by soft eyelashes. Her hair is a pale mint-green or soft sage-green color, styled in a bob with straight, blunt-cut bangs across the forehead and shoulder-length wavy locks framing her face. She wears a white top featuring red ribbon accents at the neckline and a prominent red bow at the chest, paired with red arm sleeves wrapped with lighter reddish bands. The art style features clean line art and simple, flat cel shading with soft pink blush spots on her cheeks."},
            { "fishbone","Anime illustration character design, slim young female proportions with fair skin tone, delicate soft facial features, rounded jawline, small gentle mouth, thin defined eyebrows. Large expressive light blue-gray eyes with detailed irises, long delicate dark eyelashes. Light honey blonde hair, shoulder-length with soft layered texture, straight wispy bangs framing the forehead, longer side locks framing the face. Wearing an oversized slouchy ribbed turtleneck sweater in muted lavender-purple with dropped shoulders, paired with a dark charcoal gray pleated mini skirt, opaque black tights, and brown leather loafers. A small decorative hair clip secures hair on the left side. Soft anime art style with clean line art and smooth cel shading."},
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
