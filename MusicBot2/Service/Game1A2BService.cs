using Discord;
using Discord.WebSocket;
using MusicBot2.Helpers;
using MusicBot2.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class Game1A2BService
    {
        private readonly Dictionary<ulong, Game1A2BSession> _activeSessions = new Dictionary<ulong, Game1A2BSession>();
        private readonly Random _random = new Random();

        public Task<(ComponentBuilder, Embed)> StartGameAsync(ulong userId, string number)
        {
            // 檢查是否已有進行中的遊戲
            if (_activeSessions.ContainsKey(userId))
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("⚠️ 提示")
                    .WithColor(Color.Orange)
                    .WithDescription("你已有進行中的遊戲！")
                    .Build();
                return Task.FromResult((new ComponentBuilder(), errorEmbed));
            }

            // 產生答案
            string answer;
            if (string.IsNullOrEmpty(number))
            {
                // 隨機產生4位數字（可重複）
                answer = GenerateRandomNumber();
            }
            else
            {
                answer = number;
            }

            // 建立遊戲session
            var session = new Game1A2BSession
            {
                Answer = answer,
                Attempts = 0,
                History = new List<string>()
            };

            _activeSessions[userId] = session;

            var embed = BuildGameEmbed(session);

            // 建立「猜數字」按鈕
            var component = new ComponentBuilder()
                .WithButton("🔢 猜數字", customId: $"1a2b_guess_{userId}", ButtonStyle.Primary)
                .WithButton("❌ 放棄", customId: $"1a2b_quit_{userId}", ButtonStyle.Danger);

            return Task.FromResult((component, embed));
        }

        public async Task<(ComponentBuilder component,Embed embed)> HandleButtonClickAsync(SocketMessageComponent component, string action, ulong userId)
        {
            if (action == "guess")
            {
                // 檢查是否有進行中的遊戲
                if (!_activeSessions.ContainsKey(userId))
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("⚠️ 提示")
                        .WithColor(Color.Orange)
                        .WithDescription("找不到遊戲，請重新開始！")
                        .Build();
                    return CommonHelper.BuildErrorResponse("");
                }

                // 跳出 Modal 輸入框
                var modal = new ModalBuilder()
                    .WithTitle("輸入你的猜測")
                    .WithCustomId($"1a2b_modal_{userId}")
                    .AddTextInput("請輸入4位數字（可重複）", "guess_input",
                        placeholder: "例如：1234",
                        minLength: 4,
                        maxLength: 4)
                    .Build();

                await component.RespondWithModalAsync(modal);
                return (null, null);
            }
            else if (action == "quit")
            {
                var session = _activeSessions.GetValueOrDefault(userId);
                var answer = session?.Answer ?? "?";
                _activeSessions.Remove(userId);

                var embed = new EmbedBuilder()
                    .WithTitle("🎯 1A2B 猜數字")
                    .WithColor(Color.Red)
                    .WithDescription($"遊戲結束！答案是 **{answer}**")
                    .Build();

                return (new ComponentBuilder(), embed);
            }

            return (null, null);
        }

        public async Task<((ComponentBuilder? component, Embed embed), bool isError)> HandleModalSubmitAsync(SocketModal modal, ulong userId)
        {
            var session = _activeSessions.GetValueOrDefault(userId);

            

            if (session == null)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("⚠️ 提示")
                    .WithColor(Color.Orange)
                    .WithDescription("找不到遊戲，請重新開始！")
                    .Build();
                return ((null, errorEmbed), true);
            }

            var guess = modal.Data.Components
                .First(x => x.CustomId == "guess_input").Value;

            // 驗證輸入
            if (!IsValidGuess(guess))
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("⚠️ 提示")
                    .WithColor(Color.Orange)
                    .WithDescription("請輸入4位數字！")
                    .Build();
                return ((null, errorEmbed), true);
            }

            // 計算 A、B
            var (a, b) = Calculate(guess, session.Answer);
            session.Attempts++;
            session.History.Add($"`{guess}` → **{a}A{b}B**");

            // 判斷勝利
            if (a == 4)
            {
                _activeSessions.Remove(userId);

                var winEmbed = new EmbedBuilder()
                    .WithTitle("🎉 答對了！")
                    .WithColor(Color.Green)
                    .WithDescription($"答案就是 **{guess}**，共猜了 **{session.Attempts}** 次！")
                    .AddField("猜測紀錄", string.Join("\n", session.History.TakeLast(10)))
                    .AddField($"恭喜{modal.User.Username}",$"獎勵妳 {await RewardsHelpers.GetRandomRewards(modal.Channel, modal.User as SocketGuildUser)}")
                    .Build();

                return ((new ComponentBuilder(), winEmbed), false);
            }
            else
            {
                // 更新遊戲狀態
                var embed = BuildGameEmbed(session, $"{a}A{b}B");

                var component = new ComponentBuilder()
                    .WithButton("🔢 猜數字", customId: $"1a2b_guess_{userId}", ButtonStyle.Primary)
                    .WithButton("❌ 放棄", customId: $"1a2b_quit_{userId}", ButtonStyle.Danger);

                return ((component, embed), false);
            }
        }

        private string GenerateRandomNumber()
        {
            var result = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                result.Append(_random.Next(0, 10));
            }
            return result.ToString();
        }

        private (int a, int b) Calculate(string guess, string answer)
        {
            int a = 0, b = 0;
            var answerChars = answer.ToCharArray().ToList();
            var guessChars = guess.ToCharArray().ToList();

            // 先計算A（位置和數字都正確）
            for (int i = 0; i < 4; i++)
            {
                if (guess[i] == answer[i])
                {
                    a++;
                    answerChars[i] = 'X'; // 標記已使用
                    guessChars[i] = 'Y';  // 標記已使用
                }
            }

            // 再計算B（數字正確但位置不對）
            for (int i = 0; i < 4; i++)
            {
                if (guessChars[i] != 'Y') // 未被標記為A
                {
                    for (int j = 0; j < 4; j++)
                    {
                        if (answerChars[j] != 'X' && guess[i] == answer[j])
                        {
                            b++;
                            answerChars[j] = 'X'; // 標記已使用
                            break;
                        }
                    }
                }
            }

            return (a, b);
        }

        private bool IsValidGuess(string guess) =>
            guess.Length == 4 &&
            guess.All(char.IsDigit);

        private Embed BuildGameEmbed(Game1A2BSession session, string? lastResult = null)
        {
            var eb = new EmbedBuilder()
                .WithTitle("🎯 1A2B 猜數字")
                .WithColor(Color.Blue)
                .AddField("猜測次數", session.Attempts.ToString(), inline: true)
                .AddField("說明", "數字可重複，共4位", inline: true);

            if (session.History.Any())
            {
                eb.AddField("猜測紀錄", string.Join("\n", session.History.TakeLast(10)));
            }

            if (lastResult != null)
                eb.WithDescription($"上次結果：**{lastResult}**");
            else
                eb.WithDescription("點擊按鈕開始猜！");

            return eb.Build();
        }

        public Game1A2BSession GetSession(ulong userId)
        {
            return _activeSessions.GetValueOrDefault(userId);
        }

        public void EndGame(ulong userId)
        {
            _activeSessions.Remove(userId);
        }
    }
}
