using System;
using System.Collections.Generic;

namespace MusicBot2.Models
{
    /// <summary>
    /// TRPG 遊戲狀態
    /// </summary>
    public class TRPGGameState
    {
        public ulong ChannelId { get; set; }
        public ulong GameMasterId { get; set; }
        public DateTime StartTime { get; set; }
        public List<TRPGMessage> GameHistory { get; set; } = new();
        public bool IsActive { get; set; }
        public bool WaitingForDiceRoll { get; set; }
        public string? PendingDiceContext { get; set; }
        public ulong? WaitingPlayerId { get; set; }
    }

    /// <summary>
    /// TRPG 訊息記錄
    /// </summary>
    public class TRPGMessage
    {
        public ulong UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public TRPGMessageType Type { get; set; }
        public int? DiceResult { get; set; }
    }

    /// <summary>
    /// TRPG 訊息類型
    /// </summary>
    public enum TRPGMessageType
    {
        PlayerAction,    // 玩家行動
        DiceRoll,       // 骰子結果
        GMNarration,    // GM 旁白
        SystemMessage   // 系統訊息
    }

    /// <summary>
    /// OpenRouter TRPG 請求
    /// </summary>
    public class TRPGRequest
    {
        public string UserMessage { get; set; } = string.Empty;
        public List<TRPGMessage> GameHistory { get; set; } = new();
        public int? LastDiceRoll { get; set; }
    }
}
