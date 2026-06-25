using System;
using System.Collections.Generic;
using System.Linq;

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
        public Dictionary<ulong, TRPGCharacter> Characters { get; set; } = new();
        public bool IsActive { get; set; }
        public bool WaitingForDiceRoll { get; set; }
        public string? PendingDiceContext { get; set; }
        public ulong? WaitingPlayerId { get; set; }

        /// <summary>
        /// 獲取或創建角色
        /// </summary>
        public TRPGCharacter GetOrCreateCharacter(ulong userId, string userName)
        {
            if (!Characters.ContainsKey(userId))
            {
                Characters[userId] = new TRPGCharacter
                {
                    UserId = userId,
                    UserName = userName,
                    CurrentHP = 100,
                    MaxHP = 100
                };
            }
            return Characters[userId];
        }

        /// <summary>
        /// 獲取所有角色的狀態摘要
        /// </summary>
        public string GetCharactersStatusSummary()
        {
            if (Characters.Count == 0)
                return "目前沒有角色";

            var lines = Characters.Values.Select(c => 
                $"- {c.UserName}: {c.CurrentHP}/{c.MaxHP} HP {c.GetHealthStatus()}");
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// TRPG 角色資訊
    /// </summary>
    public class TRPGCharacter
    {
        public ulong UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public int CurrentHP { get; set; } = 100;
        public int MaxHP { get; set; } = 100;
        public DateTime LastActionTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 受到傷害
        /// </summary>
        public void TakeDamage(int damage)
        {
            CurrentHP = Math.Max(0, CurrentHP - damage);
        }

        /// <summary>
        /// 恢復生命值
        /// </summary>
        public void Heal(int amount)
        {
            CurrentHP = Math.Min(MaxHP, CurrentHP + amount);
        }

        /// <summary>
        /// 是否存活
        /// </summary>
        public bool IsAlive => CurrentHP > 0;

        /// <summary>
        /// 獲取健康狀態圖示
        /// </summary>
        public string GetHealthStatus()
        {
            var percentage = (double)CurrentHP / MaxHP;
            return percentage switch
            {
                >= 0.8 => "??",
                >= 0.5 => "??",
                >= 0.3 => "??",
                > 0 => "??",
                _ => "??"
            };
        }

        /// <summary>
        /// 獲取健康狀態描述
        /// </summary>
        public string GetHealthDescription()
        {
            var percentage = (double)CurrentHP / MaxHP;
            return percentage switch
            {
                >= 0.8 => "狀態良好",
                >= 0.5 => "輕傷",
                >= 0.3 => "中傷",
                > 0 => "重傷",
                _ => "已死亡"
            };
        }
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
