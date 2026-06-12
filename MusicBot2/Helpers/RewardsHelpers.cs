using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MusicBot2.Helpers
{
    public static class RewardsHelpers
    {
        //後續可以考慮直接下載音檔，然後撥放就好
        private static Dictionary<int, string> videoCompareList = new Dictionary<int, string>
        {
            {1 , "https://www.youtube.com/watch?v=2raPO16GHdI" },
            {2 , "https://www.youtube.com/watch?v=EPO7f7hMnOk" },
            {9 ,"https://www.youtube.com/watch?v=ZMN9TFioGyg"},
            {23 ,"https://www.youtube.com/watch?v=RexyoaXaQ1o"},
            {24 ,"https://www.youtube.com/watch?v=RexyoaXaQ1o"},
            {25 ,"https://www.youtube.com/watch?v=v-dKrhA6_i0"},
            {29 ,"https://www.youtube.com/watch?v=2raPO16GHdI"},
            {30 ,"https://www.youtube.com/watch?v=Q-Y-C_S5HFY"},
            {31 ,"https://www.youtube.com/watch?v=EPO7f7hMnOk"}
        };
         
        private static Dictionary<int, string> rewardsCompareList = new Dictionary<int, string>
            {
                { 1, "一首戰神阿基里斯" },
                { 2, "和葳葳夢夢一起去日本" },
                { 3, "去偷一台停在地下室還沒鎖的腳踏車" },
                { 4, "去花蓮找綠開心" },
                { 5, "被小魚告上法庭" },
                { 6, "一把雙刀流柔伊" },
                { 7, "和初華睡一晚" },
                { 8, "被傻屌愛音摳" },
                { 9, "一首夏夜晚風 芒果醬ver." },
                { 10, "玩一小時 furry shades of gay" },
                { 11, "沒有墨水存檔，直接重頭開始" },
                { 12, "一份免費的discord硝基" },
                { 13, "一張陳俊佑的一日份屁眼使用卷" },
                { 14, "一張劉宗漢的一日份屁眼使用卷" },
                { 15, "一張陳偉鵬的一日份屁眼使用卷" },
                { 16, "一張吳霈辰的一日份屁眼使用卷" },
                { 17, "和外星人一起遨遊桃子腳" },
                { 18, "和王議員一起參選" },
                { 19, "邊洗澡邊尿尿" },
                { 20, "和柯比勞大一起搭直升機" },
                { 21, "和買狗成員一起演唱" },
                { 22, "一把吉祥之刃" },
                { 23, "\n KILL KISS judy😩🎭😩\r\nKILL KISS judu\U0001f92a\U0001f952\U0001f92a\r\nKILL KISS juda🎠😨🎠\r\n瞞天過海\U0001fa9e👋🏻👿\r\nKILL KISS judy\U0001f941\U0001f92c\U0001f941\r\nKILL KISS judu😡🙃😡\r\nKILL KISS juda🎹🐙🎹\r\n緊抱入懷\r\n🎠🎠🎠\r\n🎠🐙🎠\r\n🎠🎠🎠" },
                { 24, "\n是殺戮之吻🎠🎪🎠❗❗❗\r\n擺弄下流淌\U0001f92e出的無聲\U0001f92b之音🎶\r\n已捨棄🗑️一切名字\U0001faaa\r\n你的模糊身影👥正在哭泣😢\r\ncan not🚫can not🚫not❌ not⛔ not🈲 deny😔↔️\r\n毫無疑問🙂‍↕️的真實\U0001fa7b\r\n來吧🤗 委身於我\U0001f934\r\n有如回歸初始🐒般\r\n能量🔋正在循環🔄\r\n沒錯 此刻重力⤵️不復存在🈚\r\n象徵性🐘的遊行🚩🙍‍♂️🙍‍♀️🙍‍♂️🙎‍♂️🙎‍♀️🙎\r\n在這月夜🌛抬頭仰望🙄吧\r\n'completeness🈵'\r\n啊 高舉🙋‍♂️生命❣️之燈火🏮\r\n殺戮🔪之吻💋 招弟👶\r\n殺戮🔪之吻💋 豬肚🐷\r\n殺戮🔪之吻💋 揪打🐣\r\n偽裝\U0001f977欺騙🍕🍕🍕🍕🍕🍕🍕\r\n奪命☠️之吻👄 招弟👼\r\n奪命☠️之吻👄 豬肚🐖\r\n奪命☠️之吻👄 揪打🐥\r\n緊👮‍♂️緊👮‍♂️擁抱\U0001fac2\r\n吶\U0001f9c2 還真是毫無防備\U0001faa4啊\r\n在這美麗💅的遊戲🎮中\r\n人們👤開始逐漸崩壞\U0001f92a\r\n多麼可笑啊\U0001f92d" },
                { 25, "\nAhh 你過去羞辱我的旋律🎶\r\n至今仍在狂怒咆哮著嗎🗣️\r\n戰慄的大聖堂✝️ 抗拒的心臟\U0001fac0\r\n燃燒殆盡🔥 沒有後髮\U0001f9d1‍\U0001f9b2的幸運女神🗽\r\nStill alive🎶 生命的齒輪⚙️\r\nSo, still alive 開始轉動💃\r\n掙脫束縛⛓️‍💥 踏上自己的道路🛣️\r\nStill alive? 邏各斯的誘惑😘\r\nSo, still alive? 仍然看不見🙈\r\n被玷污的異教徒們的眼淚😢" },
                { 26, "\n💣💥會臭芒果醬「難聽像垃圾」\U0001f92e⚡️\r\n拜託～～這種人一開口就露餡\U0001f921💀\r\n根！本！不！懂！音！樂！🎧💢💢\r\n🎶聽音樂不是拿來裝酷的😎💅💄\r\n也不是拿來炫耀「自己多有品味」💎✨🙄\r\n你那套假清高\U0001f922\U0001f922\U0001f922\r\n一看就知道只是在刷存在感🔥🔥🔥\r\n別再玩那種國小圈圈👶👭👬👫💢\r\n以為自己懂幾個冷門團就能封神？👑\U0001f923\r\n拜託⚡️這年頭誰還在搞那套內圈同溫層\U0001fae7\U0001fae7\U0001fae7\r\n真的超！可！笑！💀💀💀\r\n🎸音樂是靈魂的共鳴🌈💫不是你拿來裝懂的教材📚\U0001f928\r\n不是用來排擠別人的門票🚷💥\r\n聽音樂不是比懂❌\r\n是比誰能「真誠感受」💖💥💥💥\r\n結果你呢？💀\r\n聽幾首歌就以為自己是評論家\U0001f9d0📢\r\n每天噴來噴去💣💣💣\r\n嘴比節奏還快⚡️💋💨\r\n根本噪音製造機📢🚫🌀\r\n💥⚡️音樂無分貴賤💯💯💯\r\n真正懂音樂的人🎶✨\r\n不會拿愛好當武器🔪\U0001fa78\r\n也不會拿別人的喜歡開地圖砲💣🔥\r\n🙌請你離開那個假掰同溫層🌡️💢\r\n那種「只愛自己懂的」世界🌍\r\n早就發霉長菇🍄💀💀\r\n臭到宇宙外太空🌌🚀\U0001f92f💨\r\n💀💥音樂是溫度🔥\r\n是自由🌈\r\n是人和人之間的共鳴💫\r\n不是你打卡用的炫耀牆📱💣\r\n🎧懂不懂音樂沒差🎵\r\n但別再拿無知當態度⚡️\r\n你的嘴太吵💢💢💢\r\n把旋律都掩蓋了💀🎶🔥\r\n最後送你一句👇\r\n\U0001fa78「懂音樂的人會共鳴，不懂的人只會嘴。」💬💥\r\n聽不懂也別硬裝懂🙏💣\r\n音樂會記得誰是真心的💖✨" },
                { 27,"\n嗨哈囉哈囉👋👋我都還沒講什麼（發出氣喘笑聲（ㄜㄏ~ㄜ哈哈哈）🤓🤓你可以不要笑那麼開心嗎\U0001f923\U0001f923我剛看到你也覺得你還蠻可愛的😍😍 覺得 想認識你一下\U0001f975\U0001f975\r\n我叫Edward 你怎麼稱呼\U0001f92a\U0001f92a\r\n呃%$@¥#^🗣️🗣️ 你是台灣人嗎😵‍💫😵‍💫 我就覺得你有個口音在😎😎 就是不太像道地的台灣腔的這樣子一個感覺😉😉 法國！？🇫🇷🇫🇷好特別😁😁可是我覺得你中文好像蠻厲害的👍🏻👍🏻 那這樣子運氣很好我就遇到你了\U0001f924\U0001f924*發出氣喘笑聲（ㄜㄏ~ㄜ哈哈哈哈哈哈\r\n那邊剛好有個酒吧🍹🍹 我請你喝一杯好不好😳😳 好那我們走吧👫👫\r\n*再次發出氣喘笑聲（ㄜㄏ~ㄜ哈哈哈哈哈哈"},
                { 28,"\n如果你愛我，你就會隨時回訊息💔💔如果你在乎我，你就不會消失不見😡😡如果我比你兄弟重要，你就不會半夜跑出門打麻將🀄️😭😭你信不信我拿刀子割自己🥺你信不信我從窗戶跳下去😢我跟你媽誰比較重要😣如果我跟她同時掉到水裡🏖️你會先救誰😩😩如果你敢踏出門我們就玩完了🤗🤗如果你再一直打遊戲🎮我們就分手🤬🤬 你要怎麼證明你愛我😇寶貝你有愛我嗎😛你到底愛不愛我🤓為什麼不回訊息🥺🥺是貓咪太好摸了嗎😾是奶茶太好喝了嗎🍾我們的那些海誓山盟還算些什麼🤔你是不是不愛我了😔你真的傷我好深😣😣你都不知道我為你付出了多少🤙🤙🤙那我們可以永遠在一起嗎😻😻那我們什麼時候可以結婚呢🎎👩‍❤️‍👨寶貝你到底在不在乎我❓❓寶貝我這麼愛你❤️🧡💛你為什麼不理我了💔💔😭😭😭"},
                { 29,"\n啊我痛的大叫😫💥\r\n腦袋也空白了⚪️\U0001f9e0🤘\r\n只剩下黑色的夜🌚\r\n還有腳的血在流\U0001fa78🎸\r\n來人啊救救我吧🆘 救救我‼️🏃‍♂️🔥\r\n腳下的戰神阿基里斯🛡️ 華麗殞落\U0001f940🏛️⚡️\r\n戰神戰神戰神阿基里斯🔱🔥\U0001f941\r\n你怎麼還沒陪我征戰沙場就先死⚔️\U0001faa6🎸\r\n戰神戰神戰神阿基里斯🔱🔥\U0001f941\r\n我還要運動一百年你怎麼就先死🏋️‍♂️⏳🔥\r\n戰神戰神戰神阿基里斯🔱🔥\U0001f941\r\n你怎麼還沒等我成為英雄就先死🏆💀🎸\r\n戰神戰神戰神阿基里斯🔱🔥\U0001f941\r\n只能說少了你的加持🕯️✨🤘" },
                { 30,"\n喔愛 💗 什麼是愛 ❓\r\n你看我的 眼神 👀 怎麼這麼 可愛 😍\r\n若是講你的 心 ❤️ 親像 大海 🌊\r\n我也會甘願 \U0001fae6 為了你 暈船 ⛵️😵‍💫\r\n喔喔喔愛 💘 什麼是愛 \U0001f9d0\r\n你看我的 眼神 👁️ 甘嘸一絲絲 愛 ✨\r\n真想欲在這個 花花世界 🌸🌍\r\n帶你去一個 溫暖的所在 🏡☀️\r\nNow I just want hold you tight \U0001fac2, oh baby don’t cry 🚫😢.\r\nI miss you 💭 In this rainy night 🌧️🌙.\r\nI’ll show you the best 💎 in my mind \U0001f9e0.\r\nLet’s dance 💃\U0001f57a in the night.\r\nI will sing a song 🎤 for you, the love ❤️ in my eyes 👀.\r\n喔喔喔愛 👩‍❤️‍👨 有你的 將來 🔜\r\n我對你的 感情 💓 我講不出來 🤐\r\n在這個 風風雨雨 💨☔️ 的世界\r\n你敢會 嫌棄 \U0001f97a 我騎 摩托車 \U0001f6f5💨\r\n喔喔喔愛 🌈 有我的 未來 🌅\r\n我對你的 感情 🔥 我要 講出來"},
                { 31,"\n一二三 1️⃣2️⃣3️⃣ 三百天 3️⃣0️⃣0️⃣🗓️\r\n四五六 4️⃣5️⃣6️⃣ 六百天 6️⃣0️⃣0️⃣🗓️\r\n不知不覺 😶‍🌫️⏳ 在一起永永遠遠 👩‍❤️‍💋‍👨♾️🔒\r\n崴崴孟孟 👫 一起去日本 🇯🇵🍣🗻✈️\r\n厲害崴孟 💪😎 讓旅途順暢無阻 🛣️✅🏎️💨\r\n有時候 🕰️ 孟寶是怪怪寶 \U0001f92a👾👻👶\r\n還好有崴寶 😅👌 細心保護孟寶 🛡️👮‍♂️\U0001fac2\U0001f9b8‍♂️\r\n崴崴孟孟 👫 旅行三百天 \U0001f9f33️⃣0️⃣0️⃣🌞\r\n厲害崴孟 💪😎 讓旅途順暢無阻 🛣️✅🆗\r\n崴孟合作 \U0001f91d\U0001f91b 什麼難關都不怕 🚫😱\U0001f9d7‍♂️🔥\r\n孟寶總是 👶 心暖暖 ❤️🔥\U0001f970 因為崴崴寶 \U0001f97a💖💊\r\n孟寶愛崴寶 👶❤️👱 崴寶愛孟寶 👱❤️👶\r\n崴寶想孟寶 👱💭👶 孟寶想崴寶 👶💭👱\r\n崴孟愛孟崴 😵‍💫❤️‍🔥🌀 孟崴想崴孟 😵‍💫💭🌀\r\n孟想崴崴孟 \U0001f9e0💥\U0001f95c 崴想孟孟崴 \U0001f92a💞💫\r\n崴崴孟孟 👫 一起去日本 🇯🇵🍜⛩️👘\r\n厲害崴孟 💪😎 讓旅途順暢無阻 🛣️\U0001f7e2🚗\r\n有時候 🕰️ 孟寶是怪怪寶 \U0001f92a\U0001f921\U0001f9a0\r\n還好有崴寶 😅👍 細心保護孟寶 ☂️🚧\U0001f98d\r\n崴崴孟孟 👫 旅行三百天 ✈️3️⃣0️⃣0️⃣📅\r\n厲害崴孟 💪😎 讓旅途順暢無阻 🛣️🛤️👌\r\n崴孟合作 👯‍♂️\U0001f9e9 什麼難關都不怕 \U0001f9df‍♂️\U0001f94a💣\r\n孟寶總是 👶 心暖暖 ♨️💓 因為崴崴寶 \U0001f97a🍬\U0001f9f8\r\n崴崴孟孟 👫 旅行永永遠遠 ♾️🚀🌌\r\n厲害崴孟 💪😎"},
                { 32,"\n https://cdn.discordapp.com/attachments/592716175461580800/1136924466467844167/sketch-1691134309329.png?ex=69d4b2eb&is=69d3616b&hm=ec48b3b235886b4519da82b8918c9aeb34c6d67270fc6f3603b609d9355ce5d5&"},
                { 33,"\n https://cdn.discordapp.com/attachments/592716175461580800/1215907744893374595/image.png?ex=69d4a2d4&is=69d35154&hm=dae1edc7b8c33a79c1f43c6174330e41a1c189770cd4d1b8bcc0579391d49382&"},
                { 34,"抽到教招 <:kc13:1511607096473288835>"},
                { 35,"\n https://cdn.discordapp.com/attachments/955038990770270248/1002370012432060488/1657270280182.webm"},
                { 36,"\n 外星人太強了<:kc6:1511607110700498944> \r\n而且她還沒完全張開眼睛的樣子<:kc2:1511607127825715231> \r\n就算沒有王議員的睡臉照也會贏<:kc8:1511607104849317958> \r\n我甚至有點對不起她（大概）:han: \r\n我沒能將我的貼圖在法院上完全展現給她看:alien~1: :alienAnya: :alienAriel: :alienAvatar: :alienBuff: :alienCry: :alienCua: :alienGodzilla: :alienGojo: :alienHotdog: :alienStarburst: :alienSunglasses: \r\n總而言之，告死我的不是小魚:yu: 或小鬼👻而是外星人:alienSunglasses: ，真是太好了:wuu:" },
                { 37,"\n 下部隊要帶的東西(不急，可以等第一次放假再慢慢處理)\r\n1.鞋油跟刷子，也可以用濕紙巾，比較累\r\n2.有蓋子的便當盒，應該都可以裝回宿舍吃(可以先帶)\r\n3.奶頭扣子，我也不知道那是三小，就是一個可以扣在胸前扣子的小布塊。可以夾識別證，比較不會掉，買3個，2件上衣+1件外套的\r\n4.銀色哨子，你雞雞比較大也可以買綠色或紅色，我會在禮拜五晚上打視訊電話給你，問你為甚麼不開鏡頭\r\n5.自己的運動長褲，部隊發的可以穿我隨便你 襪子也可以順便(下部隊前的那個收假就直接穿這套)\r\n6.自己的寢具，豆腐被子晚上就放桌子上，起床擺回來，再也不用折了\r\n7.延長線，插座在下面，上鋪在上面，我在你裡面。如果你是住沒有上下舖的宿舍，我幹你娘\r\n8.縫在衣服上的狗屎爛蛋，部隊應該都會跟你講\r\n9.衣服可以不送洗了，冬天不操課基本上不會流汗，兩套外衣可以穿一個禮拜，問一下學長，自己考慮一下。幹你娘洗衣阿姨偷偷漲價ㄘㄨㄚˋ\r\n10.自己的水壺，可以不用那個爛壺了，尤其是蓋子飛走的(可以先帶，有些人一定要帶) \r\n11.電蚊香，認真，部隊的蚊子比企鵝抗寒，比蜘蛛大隻(可以先帶，看你的抗蚊子程度)\r\n12.行充，你要陪我玩TFT(先帶)"},
                { 38,"\n 1.不要當福委\r\n2.帶防水貼跟奇異筆，在你所有的東西上貼或寫你的號碼，所有的意思是「所有」，立刻、現在\r\n3.行充有用，豆腐頭也許有用，紫色隱者最沒用\r\n4.帶感冒藥，30幾個人擠在一間房間咳嗽打噴嚏，我還以為這裡是他媽方艙醫院\r\n5.不要當福委(認真)\r\n6.耳塞不一定有用，我比奧丁難壓，有些人比奧丁大聲\r\n7.你的鄰兵有大概50%的機率是弱智，長官也是，不要太認真\r\n8.防彈背心不夠可以拿午餐的排骨，這個比較硬\r\n9.我前面有講過不要當福委嗎\r\n10.拿東西的時候檢查一下，不要檢查是不是你的，檢查有沒有壞跟有沒有蟲就好\r\n11.有人叫你簽志願役的時候假裝有興趣，先跟你家人串通好，玩完手機後跟他說家人不同意(有可能看對話紀錄，自己處理一下)\r\n12.立可帶...真的好強\r\n13.如果有變態殺人魔把你關到小房間裡面，讓你選要當福委還是跟醜哥玩Don't Starve Together，選有高腳鳥的那個"},
                { 40,"\n 鼎王麻辣鍋，一桌4個人低消650元任你吃啊，一個人150元湯底，再點個50元的油條，就坐在那邊吃，不用受那個鳥氣，白飯無限盛啊！若飯不夠，店員會跟你90度鞠躬，「貴賓對不起，我們現在白飯在煮，需要等一下，不好意思，貴賓」。我去過鼎王啊！這種態度的啊！鼎王把你當貴賓，點菜的、送菜的還是個美女，不吃飯還有得看，喝完水「欸，我要水」冰水一壺，去鼎王我是不點飲料的啊，冰水一壺冰水一壺冰水一壺，豆腐鴨血豆腐鴨血豆腐鴨血冰水冰水冰水冰水就吃飽啦。"}
            };



        public static async Task<string> GetRandomRewards(IMessageChannel channel, SocketGuildUser user)
        {

            var random = new Random();
            Dictionary<int, string> reward = rewardsCompareList.OrderBy(x => random.Next()).ToDictionary(x => x.Key, x => x.Value);

            
            //如果人在頻道，又抽到有音樂的獎勵，就直接撥音樂
            if (user?.VoiceChannel != null && videoCompareList.ContainsKey(reward.Keys.First()))
            {
                await PlayRewardsMusicAsync(reward.Keys.First(), channel, user);
            }

            return reward.Values.First();
        }



        private static async Task PlayRewardsMusicAsync(int id, IMessageChannel channel, SocketGuildUser user)
        {
            
            Program program = new Program();
            await program.PlayMusicAsync(channel, user, videoCompareList[id]);
        }
    }
}
