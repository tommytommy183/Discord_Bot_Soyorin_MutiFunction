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
        /// 開始顯示同步歌詞
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
                    StartTime = DateTime.UtcNow,
                    CancellationTokenSource = new CancellationTokenSource()
                };

                _activeSessions[channelId] = session;

                // 發送初始訊息
                var initialEmbed = new EmbedBuilder()
                    .WithTitle($"?? {lyrics.trackName}")
                    .WithDescription($"**{lyrics.artistName}**\n\n_準備開始播放歌詞..._")
                    .WithColor(Color.Blue)
                    .WithFooter("歌詞同步中")
                    .Build();

                session.LyricsMessage = await messageChannel.SendMessageAsync(embed: initialEmbed);

                // 啟動歌詞更新任務
                _ = Task.Run(() => UpdateLyricsAsync(channelId, session));

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
                session.CancellationTokenSource?.Cancel();
                session.CancellationTokenSource?.Dispose();
            }
        }

        /// <summary>
        /// 更新歌詞顯示
        /// </summary>
        private async Task UpdateLyricsAsync(ulong channelId, LyricsSession session)
        {
            var cancellationToken = session.CancellationTokenSource.Token;
            int currentLineIndex = 0;
            var lastUpdateTime = DateTime.UtcNow;

            try
            {
                while (!cancellationToken.IsCancellationRequested && currentLineIndex < session.SyncedLines.Count)
                {
                    var elapsed = DateTime.UtcNow - session.StartTime;
                    var currentLine = session.SyncedLines[currentLineIndex];

                    // 檢查是否到達當前行的時間
                    if (elapsed >= currentLine.timestamp)
                    {
                        // 只在時間變化超過 2 秒時更新，避免過於頻繁
                        if ((DateTime.UtcNow - lastUpdateTime).TotalSeconds >= 2)
                        {
                            await UpdateLyricsMessageAsync(session, currentLineIndex);
                            lastUpdateTime = DateTime.UtcNow;
                        }
                        currentLineIndex++;
                    }
                    else
                    {
                        // 等待到下一行的時間
                        var delay = currentLine.timestamp - elapsed;
                        if (delay.TotalMilliseconds > 0 && delay.TotalMilliseconds < 10000) // 最多等10秒
                        {
                            await Task.Delay(delay, cancellationToken);
                        }
                        else
                        {
                            await Task.Delay(100, cancellationToken); // 短暫延遲
                        }
                    }
                }

                // 歌曲結束
                if (!cancellationToken.IsCancellationRequested)
                {
                    var finalEmbed = new EmbedBuilder()
                        .WithTitle($"?? {session.TrackName}")
                        .WithDescription($"**{session.ArtistName}**\n\n_歌曲已結束_")
                        .WithColor(Color.Green)
                        .WithFooter("感謝聆聽")
                        .Build();

                    if (session.LyricsMessage != null)
                    {
                        await session.LyricsMessage.ModifyAsync(msg => msg.Embed = finalEmbed);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，不需要處理
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LYRICS ERROR] 更新歌詞時發生錯誤: {ex.Message}");
            }
            finally
            {
                StopLyricsDisplay(channelId);
            }
        }

        /// <summary>
        /// 更新歌詞訊息
        /// </summary>
        private async Task UpdateLyricsMessageAsync(LyricsSession session, int currentLineIndex)
        {
            try
            {
                if (session.LyricsMessage == null) return;

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
                    .WithFooter($"第 {currentLineIndex + 1}/{session.SyncedLines.Count} 行")
                    .Build();

                await session.LyricsMessage.ModifyAsync(msg => msg.Embed = embed);
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
            public DateTime StartTime { get; set; }
            public CancellationTokenSource CancellationTokenSource { get; set; }
        }
    }
}
