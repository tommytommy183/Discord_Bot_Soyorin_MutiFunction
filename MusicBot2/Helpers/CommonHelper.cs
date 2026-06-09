using Discord;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Helpers
{
    public class CommonHelper
    {
        private static List<string> emojis = new List<string>
        {
            "<:kc2:1511607127825715231>",
            "<:kc3:1511607126064103424>",
            "<:kc5:1511607112378089491>",
            "<:kc6:1511607110700498944>",
            "<:kc7:1511607109035495444>",
            "<:kc9:1511607103377379429>",
            "<:kc10:1511607101376692305>",
            "<:kc11:1511607099732529203>",
            "<:kc12:1511607098134495262>",
            "<:kc13:1511607096473288835>",
            "<:kc14:1511607094636314654>",
            "<:kc15:1511607092925038592>",
            "<a:95333c6fabb3e5d23e6325817ce09986:1293572566715203594>",
            "<a:9817093c3e872b967d7759abd0a3a6c4:1293572553184378901>",
            "<a:mygo:1293569874001530921>",
            "<:anon_confuse:1293560897842712616>",
            "<:taki_uh:1293560881925455913>",
            "<:soyo_glare:1293560866800930866>",
            "<:saki_customer_service:1293560846936572010>",
            "<:kueze:1293560834131234907>",
            "<:anon_ha_1:1293560817194897469>",
            "<:mutsumi_tanoshi:1293560758139097248>",
            "<:saki_ave_mujica:1293560735695376485>",
            "<:taki_ha_1:1293560646012637195>",
            "<:soyo_qq:1293560619278008321>",
            "<:umiri_smug_1:1293560451300589618>",
            "<:uika_smug_1:1293560392341000272>",
            "<:anon_angry_1:1293560338809094244>",
            "<:tomori_happy:1293560266969055434>",
            "<:anon_nervous:1293560190372675604>",
            "<:nyamuchi_glass:1293559970281033818>",
            "<:mutsumi_yokada:1293559939536519269>",
            "<:sakiko_tea:1293559927133962301>",
            "<:tomori_scream:1293559831310897184>",
        };

        public static async Task AddEmojiToMessageAsync(IUserMessage message, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var emote = Emote.Parse(emojis[i]);
                await message.AddReactionAsync(emote);
                await Task.Delay(300);
            }

            await message.AddReactionAsync(Emote.Parse("<a:24f60b6c774e8beb:1513720299558801480>"));
        }

        public static string AddEmoji(string item)
        {
            //傳入的item字串以,區隔，將每個item前面加上emoji
            var items = item.Split(',');
            var result = new StringBuilder();

            for (int i = 0; i < items.Length; i++)
            {
                var emoji = emojis[i]; // 循環使用emoji
                result.Append($"{emoji} {items[i].Trim()}");
                if (i <= items.Length - 1)
                    result.Append("\n ");
            }
            result.Append("<a:24f60b6c774e8beb:1513720299558801480> 是gay或T或非二元性別或性別流動者");

            return result.ToString();
        }
    }
}
