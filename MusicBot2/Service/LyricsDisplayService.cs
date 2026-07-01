using Discord;
using Discord.WebSocket;
using MusicBot2.Models;
using MusicBot2.Helpers;
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
        private readonly Timer _updateTimer;

        public LyricsDisplayService(LyrisService lyrisService)
        {
            _lyrisService = lyrisService;
            // 每500ms檢查一次是否需要更新歌詞
            _updateTimer = new Timer(UpdateAllSessions, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
        }

        /// <summary>
        /// 創建歌詞控制按鈕
        /// </summary>
        private ComponentBuilder CreateLyricsButtons(ulong channelId, int currentLine, int totalLines, bool isAutoMode)
        {
            var builder = new ComponentBuilder();

            // 第一排：上一句和下一句
            builder.WithButton("⬅️ 上一句", $"lyrics_prev_{channelId}", ButtonStyle.Primary, disabled: currentLine <= 0 || isAutoMode)
                   .WithButton("下一句 ➡️", $"lyrics_next_{channelId}", ButtonStyle.Primary, disabled: currentLine >= totalLines - 1 || isAutoMode);

            // 第二排：自動/手動模式切換
            var modeButton = isAutoMode
                ? new ButtonBuilder("⏸️ 手動模式", $"lyrics_manual_{channelId}", ButtonStyle.Secondary)
                : new ButtonBuilder("▶️ 自動模式", $"lyrics_auto_{channelId}", ButtonStyle.Success);

            builder.AddRow(new ActionRowBuilder().WithButton(modeButton));

            return builder;
        }

        /// <summary>
        /// 定期更新所有活動的歌詞會話
        /// </summary>
        private async void UpdateAllSessions(object state)
        {
            foreach (var kvp in _activeSessions)
            {
                var session = kvp.Value;

                // 只在自動模式下更新
                if (!session.IsAutoMode || session.StartTime == null)
                    continue;

                try
                {
                    var elapsed = DateTime.UtcNow - session.StartTime.Value;
                    var newLineIndex = FindCurrentLineIndex(session.SyncedLines, elapsed);

                    if (newLineIndex != session.CurrentLineIndex && newLineIndex >= 0)
                    {
                        session.CurrentLineIndex = newLineIndex;
                        await UpdateLyricsMessageAsync(session, newLineIndex);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LYRICS ERROR] 自動更新失敗: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 根據時間戳找到當前應該顯示的歌詞行索引
        /// </summary>
        private int FindCurrentLineIndex(List<(TimeSpan timestamp, string line)> lines, TimeSpan currentTime)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (currentTime >= lines[i].timestamp)
                {
                    return i;
                }
            }
            return 0;
        }

        /// <summary>
        /// 開始顯示同步歌詞（支援自動和手動模式）
        /// </summary>
        public async Task<bool> StartLyricsDisplayAsync(ulong channelId, string trackName, string artistName, IMessageChannel messageChannel, bool autoMode = true)
        {
            try
            {
                // 獲取歌詞
                var lyrics = await _lyrisService.GetLyricsAsync(trackName, artistName);
                if (lyrics == null || string.IsNullOrWhiteSpace(lyrics.syncedLyrics))
                {
                    await messageChannel.SendMessageAsync($"⚠️ 找不到 **{trackName}** 的同步歌詞");
                    return false;
                }

                // 解析同步歌詞
                var syncedLines = _lyrisService.ParseSyncedLyrics(lyrics.syncedLyrics);
                if (syncedLines.Count == 0)
                {
                    await messageChannel.SendMessageAsync($"⚠️ 歌詞格式錯誤");
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
                    CurrentLineIndex = 0,
                    IsJapanese = DetectJapanese(syncedLines),
                    IsAutoMode = autoMode,
                    StartTime = autoMode ? DateTime.UtcNow : null
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
        /// 檢測歌詞是否為日文
        /// </summary>
        private bool DetectJapanese(List<(TimeSpan timestamp, string line)> lines)
        {
            if (lines == null || lines.Count == 0)
                return false;

            // 檢查前10行歌詞
            int checkCount = Math.Min(10, lines.Count);
            int japaneseLines = 0;

            for (int i = 0; i < checkCount; i++)
            {
                if (JapaneseTextHelper.IsLikelyJapanese(lines[i].line))
                {
                    japaneseLines++;
                }
            }

            // 如果超過一半的行包含日文，判定為日文歌詞
            return japaneseLines > checkCount / 2;
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

                var action = parts[1]; // "prev", "next", "auto", or "manual"
                var channelId = ulong.Parse(parts[2]);

                if (!_activeSessions.TryGetValue(channelId, out var session))
                {
                    await component.RespondAsync("⚠️ 找不到歌詞會話，請重新開始", ephemeral: true);
                    return true;
                }

                // 處理模式切換
                if (action == "auto")
                {
                    session.IsAutoMode = true;
                    session.StartTime = DateTime.UtcNow;
                    await UpdateLyricsMessageAsync(session, session.CurrentLineIndex);
                    await component.DeferAsync();
                    return true;
                }
                else if (action == "manual")
                {
                    session.IsAutoMode = false;
                    session.StartTime = null;
                    await UpdateLyricsMessageAsync(session, session.CurrentLineIndex);
                    await component.DeferAsync();
                    return true;
                }

                // 手動模式下才能切換行
                if (session.IsAutoMode)
                {
                    await component.RespondAsync("⚠️ 請先切換到手動模式", ephemeral: true);
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
                await component.RespondAsync("❌ 處理按鈕時發生錯誤", ephemeral: true);
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

                // 檢測是否為日文歌詞
                bool isJapanese = session.IsJapanese;

                var lyricsText = "";
                for (int i = startIndex; i <= endIndex; i++)
                {
                    var line = session.SyncedLines[i];
                    string displayLine = line.line;

                    // 如果是日文歌詞，為片假名添加平假名標註
                    if (isJapanese && !string.IsNullOrWhiteSpace(displayLine))
                    {
                        displayLine = JapaneseTextHelper.AddKatakanaFurigana(displayLine);
                    }

                    if (i == currentLineIndex)
                    {
                        // 當前行用粗體和特殊符號標記
                        lyricsText += $"**► {displayLine}**\n";
                    }
                    else if (i == currentLineIndex - 1 || i == currentLineIndex + 1)
                    {
                        // 前後一行稍微強調
                        lyricsText += $"  {displayLine}\n";
                    }
                    else
                    {
                        // 其他行淡化顯示
                        lyricsText += $"  _{displayLine}_\n";
                    }
                }

                var mode = session.IsAutoMode ? "自動同步" : "手動控制";
                var footerText = $"第 {currentLineIndex + 1}/{session.SyncedLines.Count} 行 | {mode}";

                if (isJapanese)
                {
                    footerText += " | 日文歌詞：片假名ᐧひらがな / 漢字ᐧ˙";
                }

                if (session.IsAutoMode && session.StartTime != null)
                {
                    var elapsed = DateTime.UtcNow - session.StartTime.Value;
                    footerText += $" | {elapsed:mm\\:ss}";
                }

                var embed = new EmbedBuilder()
                    .WithTitle($"🎵 {session.TrackName}")
                    .WithDescription($"**{session.ArtistName}**\n\n{lyricsText}")
                    .WithColor(session.IsAutoMode ? Color.Green : Color.Purple)
                    .WithFooter(footerText)
                    .Build();

                var components = CreateLyricsButtons(session.MessageChannel.Id, currentLineIndex, session.SyncedLines.Count, session.IsAutoMode);

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
            public bool IsJapanese { get; set; }
            public bool IsAutoMode { get; set; }
            public DateTime? StartTime { get; set; }
        }
    }
}
