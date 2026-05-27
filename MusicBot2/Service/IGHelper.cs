using Discord;
using Discord.WebSocket;
using InstagramApiSharp;
using InstagramApiSharp.API;
using InstagramApiSharp.API.Builder;
using InstagramApiSharp.Classes;
using InstagramApiSharp.Classes.SessionHandlers;
using InstagramApiSharp.Logger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YoutubeExplode.Channels;

namespace MusicBot2.Service
{
    public class IGHelper
    {
        private static IInstaApi InstaApi;
        private HashSet<string> readMessages = new HashSet<string>();

        public async Task StartAsync(DiscordSocketClient client)
        {
            var userSession = new UserSessionData
            {
                UserName = "你的IG帳號",
                Password = "你的IG密碼"
            };

            InstaApi = InstaApiBuilder.CreateBuilder()
                        .SetUser(userSession)
                        .Build();

            var loginResult = await InstaApi.LoginAsync();
            if (!loginResult.Succeeded)
            {
                Console.WriteLine("登入失敗: " + loginResult.Info.Message);
                return;
            }

            var channel = client.GetChannel(1286327830904569906) as IMessageChannel;
            while (true)
            {
                try
                {
                    var inbox = await InstaApi.MessagingProcessor.GetDirectInboxAsync(PaginationParameters.MaxPagesToLoad(1));

                    var newThreads = inbox.Value.Inbox.Threads.Where(t => t.HasUnreadMessage == true).ToList();

                    foreach (var thread in newThreads)
                    {
                        foreach (var msg in thread.Items)
                        {
                            if (!readMessages.Contains(msg.ItemId))
                            {
                                readMessages.Add(msg.ItemId);

                                var sender = thread.Users.FirstOrDefault(u => u.Pk == msg.UserId)?.UserName ?? "Unknown";
                                Console.WriteLine($"{sender} : {msg.Text}");
                                await channel.SendMessageAsync($"{sender}這個王八蛋又傳了姬芭東西給我，所以我要傳給所有人");
                                await channel.SendMessageAsync($"{msg.Text}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("IG 監聽發生錯誤: " + ex.Message);
                }

                await Task.Delay(5000); //每5秒檢查一次新訊息
            }
        }
    }
}
