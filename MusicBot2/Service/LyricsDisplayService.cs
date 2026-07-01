using Discord;
using Discord.WebSocket;
using MusicBot2.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class LyricsDisplayService
    {
        private readonly LyrisService _lyrisService;
        private readonly ConcurrentDictionary<ulong, LyricsSession> _activeSessions = new();

        public LyricsDisplayService(LyrisService lyrisService)
        {
            _lyrisService = lyrisService;
        }

        /// <summary>
        /// 創建歌詞控制按鈕
        /// </summary>
        private ComponentBuilder CreateLyricsButtons(ulong channelId, int currentLine, int totalLines)
        {
            var builder = new ComponentBuilder();

            // 第一排：上一句和下一句
            builder.WithButton("?? 上一句", $"lyrics_prev_{channelId}", ButtonStyle.Primary, disabled: currentLine <= 0)
                   .WithButton("下一句 ??", $"lyrics_next_{channelId}", ButtonStyle.Primary, disabled: currentLine >= totalLines - 1);

            return builder;
        }

        /// <summary>
        /// 開始顯示同步歌詞（手動控制模式）
        /// </summary>
        public async Task<bool> StartLyricsDisplayAsync(ulong channelId, string trackName, string artistName, IMessageChannel messageChannel)
        {
            try
            {
                // 獲取歌詞
                var lyrics = await _lyrisService.GetLyricsAsync(trackName, artistName);
                if (lyrics == null || string.IsNullOrWhiteSpace(lyrics.syncedLyrics))
                {
                    await messageChannel.SendMessageAsync($"?? 找不到 **{trackName}** 的同步歌詞");
                    return false;
                }

                // 解析同步歌詞
                var syncedLines = _lyrisService.ParseSyncedLyrics(lyrics.syncedLyrics);
                if (syncedLines.Count == 0)
                {
                    await messageChannel.SendMessageAsync($"?? 歌詞格式錯誤");
                    return false;
                }

                // 停止現有的歌詞顯示（如果有）
                StopLyricsDisplay(channelId);

                // 創建新的歌詞會話
                var session = new LyricsSession
                {
                    TrackName = lyrics.trackName,
                    ArtistName = lyrics.artistName,
                    SyncedLines = syncedLines,
                    MessageChannel = messageChannel,
                    CurrentLineIndex = 0
                };

                _activeSessions[channelId] = session;

                // 發送初始訊息
                await UpdateLyricsMessageAsync(session, 0);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LYRICS ERROR] 啟動歌詞顯示失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止顯示歌詞
        /// </summary>
        public void StopLyricsDisplay(ulong channelId)
        {
            if (_activeSessions.TryRemove(channelId, out var session))
            {
                // 清理資源（如果有的話）
            }
        }

        /// <summary>
        /// 處理按鈕點擊事件
        /// </summary>
        public async Task<bool> HandleButtonAsync(SocketMessageComponent component)
        {
            var customId = component.Data.CustomId;

            if (!customId.StartsWith("lyrics_"))
                return false;

            try
            {
                var parts = customId.Split('_');
                if (parts.Length != 3)
                    return false;

                var action = parts[1]; // "prev" or "next"
                var channelId = ulong.Parse(parts[2]);

                if (!_activeSessions.TryGetValue(channelId, out var session))
                {
                    await component.RespondAsync("?? 找不到歌詞會話，請重新開始", ephemeral: true);
                    return true;
                }

                // 計算新的行索引
                int newIndex = session.CurrentLineIndex;
                if (action == "prev")
                {
                    newIndex = Math.Max(0, session.CurrentLineIndex - 1);
                }
                else if (action == "next")
                {
                    newIndex = Math.Min(session.SyncedLines.Count - 1, session.CurrentLineIndex + 1);
                }

                // 更新當前行
                session.CurrentLineIndex = newIndex;

                // 更新訊息
                await UpdateLyricsMessageAsync(session, newIndex);
                await component.DeferAsync();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LYRICS ERROR] 處理按鈕失敗: {ex.Message}");
                await component.RespondAsync("? 處理按鈕時發生錯誤", ephemeral: true);
                return true;
            }
        }

        /// <summary>
        /// 更新歌詞訊息
        /// </summary>
        private async Task UpdateLyricsMessageAsync(LyricsSession session, int currentLineIndex)
        {
            try
            {
                // 顯示前後幾行歌詞
                const int contextLines = 3;
                var startIndex = Math.Max(0, currentLineIndex - contextLines);
                var endIndex = Math.Min(session.SyncedLines.Count - 1, currentLineIndex + contextLines);

                var lyricsText = "";
                for (int i = startIndex; i <= endIndex; i++)
                {
                    var line = session.SyncedLines[i];
                    if (i == currentLineIndex)
                    {
                        // 當前行用粗體和特殊符號標記
                        lyricsText += $"**? {line.line}**\n";
                    }
                    else if (i == currentLineIndex - 1 || i == currentLineIndex + 1)
                    {
                        // 前後一行稍微強調
                        lyricsText += $"  {line.line}\n";
                    }
                    else
                    {
                        // 其他行淡化顯示
                        lyricsText += $"  _{line.line}_\n";
                    }
                }

                var embed = new EmbedBuilder()
                    .WithTitle($"?? {session.TrackName}")
                    .WithDescription($"**{session.ArtistName}**\n\n{lyricsText}")
                    .WithColor(Color.Purple)
                    .WithFooter($"第 {currentLineIndex + 1}/{session.SyncedLines.Count} 行 | 使用按鈕手動控制")
                    .Build();

                var components = CreateLyricsButtons(session.MessageChannel.Id, currentLineIndex, session.SyncedLines.Count);

                if (session.LyricsMessage == null)
                {
                    // 第一次創建訊息
                    session.LyricsMessage = await session.MessageChannel.SendMessageAsync(embed: embed, components: components.Build());
                }
                else
                {
                    // 更新現有訊息
                    await session.LyricsMessage.ModifyAsync(msg =>
                    {
                        msg.Embed = embed;
                        msg.Components = components.Build();
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LYRICS ERROR] 更新歌詞訊息失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 歌詞會話類
        /// </summary>
        private class LyricsSession
        {
            public string TrackName { get; set; }
            public string ArtistName { get; set; }
            public List<(TimeSpan timestamp, string line)> SyncedLines { get; set; }
            public IMessageChannel MessageChannel { get; set; }
            public IUserMessage LyricsMessage { get; set; }
            public int CurrentLineIndex { get; set; }
        }
    }
}
