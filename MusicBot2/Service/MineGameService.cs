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
    public class MineGameService
    {
        private readonly Dictionary<ulong, MineGameVM> _activeGames = new Dictionary<ulong, MineGameVM>();

        public MineGameService()
        {
        }

        // 開始新遊戲（小版本，使用按鈕）
        public Task<(ComponentBuilder, Embed)> StartGameAsync(ulong userId, int height, int width)
        {
            try
            {
                //隨機生成地雷，地雷數量隨機生成，範圍在場地的 20% 到 30% 之間
                //int minMines = (int)(height * width * 0.1);
                //int maxMines = (int)(height * width * 0.2);
                //int mines = new Random().Next(minMines, maxMines + 1);

                // 初始化遊戲狀態
                // 按鈕版最多 5x5；地雷數不能比格子多（以前 2x2 以下會在放地雷時無限迴圈）
                width = Math.Clamp(width, 3, 5);
                height = Math.Clamp(height, 3, 5);
                var gameState = new MineGameVM
                {
                    Width = width,
                    Height = height,
                    Mines = Math.Min(4, width * height - 1)
                };

                gameState.MineMap = new bool[gameState.Width, gameState.Height];
                gameState.Revealed = new bool[gameState.Width, gameState.Height];

                // 生成地雷
                GenerateMines(gameState);

                // 自動揭開第一格（確保是安全的）
                RevealFirstSafeCell(gameState);

                // 儲存遊戲狀態
                _activeGames[userId] = gameState;

                // 建立按鈕網格
                var component = BuildGameBoard(gameState, userId);

                // 建立 Embed
                var embed = new EmbedBuilder()
                {
                    Title = "👽 花蓮散步模擬器",
                    Description = $"🎮 開始摟\n" +
                                  $"📊 地圖大小: {gameState.Width}x{gameState.Height}\n" +
                                  $"💣 外星人數量: {gameState.Mines}\n" +
                                  $"✨ 已經踩第一格ㄌ，剩下的請自己靠賽吧寶貝",
                    Color = Color.Blue
                }.Build();

                return Task.FromResult((component, embed));
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                {
                    Title = "❌ 錯誤",
                    Description = $"@zu_tomayo 看一下啦: {ex.Message}",
                    Color = Color.Red
                }.Build();
                var builder = new ComponentBuilder();
                return Task.FromResult((builder, errorEmbed));
            }
        }

        // 開始超大版遊戲（使用文字座標輸入）
        public Task<(ComponentBuilder, Embed)> StartBiggerGameAsync(ulong userId, int width, int height)
        {
            try
            {
                // 限制最大尺寸
                if (width > 20 || height > 20)
                {
                    var errorEmbed = new EmbedBuilder()
                    {
                        Title = "❌ 好大 太大 我塞不下",
                        Description = "最大只能 20x20，有問題跟discord靠北",
                        Color = Color.Red
                    }.Build();
                    return Task.FromResult((new ComponentBuilder(), errorEmbed));
                }

                if (width < 5 || height < 5)
                {
                    var errorEmbed = new EmbedBuilder()
                    {
                        Title = "❌ 小到靠北",
                        Description = "要5*5去/mine好爆",
                        Color = Color.Red
                    }.Build();
                    return Task.FromResult((new ComponentBuilder(), errorEmbed));
                }

                //隨機生成地雷
                int minMines = (int)(height * width * 0.15);
                int maxMines = (int)(height * width * 0.25);
                int mines = new Random().Next(minMines, maxMines + 1);

                var gameState = new MineGameVM
                {
                    Width = width,
                    Height = height,
                    Mines = mines
                };

                gameState.MineMap = new bool[gameState.Width, gameState.Height];
                gameState.Revealed = new bool[gameState.Width, gameState.Height];

                GenerateMines(gameState);
                RevealFirstSafeCell(gameState);

                _activeGames[userId] = gameState;

                var embed = BuildBigGameEmbed(gameState, userId, "👽 花蓮散步模擬器 (超大版)", 
                    $"🎮 開始摟\n使用 `$$mine x,y` 來揭開格子\n例如: `$$mineopen 3 5`");

                return Task.FromResult((new ComponentBuilder(), embed));
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

        // 處理文字座標輸入
        public Task<Embed> HandleTextCoordinate(ulong userId, int x, int y)
        {
            try
            {
                if (!_activeGames.ContainsKey(userId))
                {
                    return Task.FromResult(new EmbedBuilder()
                    {
                        Title = "❌ 錯誤",
                        Description = "你沒有進行中的遊戲呀寶貝\n使用 `/minebig 寬度 高度` 開始新遊戲",
                        Color = Color.Red
                    }.Build());
                }

                var game = _activeGames[userId];

                // 檢查座標是否有效
                if (x < 0 || x >= game.Width || y < 0 || y >= game.Height)
                {
                    return Task.FromResult(BuildBigGameEmbed(game, userId, "❌ 座標錯誤",
                        $"座標超出範圍，你打到哪裡去了? 有效範圍: 0-{game.Width - 1}, 0-{game.Height - 1}"));
                }

                // 檢查是否已揭開
                if (game.Revealed[x, y])
                {
                    return Task.FromResult(BuildBigGameEmbed(game, userId, "⚠️ 提示",
                        "點已經開的衝三小?"));
                }

                // 揭開格子
                game.Revealed[x, y] = true;

                // 檢查是否踩到地雷
                if (game.MineMap[x, y])
                {
                    RevealAllMines(game);
                    _activeGames.Remove(userId); // 輸了就結束，不能繼續踩
                    var loseEmbed = BuildBigGameEmbed(game, userId, "💥 遊戲結束",
                        $"你在 ({x + 1},{y + 1}) 踩到外星人了\n請到花蓮地檢署一趟\n💀 Game Over");
                    return Task.FromResult(loseEmbed);
                }

                // 自動揭開周圍
                int mineCount = CountAdjacentMines(game, x, y);
                if (mineCount == 0)
                {
                    RevealAdjacentCells(game, x, y);
                }

                // 檢查勝利
                if (CheckWin(game))
                {
                    RevealAllMines(game);
                    _activeGames.Remove(userId);
                    var winEmbed = BuildBigGameEmbed(game, userId, "🎉 恭喜你贏了",
                        $"基本上你跟faker最大的差別只在他有去打職業，而你還沒有\n");
                    return Task.FromResult(winEmbed);
                }

                // 繼續遊戲
                return Task.FromResult(BuildBigGameEmbed(game, userId, "👽 花蓮散步模擬器",
                    $"✅ 安全！剩餘格子: {CountRemainingCells(game)}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new EmbedBuilder()
                {
                    Title = "❌ 錯誤",
                    Description = $"發生錯誤: {ex.Message}",
                    Color = Color.Red
                }.Build());
            }
        }

        // 建立超大版遊戲的文字地圖 Embed
        private Embed BuildBigGameEmbed(MineGameVM game, ulong userId, string title, string description)
        {
            var mapString = BuildTextMap(game);
            
            var embed = new EmbedBuilder()
            {
                Title = title,
                Description = description,
                Color = Color.Blue
            };

            embed.AddField("📊 遊戲資訊",
                $"地圖: {game.Width}x{game.Height}\n" +
                $"外星人: {game.Mines}\n" +
                $"剩餘: {CountRemainingCells(game)}");

            embed.AddField("🗺️ 地圖", $"```\n{mapString}\n```");

            embed.WithFooter($"使用 $$mine x,y 來揭開格子 | 例: $$mine 3,5");

            return embed.Build();
        }

        // 建立文字地圖
        private string BuildTextMap(MineGameVM game)
        {
            var sb = new StringBuilder();

            // 頂部座標軸 (從 1 開始)
            sb.Append("    "); // 左側留空給 Y 軸
            for (int x = 0; x < game.Width; x++)
            {
                int displayX = x + 1;
                if (displayX < 10)
                {
                    sb.Append($"{displayX} ");
                }
                else
                {
                    sb.Append($"{displayX}");
                }
            }
            sb.AppendLine();

            // 分隔線
            sb.Append("   +");
            for (int x = 0; x < game.Width; x++)
            {
                sb.Append("--");
            }
            sb.AppendLine();

            // 地圖內容
            for (int y = 0; y < game.Height; y++)
            {
                int displayY = y + 1;
                if (displayY < 10)
                {
                    sb.Append($" {displayY}|");
                }
                else
                {
                    sb.Append($"{displayY}|");
                }
                
                for (int x = 0; x < game.Width; x++)
                {
                    if (game.Revealed[x, y])
                    {
                        if (game.MineMap[x, y])
                        {
                            sb.Append("💣");
                        }
                        else
                        {
                            int count = CountAdjacentMines(game, x, y);
                            sb.Append(count == 0 ? "□ " : $"{count} ");
                        }
                    }
                    else
                    {
                        sb.Append("■ ");
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // 處理按鈕點擊（小版本用）
        public Task<(ComponentBuilder?, Embed, bool)> HandleButtonClick(SocketMessageComponent interaction, int x, int y)
        {
            try
            {
                // customId 裡的擁有者（Program 已擋掉別人點）
                var idParts = interaction.Data.CustomId.Split('_');
                ulong userId = idParts.Length >= 2 && ulong.TryParse(idParts[1], out var ownerId) ? ownerId : interaction.User.Id;

                if (!_activeGames.ContainsKey(userId))
                {
                    var errorEmbed = new EmbedBuilder()
                    {
                        Title = "❌ 錯誤",
                        Description = "現在要馬沒遊戲，要馬就是有bug，請跟豬頭馬又哭",
                        Color = Color.Red
                    }.Build();
                    return Task.FromResult<(ComponentBuilder?, Embed, bool)>((null, errorEmbed, false));
                }

                var game = _activeGames[userId];

                if (game.Revealed[x, y])
                {
                    var alreadyEmbed = new EmbedBuilder()
                    {
                        Title = "⚠️ 提示",
                        Description = "點已經開的衝三小?",
                        Color = Color.Orange
                    }.Build();
                    return Task.FromResult<(ComponentBuilder?, Embed, bool)>((BuildGameBoard(game, userId), alreadyEmbed, false));
                }

                game.Revealed[x, y] = true;

                if (game.MineMap[x, y])
                {
                    RevealAllMines(game);
                    var loseEmbed = new EmbedBuilder()
                    {
                        Title = "💥 遊戲結束",
                        Description = "你踩到外星人了，請到花蓮地檢署一趟\n" +
                                      "💀 Game Over\n" +
                                      "🔄 輸入 /mine 再開始新遊戲",
                        Color = Color.Red
                    }.Build();

                    _activeGames.Remove(userId); // 結束就釋放
                    interaction.Channel.SendMessageAsync($"{interaction.User.Username} / {interaction.User.GlobalName} 收到了外星人的傳票");
                    return Task.FromResult<(ComponentBuilder?, Embed, bool)>((BuildGameBoard(game, userId, true), loseEmbed, true));
                }

                int mineCount = CountAdjacentMines(game, x, y);
                if (mineCount == 0)
                {
                    RevealAdjacentCells(game, x, y);
                }

                if (CheckWin(game))
                {
                    RevealAllMines(game);
                    var winEmbed = new EmbedBuilder()
                    {
                        Title = "🎉 恭喜你贏摟",
                        Description = "你成功避開外星人的傳票了 \n" +
                                      "你基本上跟faker最大的差別只在他有去打職業，而你還沒有 \n" +
                                      "🔄 輸入 /mine 再開始新遊戲",
                        Color = Color.Gold
                    }.Build();
                    _activeGames.Remove(userId); // 結束就釋放
                    return Task.FromResult<(ComponentBuilder?, Embed, bool)>((BuildGameBoard(game, userId, true), winEmbed, true));
                }

                var continueEmbed = new EmbedBuilder()
                {
                    Title = "👽 花蓮散步模擬器",
                    Description = $"✅ 安全！繼續加油！\n" +
                                  $"📊 剩餘格子: {CountRemainingCells(game)}",
                    Color = Color.Green
                }.Build();

                return Task.FromResult<(ComponentBuilder?, Embed, bool)>((BuildGameBoard(game, userId), continueEmbed, false));
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                {
                    Title = "❌ 按鈕點擊錯誤",
                    Description = $"發生錯誤: {ex.Message}\n堆疊: {ex.StackTrace}",
                    Color = Color.Red
                }.Build();
                return Task.FromResult<(ComponentBuilder?, Embed, bool)>((null, errorEmbed, true));
            }
        }

        // 以下是共用方法...
        private void GenerateMines(MineGameVM game)
        {
            Random rand = new Random();
            int placedMines = 0;

            while (placedMines < game.Mines)
            {
                int x = rand.Next(game.Width);
                int y = rand.Next(game.Height);

                if (!game.MineMap[x, y])
                {
                    game.MineMap[x, y] = true;
                    placedMines++;
                }
            }
        }

        private void RevealFirstSafeCell(MineGameVM game)
        {
            // 優先尋找周圍沒有地雷的格子 (數字 0)
            for (int y = 0; y < game.Height; y++)
            {
                for (int x = 0; x < game.Width; x++)
                {
                    if (!game.MineMap[x, y] && CountAdjacentMines(game, x, y) == 0)
                    {
                        game.Revealed[x, y] = true;
                        RevealAdjacentCells(game, x, y);  // 自動展開安全區域
                        return;
                    }
                }
            }
            
            // 如果找不到完全安全的 (地雷太多的情況)，退而求其次找數字最小的
            int minMines = int.MaxValue;
            int bestX = 0, bestY = 0;
            
            for (int y = 0; y < game.Height; y++)
            {
                for (int x = 0; x < game.Width; x++)
                {
                    if (!game.MineMap[x, y])
                    {
                        int count = CountAdjacentMines(game, x, y);
                        if (count < minMines)
                        {
                            minMines = count;
                            bestX = x;
                            bestY = y;
                        }
                    }
                }
            }
            
            game.Revealed[bestX, bestY] = true;
            if (minMines == 0)
            {
                RevealAdjacentCells(game, bestX, bestY);
            }
        }

        private ComponentBuilder BuildGameBoard(MineGameVM game, ulong userId, bool gameOver = false)
        {
            var builder = new ComponentBuilder();

            for (int y = 0; y < game.Height; y++)
            {
                var row = new ActionRowBuilder();

                for (int x = 0; x < game.Width; x++)
                {
                    string label;
                    ButtonStyle style;

                    if (game.Revealed[x, y])
                    {
                        if (game.MineMap[x, y])
                        {
                            label = "👽";
                            style = ButtonStyle.Danger;
                        }
                        else
                        {
                            int count = CountAdjacentMines(game, x, y);
                            label = count == 0 ? "⬜" : count.ToString();
                            style = count switch
                            {
                                0 => ButtonStyle.Secondary,
                                1 => ButtonStyle.Primary,
                                2 => ButtonStyle.Success,
                                _ => ButtonStyle.Danger
                            };
                        }
                    }
                    else
                    {
                        label = "❓";
                        style = ButtonStyle.Secondary;
                    }

                    row.WithButton(label, $"mine_{userId}_{x}_{y}", style, disabled: gameOver);
                }

                builder.AddRow(row);
            }

            return builder;
        }

        private int CountAdjacentMines(MineGameVM game, int x, int y)
        {
            int count = 0;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int nx = x + dx;
                    int ny = y + dy;

                    if (nx >= 0 && nx < game.Width && ny >= 0 && ny < game.Height)
                    {
                        if (game.MineMap[nx, ny])
                        {
                            count++;
                        }
                    }
                }
            }
            return count;
        }

        private void RevealAdjacentCells(MineGameVM game, int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int nx = x + dx;
                    int ny = y + dy;

                    if (nx >= 0 && nx < game.Width && ny >= 0 && ny < game.Height && !game.Revealed[nx, ny])
                    {
                        game.Revealed[nx, ny] = true;  // ✅ 修正：應該是 [nx, ny] 不是 [nx, y]

                        int adjacentMines = CountAdjacentMines(game, nx, ny);

                        // 如果周圍也是 0，繼續遞迴展開
                        if (adjacentMines == 0)
                        {
                            RevealAdjacentCells(game, nx, ny);
                        }
                    }
                }
            }
        }

        private void RevealAllMines(MineGameVM game)
        {
            for (int x = 0; x < game.Width; x++)
            {
                for (int y = 0; y < game.Height; y++)
                {
                    if (game.MineMap[x, y])
                    {
                        game.Revealed[x, y] = true;
                    }
                }
            }
        }

        private bool CheckWin(MineGameVM game)
        {
            for (int x = 0; x < game.Width; x++)
            {
                for (int y = 0; y < game.Height; y++)
                {
                    if (!game.MineMap[x, y] && !game.Revealed[x, y])
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private int CountRemainingCells(MineGameVM game)
        {
            int count = 0;
            for (int x = 0; x < game.Width; x++)
            {
                for (int y = 0; y < game.Height; y++)
                {
                    if (!game.MineMap[x, y] && !game.Revealed[x, y])
                    {
                        count++;
                    }
                }
            }
            return count;
        }
    }
}
