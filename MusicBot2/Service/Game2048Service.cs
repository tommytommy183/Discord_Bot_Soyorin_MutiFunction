using Discord;
using Discord.WebSocket;
using MusicBot2.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class Game2048Service
    {
        private readonly Dictionary<ulong, Game2048VM> _activeGames = new Dictionary<ulong, Game2048VM>();
        private readonly Random _random = new Random();

        // 開始新遊戲
        public Task<(ComponentBuilder, Embed)> StartGameAsync(ulong channelId)
        {
            try
            {
                var gameState = new Game2048VM
                {
                    ChannelId = channelId
                };

                // 初始化空棋盤
                for (int i = 0; i < gameState.Size; i++)
                {
                    for (int j = 0; j < gameState.Size; j++)
                    {
                        gameState.Grid[i, j] = 0;
                    }
                }

                // 添加兩個初始方塊
                AddRandomTile(gameState);
                AddRandomTile(gameState);

                _activeGames[channelId] = gameState;

                var component = BuildGameBoard(gameState);
                var embed = BuildEmbed(gameState, "🎮 2048 遊戲", "合併相同數字的方塊，目標達成 2048！");

                return Task.FromResult((component, embed));
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                {
                    Title = "❌ 錯誤",
                    Description = $"發生錯誤: {ex.Message}",
                    Color = Color.Red
                }.Build();
                return Task.FromResult((new ComponentBuilder(), errorEmbed));
            }
        }

        // 處理按鈕點擊
        public Task<(ComponentBuilder, Embed)> HandleButtonClick(SocketMessageComponent component, string direction)
        {
            try
            {
                ulong channelId = component.Channel.Id;

                if (!_activeGames.ContainsKey(channelId))
                {
                    var errorEmbed = new EmbedBuilder()
                    {
                        Title = "❌ 找不到遊戲",
                        Description = "請先使用 /2048 開始新遊戲",
                        Color = Color.Red
                    }.Build();
                    return Task.FromResult((new ComponentBuilder(), errorEmbed));
                }

                var gameState = _activeGames[channelId];

                if (gameState.GameOver)
                {
                    var gameOverEmbed = BuildEmbed(gameState, "🎮 遊戲已結束", "請使用 /2048 開始新遊戲");
                    return Task.FromResult((new ComponentBuilder(), gameOverEmbed));
                }

                bool moved = false;
                int[,] previousGrid = (int[,])gameState.Grid.Clone();

                // 根據方向移動
                switch (direction.ToUpper())
                {
                    case "UP":
                        moved = MoveUp(gameState);
                        break;
                    case "DOWN":
                        moved = MoveDown(gameState);
                        break;
                    case "LEFT":
                        moved = MoveLeft(gameState);
                        break;
                    case "RIGHT":
                        moved = MoveRight(gameState);
                        break;
                    case "RESET":
                        return StartGameAsync(gameState.ChannelId);
                }

                // 如果有移動，添加新方塊
                if (moved)
                {
                    AddRandomTile(gameState);

                    // 檢查是否獲勝
                    if (!gameState.Won && CheckWin(gameState))
                    {
                        gameState.Won = true;
                        var winEmbed = BuildEmbed(gameState, "🎉 恭喜獲勝！", "你達成了 2048！可以繼續挑戰更高分數");
                        var winComponent = BuildGameBoard(gameState);
                        return Task.FromResult((winComponent, winEmbed));
                    }

                    // 檢查遊戲是否結束
                    if (IsGameOver(gameState))
                    {
                        gameState.GameOver = true;
                        var loseEmbed = BuildEmbed(gameState, "😢 遊戲結束", "沒有可移動的空間了！");
                        return Task.FromResult((new ComponentBuilder(), loseEmbed));
                    }
                }

                var component2 = BuildGameBoard(gameState);
                var embed = BuildEmbed(gameState, "🎮 2048", moved ? "移動成功！" : "無法往這個方向移動");

                return Task.FromResult((component2, embed));
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                {
                    Title = "❌ 錯誤",
                    Description = $"發生錯誤: {ex.Message}",
                    Color = Color.Red
                }.Build();
                return Task.FromResult((new ComponentBuilder(), errorEmbed));
            }
        }

        // 添加隨機方塊
        private void AddRandomTile(Game2048VM gameState)
        {
            var emptyCells = new List<(int, int)>();
            for (int i = 0; i < gameState.Size; i++)
            {
                for (int j = 0; j < gameState.Size; j++)
                {
                    if (gameState.Grid[i, j] == 0)
                    {
                        emptyCells.Add((i, j));
                    }
                }
            }

            if (emptyCells.Count > 0)
            {
                var (row, col) = emptyCells[_random.Next(emptyCells.Count)];
                
                gameState.Grid[row, col] = _random.Next(10) < 9 ? 2 : 4; // 90% 機率出現 2，10% 機率出現 4
            }
        }

        // 向上移動
        private bool MoveUp(Game2048VM gameState)
        {
            bool moved = false;
            for (int col = 0; col < gameState.Size; col++)
            {
                int[] column = new int[gameState.Size];
                for (int row = 0; row < gameState.Size; row++)
                {
                    column[row] = gameState.Grid[row, col];
                }

                int[] newColumn = MergeAndShift(column, gameState);
                for (int row = 0; row < gameState.Size; row++)
                {
                    if (gameState.Grid[row, col] != newColumn[row])
                    {
                        moved = true;
                    }
                    gameState.Grid[row, col] = newColumn[row];
                }
            }
            return moved;
        }

        // 向下移動
        private bool MoveDown(Game2048VM gameState)
        {
            bool moved = false;
            for (int col = 0; col < gameState.Size; col++)
            {
                int[] column = new int[gameState.Size];
                for (int row = 0; row < gameState.Size; row++)
                {
                    column[row] = gameState.Grid[gameState.Size - 1 - row, col];
                }

                int[] newColumn = MergeAndShift(column, gameState);
                for (int row = 0; row < gameState.Size; row++)
                {
                    if (gameState.Grid[gameState.Size - 1 - row, col] != newColumn[row])
                    {
                        moved = true;
                    }
                    gameState.Grid[gameState.Size - 1 - row, col] = newColumn[row];
                }
            }
            return moved;
        }

        // 向左移動
        private bool MoveLeft(Game2048VM gameState)
        {
            bool moved = false;
            for (int row = 0; row < gameState.Size; row++)
            {
                int[] rowArray = new int[gameState.Size];
                for (int col = 0; col < gameState.Size; col++)
                {
                    rowArray[col] = gameState.Grid[row, col];
                }

                int[] newRow = MergeAndShift(rowArray, gameState);
                for (int col = 0; col < gameState.Size; col++)
                {
                    if (gameState.Grid[row, col] != newRow[col])
                    {
                        moved = true;
                    }
                    gameState.Grid[row, col] = newRow[col];
                }
            }
            return moved;
        }

        // 向右移動
        private bool MoveRight(Game2048VM gameState)
        {
            bool moved = false;
            for (int row = 0; row < gameState.Size; row++)
            {
                int[] rowArray = new int[gameState.Size];
                for (int col = 0; col < gameState.Size; col++)
                {
                    rowArray[col] = gameState.Grid[row, gameState.Size - 1 - col];
                }

                int[] newRow = MergeAndShift(rowArray, gameState);
                for (int col = 0; col < gameState.Size; col++)
                {
                    if (gameState.Grid[row, gameState.Size - 1 - col] != newRow[col])
                    {
                        moved = true;
                    }
                    gameState.Grid[row, gameState.Size - 1 - col] = newRow[col];
                }
            }
            return moved;
        }

        // 合併並移動
        private int[] MergeAndShift(int[] line, Game2048VM gameState)
        {
            // 移除 0
            var nonZero = line.Where(x => x != 0).ToArray();
            var result = new int[line.Length];

            int index = 0;
            for (int i = 0; i < nonZero.Length; i++)
            {
                if (i < nonZero.Length - 1 && nonZero[i] == nonZero[i + 1])
                {
                    result[index] = nonZero[i] * 2;
                    gameState.Score += result[index];

                    if (result[index] > gameState.LargestNum)
                    {
                        gameState.LargestNum = result[index];
                    }
                    i++; // 跳過下一個
                }
                else
                {
                    result[index] = nonZero[i];
                }
                index++;
            }

            return result;
        }

        // 檢查是否獲勝
        private bool CheckWin(Game2048VM gameState)
        {
            for (int i = 0; i < gameState.Size; i++)
            {
                for (int j = 0; j < gameState.Size; j++)
                {
                    if (gameState.Grid[i, j] == 2048)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // 檢查遊戲是否結束
        private bool IsGameOver(Game2048VM gameState)
        {
            // 檢查是否還有空格
            for (int i = 0; i < gameState.Size; i++)
            {
                for (int j = 0; j < gameState.Size; j++)
                {
                    if (gameState.Grid[i, j] == 0)
                    {
                        return false;
                    }
                }
            }

            // 檢查是否可以合併
            for (int i = 0; i < gameState.Size; i++)
            {
                for (int j = 0; j < gameState.Size; j++)
                {
                    if (j < gameState.Size - 1 && gameState.Grid[i, j] == gameState.Grid[i, j + 1])
                    {
                        return false;
                    }
                    if (i < gameState.Size - 1 && gameState.Grid[i, j] == gameState.Grid[i + 1, j])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // 建立遊戲棋盤
        private ComponentBuilder BuildGameBoard(Game2048VM gameState)
        {
            var builder = new ComponentBuilder();

            // 第一行：向上按鈕和重置
            builder.WithButton("x", $"2048_SPACE1_{gameState.ChannelId}", ButtonStyle.Secondary, disabled: true);
            builder.WithButton("⬆️ 上", $"2048_UP_{gameState.ChannelId}", ButtonStyle.Primary);
            builder.WithButton("x", $"2048_SPACE2_{gameState.ChannelId}", ButtonStyle.Secondary, disabled: true);
            builder.WithButton("🔄 重置", $"2048_RESET_{gameState.ChannelId}", ButtonStyle.Danger);

            // 第二行：左、下、右按鈕
            builder.WithButton("⬅️ 左", $"2048_LEFT_{gameState.ChannelId}", ButtonStyle.Primary, row: 1);
            builder.WithButton("⬇️ 下", $"2048_DOWN_{gameState.ChannelId}", ButtonStyle.Primary, row: 1);
            builder.WithButton("➡️ 右", $"2048_RIGHT_{gameState.ChannelId}", ButtonStyle.Primary, row: 1);

            return builder;
        }

        // 建立 Embed
        private Embed BuildEmbed(Game2048VM gameState, string title, string description)
        {
            var sb = new StringBuilder();

            // 使用更清晰的方塊顯示
            for (int i = 0; i < gameState.Size; i++)
            {
                for (int j = 0; j < gameState.Size; j++)
                {
                    if (gameState.Grid[i, j] == 0)
                    {
                        sb.Append("⬜");
                    }
                    else
                    {
                        sb.Append(GetTileEmoji(gameState.Grid[i, j]));
                    }
                }
                sb.AppendLine();
            }

            var color = gameState.GameOver ? Color.Red : (gameState.Won ? Color.Gold : Color.Blue);

            var embed = new EmbedBuilder()
            {
                Title = title,
                Description = description,
                Color = color
            };

            embed.AddField("🎮 遊戲棋盤", sb.ToString(), false);
            embed.AddField("📊 分數", gameState.Score.ToString(), true);

            if (gameState.Won)
            {
                embed.AddField("狀態", "🎉 獲勝！達成 2048！", true);
            }
            else if (gameState.GameOver)
            {
                embed.AddField("狀態", "😢 遊戲結束", true);
            }
            else
            {
                embed.AddField("狀態", "🎯 進行中", true);
            }

            embed.WithFooter("使用下方按鈕移動方塊 | 🔄 重新開始");

            return embed.Build();
        }

        // 取得方塊對應的 Emoji
        private string GetTileEmoji(int value)
        {
            return value switch
            {
                2 => "<:kc1:1511607011253551194>", //1
                4 => "<:kc2:1511607127825715231>",//2
                8 => "<:kc4:1511607123765628959>",//4
                16 => "<:kc7:1511607109035495444>",//7
                32 => "<:kc6:1511607110700498944>",//6
                64 => "<:kc9:1511607103377379429>",//9
                128 => "<:kc13:1511607096473288835>",//13
                256 => "<:kc3:1511607126064103424>",//3
                512 => "<:kc10:1511607101376692305>",//10
                1024 => "<:kc11:1511607099732529203>",//11
                2048 => "<:kc12:1511607098134495262>",//12
                _ => "⭐"
            };
        }

        // 重置遊戲
        public void ResetGame(ulong channelId)
        {
            if (_activeGames.ContainsKey(channelId))
            {
                _activeGames.Remove(channelId);
            }
        }

        // 獲取遊戲狀態
        public Game2048VM GetGameState(ulong channelId)
        {
            return _activeGames.ContainsKey(channelId) ? _activeGames[channelId] : null;
        }
    }
}
