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
    public class RubiksCubeService
    {
        private Dictionary<ulong, RubiksCube> _activeGames = new Dictionary<ulong, RubiksCube>();
        private Dictionary<ulong, HashSet<ulong>> _gamePlayers = new Dictionary<ulong, HashSet<ulong>>();

        /// <summary>
        /// 開始新遊戲（頻道共享）
        /// </summary>
        public (ComponentBuilder, Embed) StartGame(ulong channelId, int scrambleMoves = 20)
        {
            var cube = new RubiksCube();
            cube.Scramble(scrambleMoves);
            _activeGames[channelId] = cube;
            _gamePlayers[channelId] = new HashSet<ulong>();

            var embed = CreateCubeEmbed(cube, channelId, "魔術方塊遊戲開始！所有人都可以一起玩！");
            var component = CreateButtons(channelId);

            return (component, embed);
        }

        /// <summary>
        /// 處理旋轉操作
        /// </summary>
        public async Task<(ComponentBuilder?, Embed)> HandleRotation(SocketMessageComponent component, string face, bool clockwise)
        {
            var channelId = component.Channel.Id;
            
            if (!_activeGames.TryGetValue(channelId, out var cube))
            {
                return (null, CreateErrorEmbed("找不到遊戲！請先在這個頻道開始新遊戲。"));
            }

            // 記錄玩家
            if (!_gamePlayers.ContainsKey(channelId))
            {
                _gamePlayers[channelId] = new HashSet<ulong>();
            }
            _gamePlayers[channelId].Add(component.User.Id);

            // 執行旋轉（多人同時按會把方塊轉壞 → 一次一個）
            bool solved;
            lock (cube)
            {
                cube.Rotate(face, clockwise);
                solved = cube.IsSolved();
            }

            // 檢查是否完成
            if (solved)
            {
                var playerCount = _gamePlayers[channelId].Count;
                _activeGames.Remove(channelId);
                _gamePlayers.Remove(channelId);
                
                var winEmbed = CreateCubeEmbed(cube, channelId,
                    $"🎉 恭喜完成！\n👥 共 {playerCount} 位玩家參與\n🎯 總共用了 {cube.MoveCount} 步！");
                return (null, winEmbed);
            }

            var playerCountStr = _gamePlayers[channelId].Count;
            var embed = CreateCubeEmbed(cube, channelId,
                $"🎮 {component.User.Mention} 旋轉了 {face} {(clockwise ? "順時針" : "逆時針")}\n👥 目前 {playerCountStr} 位玩家參與");
            var buttons = CreateButtons(channelId);

            return (buttons, embed);
        }

        /// <summary>
        /// 重置遊戲
        /// </summary>
        public (ComponentBuilder, Embed) ResetGame(ulong channelId, int scrambleMoves = 20)
        {
            return StartGame(channelId, scrambleMoves);
        }

        /// <summary>
        /// 創建魔術方塊的視覺化 Embed（直式排列）
        /// </summary>
        private Embed CreateCubeEmbed(RubiksCube cube, ulong channelId, string message)
        {
            var embedBuilder = new EmbedBuilder()
                .WithTitle("🎲 魔術方塊 (多人共玩)")
                .WithDescription(message)
                .WithColor(Color.Orange)
                .AddField("步數", cube.MoveCount.ToString(), true)
                .AddField("狀態", cube.IsSolved() ? "✅ 完成" : "🎯 進行中", true)
                .AddField("\u200b", "\u200b") // 空行
                .AddField("魔術方塊狀態", $"```\n{cube.GetVisualRepresentation()}\n```", false)
                .WithFooter($"頻道共享遊戲 | 任何人都可以操作")
                .WithCurrentTimestamp();

            return embedBuilder.Build();
        }

        /// <summary>
        /// 創建操作按鈕（移除用戶ID限制）
        /// </summary>
        private ComponentBuilder CreateButtons(ulong channelId)
        {
            var builder = new ComponentBuilder();

            // 第一排: F/B面操作
            builder.WithButton("前面 順時針", $"cube_F_1", ButtonStyle.Primary, new Emoji("🔵"));
            builder.WithButton("前面 逆時針", $"cube_F_0", ButtonStyle.Secondary, new Emoji("⚪"));
            builder.WithButton("後面 順時針", $"cube_B_1", ButtonStyle.Primary, new Emoji("🔵"));
            builder.WithButton("後面 逆時針", $"cube_B_0", ButtonStyle.Secondary, new Emoji("⚪"));

            // 第二排: U/D面操作
            builder.WithButton("上面 順時針", $"cube_U_1", ButtonStyle.Success, new Emoji("🟢"), row: 1);
            builder.WithButton("上面 逆時針", $"cube_U_0", ButtonStyle.Secondary, new Emoji("⚪"), row: 1);
            builder.WithButton("下面 順時針", $"cube_D_1", ButtonStyle.Success, new Emoji("🟢"), row: 1);
            builder.WithButton("下面 逆時針", $"cube_D_0", ButtonStyle.Secondary, new Emoji("⚪"), row: 1);

            // 第三排: L/R面操作
            builder.WithButton("左面 順時針", $"cube_L_1", ButtonStyle.Danger, new Emoji("🔴"), row: 2);
            builder.WithButton("左面 逆時針", $"cube_L_0", ButtonStyle.Secondary, new Emoji("⚪"), row: 2);
            builder.WithButton("右面 順時針", $"cube_R_1", ButtonStyle.Danger, new Emoji("🔴"), row: 2);
            builder.WithButton("右面 逆時針", $"cube_R_0", ButtonStyle.Secondary, new Emoji("⚪"), row: 2);

            // 第四排: 控制按鈕
            builder.WithButton("🔄 重新打亂", $"cube_RESET_0", ButtonStyle.Secondary, row: 3);
            builder.WithButton("❌ 結束遊戲", $"cube_END_0", ButtonStyle.Danger, row: 3);

            return builder;
        }

        /// <summary>
        /// 創建錯誤 Embed
        /// </summary>
        private Embed CreateErrorEmbed(string message)
        {
            return new EmbedBuilder()
                .WithTitle("❌ 錯誤")
                .WithDescription(message)
                .WithColor(Color.Red)
                .WithCurrentTimestamp()
                .Build();
        }

        /// <summary>
        /// 結束遊戲
        /// </summary>
        public Embed EndGame(ulong channelId)
        {
            var playerCount = 0;
            if (_gamePlayers.ContainsKey(channelId))
            {
                playerCount = _gamePlayers[channelId].Count;
            }

            _activeGames.Remove(channelId);
            _gamePlayers.Remove(channelId);
            
            return new EmbedBuilder()
                .WithTitle("遊戲結束")
                .WithDescription($"感謝遊玩魔術方塊！\n👥 共有 {playerCount} 位玩家參與過")
                .WithColor(Color.LightGrey)
                .WithCurrentTimestamp()
                .Build();
        }
    }
}