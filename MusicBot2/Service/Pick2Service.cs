using MusicBot2.Models;
using NAudio.SoundFont;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace MusicBot2.Service
{
    public class Pick2Service
    {
        private readonly HttpClient _httpClient;
        private readonly Dictionary<ulong, Pick2GameState> _activeGames = new Dictionary<ulong, Pick2GameState>();

        //post https://2pick.app/api/game 來取得game_serial
        //get https://2pick.app/api/game/{game_serial}/elements?limit=32 來取得實際元素資料，limit=32預設不動

        public Pick2Service()
        {
            _httpClient = new HttpClient();
        }

        // 開始新遊戲
        public async Task<(string imageMessage, ComponentBuilder component, Embed embed)> StartGameAsync(ulong channelId, string postSerial, int elementCount)
        {
            try
            {
                // 1. 先呼叫 API 建立遊戲並取得 game_serial
                var requestBody = new Pick2GameRequest
                {
                    post_serial = postSerial,
                    element_count = elementCount,
                    password = ""
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("https://2pick.app/api/game", content);

                if (!response.IsSuccessStatusCode)
                {
                    var (comp, emb) = BuildErrorResponse($"無法建立遊戲: {response.StatusCode}");
                    return ("", comp, emb);
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var gameResponse = JsonConvert.DeserializeObject<Pick2GameResponse>(responseContent);

                // 2. 取得遊戲元素資料
                var elementsResponse = await _httpClient.GetAsync($"https://2pick.app/api/game/{gameResponse.game_serial}/elements?limit={elementCount}");

                if (!elementsResponse.IsSuccessStatusCode)
                {
                    var (comp, emb) = BuildErrorResponse($"無法取得遊戲元素: {elementsResponse.StatusCode}");
                    return ("", comp, emb);
                }

                var elementsContent = await elementsResponse.Content.ReadAsStringAsync();
                var elementsData = JsonConvert.DeserializeObject<GetElementsResponse>(elementsContent);

                // 3. 建立遊戲狀態
                var allElements = elementsData.data.Where(e => e.is_eliminated == "0").ToList();

                // 隨機打亂順序作為初始配對
                var random = new Random();
                allElements = allElements.OrderBy(x => random.Next()).ToList();

                var gameState = new Pick2GameState
                {
                    ChannelId = channelId,
                    GameSerial = gameResponse.game_serial,
                    Title = gameResponse.data.title,
                    AllElements = elementsData.data,
                    CurrentBracket = allElements,  // 當前輪次的所有選手
                    NextBracket = new List<Pick2Element>(),  // 下一輪的勝者
                    CurrentMatchIndex = 0,  // 當前比賽場次
                    TotalMatches = allElements.Count / 2,  // 本輪總場次
                    CurrentRound = 1,  // 當前輪次（32強、16強等）
                    Votes = new Dictionary<int, HashSet<ulong>>()
                };

                _activeGames[channelId] = gameState;

                // 4. 開始第一場比賽
                return StartNewMatch(gameState);
            }
            catch (Exception ex)
            {
                var (comp, emb) = BuildErrorResponse($"發生錯誤: {ex.Message}");
                return ("", comp, emb);
            }
        }

        // 開始新的比賽場次
        private (string imageMessage, ComponentBuilder component, Embed embed) StartNewMatch(Pick2GameState gameState)
        {
            // 檢查是否所有比賽都完成
            if (gameState.CurrentMatchIndex >= gameState.TotalMatches)
            {
                // 本輪比賽全部完成，進入下一輪
                return StartNextRound(gameState);
            }

            // 取得當前比賽的兩個選手
            int index1 = gameState.CurrentMatchIndex * 2;
            int index2 = gameState.CurrentMatchIndex * 2 + 1;

            if (index2 >= gameState.CurrentBracket.Count)
            {
                // 奇數情況，最後一個選手直接晉級
                gameState.NextBracket.Add(gameState.CurrentBracket[index1]);
                gameState.CurrentMatchIndex++;
                return StartNewMatch(gameState);
            }

            gameState.CurrentOptions = new List<Pick2Element>
            {
                gameState.CurrentBracket[index1],
                gameState.CurrentBracket[index2]
            };

            gameState.Votes.Clear();
            gameState.Votes[0] = new HashSet<ulong>();
            gameState.Votes[1] = new HashSet<ulong>();

            var imageMessage = BuildImageMessage(gameState);
            var component = BuildVoteButtons(gameState);
            var embed = BuildRoundEmbed(gameState);

            return (imageMessage, component, embed);
        }

        // 進入下一輪
        private (string imageMessage, ComponentBuilder component, Embed embed) StartNextRound(Pick2GameState gameState)
        {
            // 檢查是否只剩一個選手（冠軍）
            if (gameState.NextBracket.Count <= 1)
            {
                return ShowWinner(gameState);
            }

            // 進入下一輪
            gameState.CurrentBracket = new List<Pick2Element>(gameState.NextBracket);
            gameState.NextBracket.Clear();
            gameState.CurrentMatchIndex = 0;
            gameState.TotalMatches = gameState.CurrentBracket.Count / 2;
            gameState.CurrentRound++;

            return StartNewMatch(gameState);
        }

        // 處理投票
        public async Task<(string imageMessage, ComponentBuilder component, Embed embed, bool gameOver)> HandleVoteAsync(SocketMessageComponent component, int choice)
        {
            var channelId = component.Channel.Id;
            var userId = component.User.Id;

            if (!_activeGames.ContainsKey(channelId))
            {
                var emb = BuildErrorEmbed("找不到進行中的遊戲");
                return ("", new ComponentBuilder(), emb, true);
            }

            var gameState = _activeGames[channelId];

            // 檢查使用者是否已經投過票
            foreach (var voteSet in gameState.Votes.Values)
            {
                voteSet.Remove(userId);
            }

            // 添加新投票
            if (gameState.Votes.ContainsKey(choice))
            {
                gameState.Votes[choice].Add(userId);
            }

            var imageMessage = BuildImageMessage(gameState);
            var newComponent = BuildVoteButtons(gameState);
            var embed = BuildRoundEmbed(gameState);

            return (imageMessage, newComponent, embed, false);
        }

        // 完成當前比賽
        public async Task<(string imageMessage, ComponentBuilder component, Embed embed)> FinishRoundAsync(ulong channelId)
        {
            if (!_activeGames.ContainsKey(channelId))
            {
                var (comp, emb) = BuildErrorResponse("找不到進行中的遊戲");
                return ("", comp, emb);
            }

            var gameState = _activeGames[channelId];

            // 統計票數
            int votes0 = gameState.Votes[0].Count;
            int votes1 = gameState.Votes[1].Count;

            // 決定勝者
            Pick2Element winner;
            if (votes0 > votes1)
            {
                winner = gameState.CurrentOptions[0];
            }
            else if (votes1 > votes0)
            {
                winner = gameState.CurrentOptions[1];
            }
            else
            {
                // 平手時隨機決定勝者
                var random = new Random();
                winner = gameState.CurrentOptions[random.Next(2)];
            }

            // 將勝者加入下一輪
            gameState.NextBracket.Add(winner);

            // 進入下一場比賽
            gameState.CurrentMatchIndex++;

            return StartNewMatch(gameState);
        }

        // 顯示冠軍
        private (string imageMessage, ComponentBuilder component, Embed embed) ShowWinner(Pick2GameState gameState)
        {
            var winner = gameState.NextBracket.FirstOrDefault();

            var embed = new EmbedBuilder()
            {
                Title = $"🏆 {gameState.Title} - 冠軍出爐！",
                Description = winner != null ? $"恭喜 **{winner.title}** 獲得第一名！" : "沒有冠軍",
                Color = Color.Gold
            };

            if (winner != null)
            {
                var totalRounds = (int)Math.Log(gameState.AllElements.Count, 2);
                embed.AddField("總輪次", $"{gameState.CurrentRound}/{totalRounds}", true);
                embed.AddField("總參與元素", gameState.AllElements.Count.ToString(), true);
            }

            var component = new ComponentBuilder()
                .WithButton("🔄 重新開始", $"pick2_reset", ButtonStyle.Success);

            // 冠軍圖片訊息
            var imageMessage = "";
            if (winner != null && !string.IsNullOrEmpty(winner.thumb_url))
            {
                imageMessage = $"🏆 **冠軍圖片**\n{winner.thumb_url}";
            }

            return (imageMessage, component, embed.Build());
        }

        // 建立投票按鈕
        private ComponentBuilder BuildVoteButtons(Pick2GameState gameState)
        {
            var builder = new ComponentBuilder();

            if (gameState.CurrentOptions.Count >= 2)
            {
                var votes0 = gameState.Votes[0].Count;
                var votes1 = gameState.Votes[1].Count;

                // 根據票數決定按鈕樣式
                var style0 = votes0 > votes1 ? ButtonStyle.Success : (votes0 < votes1 ? ButtonStyle.Secondary : ButtonStyle.Primary);
                var style1 = votes1 > votes0 ? ButtonStyle.Success : (votes1 < votes0 ? ButtonStyle.Secondary : ButtonStyle.Primary);

                // 限制標題長度以符合 Discord 按鈕限制
                var title0 = gameState.CurrentOptions[0].title.Length > 50 
                    ? gameState.CurrentOptions[0].title.Substring(0, 47) + "..." 
                    : gameState.CurrentOptions[0].title;
                var title1 = gameState.CurrentOptions[1].title.Length > 50 
                    ? gameState.CurrentOptions[1].title.Substring(0, 47) + "..." 
                    : gameState.CurrentOptions[1].title;

                // 第一行：選項1
                builder.WithButton($"1️⃣ {title0} ({votes0}票)", 
                    $"pick2_vote_0", style0);

                // 第二行：選項2
                builder.WithButton($"2️⃣ {title1} ({votes1}票)", 
                    $"pick2_vote_1", style1, row: 1);
            }

            // 第三行：控制按鈕
            builder.WithButton("➡️ 確認並下一題", $"pick2_finish", ButtonStyle.Success, row: 2);
            builder.WithButton("🔄 重新開始", $"pick2_reset", ButtonStyle.Danger, row: 2);

            return builder;
        }

        // 建立回合 Embed
        private Embed BuildRoundEmbed(Pick2GameState gameState)
        {
            var sb = new StringBuilder();

            // 計算當前是幾強
            var bracketName = GetBracketName(gameState.CurrentBracket.Count);
            sb.AppendLine($"**{bracketName}賽 - 第 {gameState.CurrentMatchIndex + 1}/{gameState.TotalMatches} 場**");
            sb.AppendLine($"📊 本輪剩餘: {gameState.CurrentBracket.Count - gameState.NextBracket.Count * 2} 人 | 已晉級: {gameState.NextBracket.Count} 人");
            sb.AppendLine();

            if (gameState.CurrentOptions.Count >= 2)
            {
                var votes0 = gameState.Votes[0].Count;
                var votes1 = gameState.Votes[1].Count;
                var totalVotes = votes0 + votes1;

                // 計算百分比
                var percent0 = totalVotes > 0 ? (votes0 * 100.0 / totalVotes) : 0;
                var percent1 = totalVotes > 0 ? (votes1 * 100.0 / totalVotes) : 0;

                sb.AppendLine("**請在以下兩個選項中選擇：**");
                sb.AppendLine();
                sb.AppendLine($"1️⃣ **{gameState.CurrentOptions[0].title}**");
                sb.AppendLine($"   票數: {votes0} ({percent0:F1}%)");
                sb.AppendLine();
                sb.AppendLine($"2️⃣ **{gameState.CurrentOptions[1].title}**");
                sb.AppendLine($"   票數: {votes1} ({percent1:F1}%)");
                sb.AppendLine();
                sb.AppendLine($"📝 總投票數: {totalVotes}");
            }

            var embed = new EmbedBuilder()
            {
                Title = $"🎮 {gameState.Title}",
                Description = sb.ToString(),
                Color = Color.Blue
            };

            embed.WithFooter("💡 點擊按鈕投票 | 可以更改選擇 | 投票後點「確認並下一題」繼續");

            return embed.Build();
        }

        // 取得賽制名稱
        private string GetBracketName(int count)
        {
            return count switch
            {
                32 => "32強",
                16 => "16強",
                8 => "8強",
                4 => "4強（準決賽）",
                2 => "決賽",
                _ => $"{count}強"
            };
        }

        // 建立圖片訊息
        private string BuildImageMessage(Pick2GameState gameState)
        {
            var sb = new StringBuilder();
            var bracketName = GetBracketName(gameState.CurrentBracket.Count);
            sb.AppendLine($"📸 **{bracketName} - 第 {gameState.CurrentMatchIndex + 1}/{gameState.TotalMatches} 場**");
            sb.AppendLine();

            if (gameState.CurrentOptions.Count >= 2)
            {
                // 選項 1
                sb.AppendLine($"**1️⃣ {gameState.CurrentOptions[0].title}**");
                if (!string.IsNullOrEmpty(gameState.CurrentOptions[0].source_url))
                {
                    sb.AppendLine(gameState.CurrentOptions[0].source_url);
                }
                else if (!string.IsNullOrEmpty(gameState.CurrentOptions[0].thumb_url))
                {
                    sb.AppendLine(gameState.CurrentOptions[0].thumb_url);
                }
                else
                {
                    sb.AppendLine("_無圖片_");
                }
                sb.AppendLine();

                // 選項 2
                sb.AppendLine($"**2️⃣ {gameState.CurrentOptions[1].title}**");
                if (!string.IsNullOrEmpty(gameState.CurrentOptions[1].source_url))
                {
                    sb.AppendLine(gameState.CurrentOptions[1].source_url);
                }
                else if (!string.IsNullOrEmpty(gameState.CurrentOptions[1].thumb_url))
                {
                    sb.AppendLine(gameState.CurrentOptions[1].thumb_url);
                }
                else
                {
                    sb.AppendLine("_無圖片_");
                }
            }

            return sb.ToString();
        }

        // 重置遊戲
        public void ResetGame(ulong channelId)
        {
            if (_activeGames.ContainsKey(channelId))
            {
                _activeGames.Remove(channelId);
            }
        }

        // 設置訊息 ID
        public void SetMessageIds(ulong channelId, ulong imageMessageId, ulong voteMessageId)
        {
            if (_activeGames.ContainsKey(channelId))
            {
                _activeGames[channelId].ImageMessageId = imageMessageId;
                _activeGames[channelId].VoteMessageId = voteMessageId;
            }
        }

        // 取得遊戲狀態
        public Pick2GameState GetGameState(ulong channelId)
        {
            return _activeGames.ContainsKey(channelId) ? _activeGames[channelId] : null;
        }

        // 建立錯誤回應
        private (ComponentBuilder, Embed) BuildErrorResponse(string message)
        {
            var embed = BuildErrorEmbed(message);
            return (new ComponentBuilder(), embed);
        }

        private Embed BuildErrorEmbed(string message)
        {
            return new EmbedBuilder()
            {
                Title = "❌ 錯誤",
                Description = message,
                Color = Color.Red
            }.Build();
        }
    }
}
