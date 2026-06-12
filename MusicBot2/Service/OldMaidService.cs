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
    public class OldMaidService
    {
        private Dictionary<ulong, OldMaidGame> _games = new Dictionary<ulong, OldMaidGame>();

        public OldMaidService()
        {
        }

        public async Task<string> StartGame(IMessageChannel channel, List<SocketGuildUser> players)
        {
            if (_games.ContainsKey(channel.Id))
            {
                return "已經有進行中的uc";
            }

            if (players == null || players.Count < 2)
            {
                return "至少要2個人才能開多人喔寶貝";
            }

            if (players.Count > 6)
            {
                return "最多6個人，太多人會被撐開";
            }

            var game = new OldMaidGame(players);
            game.InitializeDeck();
            game.DealCards();
            _games[channel.Id] = game;

            var sb = new StringBuilder();
            sb.AppendLine("🃏 **抽鬼牌遊戲開始！**");
            sb.AppendLine($"玩家: {string.Join(", ", players.Select(p => p.DisplayName))}");
            sb.AppendLine($"\n遊戲規則:");
            sb.AppendLine("• 每個人輪流從下家抽一張牌");
            sb.AppendLine("• 抽到能配對的牌就自動丟掉");
            sb.AppendLine("• 最後剩下鬼牌的人就輸了");
            sb.AppendLine("• 使用 `/ghosthands` 查看自己的手牌\n");
            sb.AppendLine(game.GetPublicStatus());

            return sb.ToString();
        }

        public async Task<string> StartTestGame(IMessageChannel channel, SocketGuildUser realPlayer)
        {
            if (_games.ContainsKey(channel.Id))
            {
                return "這個頻道已經有遊戲在進行中了啦，等這局結束再開新的";
            }

            var players = new List<SocketGuildUser> { realPlayer };
            var game = new OldMaidGame(players, isTestMode: true);
            game.InitializeDeck();
            game.DealCards();
            _games[channel.Id] = game;

            var sb = new StringBuilder();
            sb.AppendLine("🃏 **抽鬼牌遊戲開始！(測試模式)**");
            sb.AppendLine($"玩家: {string.Join(", ", game.GetPlayerNames())}");
            sb.AppendLine($"\n遊戲規則:");
            sb.AppendLine("• 每個人輪流從下家抽一張牌");
            sb.AppendLine("• 抽到能配對的牌就自動丟掉");
            sb.AppendLine("• 最後剩下鬼牌的人就輸了");
            sb.AppendLine("• 🤖 其他玩家是電腦自動控制");
            sb.AppendLine("• 使用 `/ghosthands` 查看自己的手牌\n");
            sb.AppendLine(game.GetPublicStatus());

            return sb.ToString();
        }

        public async Task<(string message, ComponentBuilder component, bool needFollowup, string followupMessage)> DrawCard(
            IMessageChannel channel, 
            SocketGuildUser user, 
            int cardPosition)
        {
            if (!_games.ContainsKey(channel.Id))
            {
                return ("還沒開始遊戲啦，先用指令開始遊戲", null, false, null);
            }

            var game = _games[channel.Id];

            if (game.IsGameOver)
            {
                _games.Remove(channel.Id);
                return ($"遊戲已經結束了，{game.GetLoserName()} 是榨菜味鬼鬼👻的老公", null, false, null);
            }

            // 檢查是不是當前玩家
            if (!game.IsCurrentPlayer(user))
            {
                return ($"還沒輪到你喔寶貝，現在是 **{game.GetCurrentPlayerName()}** 的回合", null, false, null);
            }

            // 立即執行抽牌，不包含延遲
            var result = game.DrawCardImmediate(cardPosition);

            if (game.IsGameOver)
            {
                result += $"\n\n🎭 **遊戲結束！**";
                result += $"\n👻 {game.GetLoserName()} 抽到鬼牌，{game.GetLoserName()}老婆是榨菜味鬼鬼";
                result += $"\n獎勵: {await RewardsHelpers.GetRandomRewards(channel, user)}";
                _games.Remove(channel.Id);
                return (result + "\n" + game.GetPublicStatus(), null, false, null);
            }

            var component = game.CreateDrawButtons();
            result += "\n" + game.GetPublicStatus();
            
            string followupMessage = null;
            bool needFollowup = false;

            if (!game.IsCurrentPlayerBot())
            {
                result += $"\n\n請選擇要抽 **{game.GetTargetPlayerName()}** 的第幾張牌：";
            }
            else
            {
                // 如果下一個是電腦，需要後續處理
                needFollowup = true;
                followupMessage = await game.ProcessBotTurns(channel);
            }
            
            return (result, component, needFollowup, followupMessage);
        }

        public Embed GetPlayerHand(IMessageChannel channel, SocketGuildUser user)
        {
            if (!_games.ContainsKey(channel.Id))
            {
                return new EmbedBuilder()
                    .WithTitle("❌ 錯誤")
                    .WithDescription("這個頻道沒有進行中的遊戲")
                    .WithColor(Color.Red)
                    .Build();
            }

            var game = _games[channel.Id];
            var player = game.GetPlayer(user);

            if (player == null)
            {
                return new EmbedBuilder()
                    .WithTitle("❌ 錯誤")
                    .WithDescription("你不在這場遊戲中")
                    .WithColor(Color.Red)
                    .Build();
            }

            var embed = new EmbedBuilder()
                .WithTitle("🎴 你的手牌")
                .WithDescription(player.GetHandDisplay())
                .WithColor(Color.Blue)
                .WithFooter($"剩餘 {player.Hand.Count} 張牌")
                .WithTimestamp(DateTimeOffset.Now);

            // 如果是當前玩家，顯示提示
            if (game.IsCurrentPlayer(user))
            {
                var targetPlayerName = game.GetTargetPlayerName();
                embed.AddField("輪到你了！", $"請選擇要抽 **{targetPlayerName}** 的第幾張牌");
            }

            return embed.Build();
        }

        public string GetStatus(IMessageChannel channel)
        {
            if (!_games.ContainsKey(channel.Id))
            {
                return "目前沒有進行中的遊戲";
            }

            return _games[channel.Id].GetPublicStatus();
        }

        public string ResetGame(IMessageChannel channel)
        {
            if (!_games.ContainsKey(channel.Id))
            {
                return "沒有遊戲在進行中";
            }

            _games.Remove(channel.Id);
            return "遊戲已重置";
        }

        public ComponentBuilder GetDrawButtons(IMessageChannel channel)
        {
            if (!_games.ContainsKey(channel.Id))
            {
                return null;
            }

            return _games[channel.Id].CreateDrawButtons();
        }
    }

    public class OldMaidGame
    {
        private List<OldMaidPlayer> _players;
        private int _currentPlayerIndex;
        private const string JokerCard = "🃏";
        private List<string> _deck;
        private bool _isTestMode;

        public bool IsGameOver { get; private set; }
        private OldMaidPlayer _loser;

        public OldMaidGame(List<SocketGuildUser> users, bool isTestMode = false)
        {
            _isTestMode = isTestMode;

            if (isTestMode)
            {
                _players = new List<OldMaidPlayer>
                {
                    new OldMaidPlayer(users[0], isBot: false),
                    new OldMaidPlayer(null, isBot: true) { BotName = "OB一串字母女士" },
                    new OldMaidPlayer(null, isBot: true) { BotName = "喜歡撿石頭的自閉症女孩" },
                    new OldMaidPlayer(null, isBot: true) { BotName = "有精神病的雙重人格黃瓜" },
                    new OldMaidPlayer(null, isBot: true) { BotName = "唐氏症粉毛" },
                    new OldMaidPlayer(null, isBot: true) { BotName = "喜歡舔藍毛的金毛大狗" },
                };
            }
            else
            {
                _players = users.Select(u => new OldMaidPlayer(u, isBot: false)).ToList();
            }

            _currentPlayerIndex = 0;
            IsGameOver = false;
        }

        public void InitializeDeck()
        {
            var deck = new List<string>();
            var suits = new[] { "♠️", "♥️", "♣️", "♦️" };
            var ranks = new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };

            foreach (var suit in suits)
            {
                foreach (var rank in ranks)
                {
                    deck.Add($"{suit}{rank}");
                }
            }

            deck.RemoveAt(new Random().Next(deck.Count));
            deck.Add(JokerCard);

            var random = new Random();
            _deck = deck.OrderBy(x => random.Next()).ToList();
        }

        public void DealCards()
        {
            int playerIndex = 0;
            foreach (var card in _deck)
            {
                _players[playerIndex].Hand.Add(card);
                playerIndex = (playerIndex + 1) % _players.Count;
            }

            foreach (var player in _players)
            {
                player.RemovePairs();
            }
        }

        // 立即執行抽牌，不包含延遲
        public string DrawCardImmediate(int cardPosition)
        {
            var currentPlayer = _players[_currentPlayerIndex];
            var nextPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;

            // 找到下一個還有牌的玩家
            while (_players[nextPlayerIndex].Hand.Count == 0)
            {
                nextPlayerIndex = (nextPlayerIndex + 1) % _players.Count;
                if (nextPlayerIndex == _currentPlayerIndex)
                {
                    break;
                }
            }

            var targetPlayer = _players[nextPlayerIndex];

            if (targetPlayer.Hand.Count == 0)
            {
                IsGameOver = true;
                _loser = currentPlayer;
                return $"{currentPlayer.GetDisplayName()} 無法再抽牌了";
            }

            // 電腦玩家自動選擇
            if (currentPlayer.IsBot)
            {
                var random = new Random();
                cardPosition = random.Next(1, targetPlayer.Hand.Count + 1);
            }

            // 檢查位置是否有效
            if (cardPosition < 1 || cardPosition > targetPlayer.Hand.Count)
            {
                return $"無效的位置！請選擇 1 到 {targetPlayer.Hand.Count} 之間的數字";
            }

            // 抽牌（位置從1開始，索引從0開始）
            var drawnCard = targetPlayer.Hand[cardPosition - 1];
            targetPlayer.Hand.RemoveAt(cardPosition - 1);
            currentPlayer.Hand.Add(drawnCard);

            var sb = new StringBuilder();
            sb.AppendLine($"🎴 {currentPlayer.GetDisplayName()} 從 {targetPlayer.GetDisplayName()} 的第 **{cardPosition}** 張牌抽了：**{drawnCard}**");

            if (drawnCard == JokerCard)
            {
                sb.AppendLine($"💀 抽到了 **鬼牌！**");
            }

            // 配對並移除
            var pairsRemoved = currentPlayer.RemovePairs();
            if (pairsRemoved > 0)
            {
                sb.AppendLine($"✨ 配對成功，丟掉了 {pairsRemoved} 對牌");
            }

            // 檢查遊戲是否結束
            var playersWithCards = _players.Count(p => p.Hand.Count > 0);
            if (playersWithCards == 1)
            {
                IsGameOver = true;
                _loser = _players.First(p => p.Hand.Count > 0);
            }
            else
            {
                // 移動到下一個還有牌的玩家
                _currentPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;
                while (_players[_currentPlayerIndex].Hand.Count == 0)
                {
                    _currentPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;
                }
            }

            return sb.ToString();
        }

        // 處理電腦玩家的回合（帶延遲）
        public async Task<string> ProcessBotTurns(IMessageChannel channel)
        {
            var sb = new StringBuilder();
            
            while (_players[_currentPlayerIndex].IsBot && !IsGameOver)
            {
                await Task.Delay(1500);
                
                var result = DrawCardImmediate(0);
                sb.AppendLine(result);
            }

            if (!IsGameOver)
            {
                sb.AppendLine("\n" + GetPublicStatus());
                if (!_players[_currentPlayerIndex].IsBot)
                {
                    sb.AppendLine($"\n請選擇要抽 **{GetTargetPlayerName()}** 的第幾張牌：");
                }
            }

            return sb.ToString();
        }

        public ComponentBuilder CreateDrawButtons()
        {
            if (IsGameOver)
                return null;

            var currentPlayer = _players[_currentPlayerIndex];
            
            // 如果當前玩家是電腦，不需要按鈕
            if (currentPlayer.IsBot)
                return null;

            var nextPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;

            while (_players[nextPlayerIndex].Hand.Count == 0)
            {
                nextPlayerIndex = (nextPlayerIndex + 1) % _players.Count;
                if (nextPlayerIndex == _currentPlayerIndex)
                    return null;
            }

            var targetPlayer = _players[nextPlayerIndex];
            var component = new ComponentBuilder();

            int cardCount = targetPlayer.Hand.Count;
            
            // 第一行：1-5
            var row1 = new ActionRowBuilder();
            for (int i = 0; i < Math.Min(5, cardCount); i++)
            {
                row1.WithButton(
                    label: $"第 {i + 1} 張",
                    customId: $"oldmaid_draw_{i + 1}",
                    style: ButtonStyle.Primary
                );
            }
            component.AddRow(row1);

            // 第二行：6-10
            if (cardCount > 5)
            {
                var row2 = new ActionRowBuilder();
                for (int i = 5; i < Math.Min(10, cardCount); i++)
                {
                    row2.WithButton(
                        label: $"第 {i + 1} 張",
                        customId: $"oldmaid_draw_{i + 1}",
                        style: ButtonStyle.Primary
                    );
                }
                component.AddRow(row2);
            }

            // 第三行：11-13
            if (cardCount > 10)
            {
                var row3 = new ActionRowBuilder();
                for (int i = 10; i < cardCount; i++)
                {
                    row3.WithButton(
                        label: $"第 {i + 1} 張",
                        customId: $"oldmaid_draw_{i + 1}",
                        style: ButtonStyle.Primary
                    );
                }
                component.AddRow(row3);
            }

            return component;
        }

        public OldMaidPlayer GetPlayer(SocketGuildUser user)
        {
            return _players.FirstOrDefault(p => !p.IsBot && p.User?.Id == user.Id);
        }

        public string GetPublicStatus()
        {
            var sb = new StringBuilder();
            sb.AppendLine("**目前狀況:**");

            foreach (var player in _players)
            {
                var indicator = _players[_currentPlayerIndex] == player ? "👉 " : "   ";
                var hasJoker = player.Hand.Contains(JokerCard) ? "🃏" : "";
                var botIndicator = player.IsBot ? "🤖 " : "";
                sb.AppendLine($"{indicator}{botIndicator}{player.GetDisplayName()}: {player.Hand.Count} 張牌 {hasJoker}");
            }

            if (!IsGameOver)
            {
                sb.AppendLine($"\n輪到: **{_players[_currentPlayerIndex].GetDisplayName()}**");
                
                var nextPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;
                while (_players[nextPlayerIndex].Hand.Count == 0)
                {
                    nextPlayerIndex = (nextPlayerIndex + 1) % _players.Count;
                    if (nextPlayerIndex == _currentPlayerIndex)
                        break;
                }
                
                if (_players[nextPlayerIndex].Hand.Count > 0)
                {
                    sb.AppendLine($"要從 **{_players[nextPlayerIndex].GetDisplayName()}** 抽牌（共 {_players[nextPlayerIndex].Hand.Count} 張）");
                }
            }

            return sb.ToString();
        }

        public SocketGuildUser GetCurrentPlayer()
        {
            return _players[_currentPlayerIndex].User;
        }

        public string GetCurrentPlayerName()
        {
            return _players[_currentPlayerIndex].GetDisplayName();
        }

        public bool IsCurrentPlayer(SocketGuildUser user)
        {
            if (_players[_currentPlayerIndex].IsBot)
                return false;
            return _players[_currentPlayerIndex].User.Id == user.Id;
        }

        public bool IsCurrentPlayerBot()
        {
            return _players[_currentPlayerIndex].IsBot;
        }

        public string GetTargetPlayerName()
        {
            var nextPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;
            while (_players[nextPlayerIndex].Hand.Count == 0)
            {
                nextPlayerIndex = (nextPlayerIndex + 1) % _players.Count;
                if (nextPlayerIndex == _currentPlayerIndex)
                    break;
            }
            return _players[nextPlayerIndex].GetDisplayName();
        }

        public List<string> GetPlayerNames()
        {
            return _players.Select(p => p.GetDisplayName()).ToList();
        }

        public string GetLoserName()
        {
            return _loser?.GetDisplayName() ?? "未知";
        }
    }

    public class OldMaidPlayer
    {
        public SocketGuildUser User { get; set; }
        public List<string> Hand { get; set; }
        public bool IsBot { get; set; }
        public string BotName { get; set; }

        public OldMaidPlayer(SocketGuildUser user, bool isBot)
        {
            User = user;
            Hand = new List<string>();
            IsBot = isBot;
        }

        public string GetDisplayName()
        {
            if (IsBot)
                return BotName;
            return User?.DisplayName ?? "未知玩家";
        }

        public string GetHandDisplay()
        {
            if (Hand.Count == 0)
                return "沒有牌了";

            var sb = new StringBuilder();
            for (int i = 0; i < Hand.Count; i++)
            {
                sb.Append($"`{i + 1}.` {Hand[i]}  ");
                if ((i + 1) % 5 == 0)
                    sb.AppendLine();
            }
            return sb.ToString();
        }

        public int RemovePairs()
        {
            var pairsRemoved = 0;
            var rankGroups = Hand
                .Where(c => c != "🃏")
                .GroupBy(c => c.Substring(1))
                .Where(g => g.Count() >= 2)
                .ToList();

            foreach (var group in rankGroups)
            {
                var cardsToRemove = group.Take(2).ToList();
                foreach (var card in cardsToRemove)
                {
                    Hand.Remove(card);
                }
                pairsRemoved++;
            }

            return pairsRemoved;
        }
    }
}