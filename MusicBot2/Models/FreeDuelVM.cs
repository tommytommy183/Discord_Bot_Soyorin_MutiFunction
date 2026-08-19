using System;
using System.Collections.Generic;

namespace MusicBot2.Models
{
    public class FreeDuelCardOnField
    {
        public string Name { get; set; } = "";
        public int Atk { get; set; }
        public int Def { get; set; }
        public bool FaceDown { get; set; }
        public bool IsDefense { get; set; }
    }

    public class FreeDuelMessage
    {
        public string Role { get; set; } = "user";   // "user" or "assistant"
        public string Content { get; set; } = "";
    }

    public class FreeDuelState
    {
        public ulong ChannelId { get; set; }
        public ulong PlayerId { get; set; }
        public string PlayerName { get; set; } = "";
        public string AiCharacterKey { get; set; } = "kaiba";
        public string AiCharacterName { get; set; } = "海馬瀬人";

        public int PlayerHp { get; set; } = 8000;
        public int AiHp { get; set; } = 8000;

        public List<string> PlayerHand { get; set; } = new();
        public List<FreeDuelCardOnField> PlayerField { get; set; } = new();
        public List<string> PlayerGraveyard { get; set; } = new();

        public int AiHandCount { get; set; } = 5;
        public List<FreeDuelCardOnField> AiField { get; set; } = new();
        public List<string> AiGraveyard { get; set; } = new();

        public int TurnNumber { get; set; } = 1;
        public bool IsDuelEnded { get; set; }
        public string Winner { get; set; }  // "player" or "ai"

        public List<FreeDuelMessage> History { get; set; } = new();
        public DateTime LastActionTime { get; set; } = DateTime.UtcNow;
    }

    // Shape of JSON the AI returns
    public class FreeDuelAiResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("dialogue")]
        public string Dialogue { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("player_hp")]
        public int PlayerHp { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ai_hp")]
        public int AiHp { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("player_hand")]
        public List<string> PlayerHand { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("player_field")]
        public List<FreeDuelCardOnField> PlayerField { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("player_graveyard")]
        public List<string> PlayerGraveyard { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ai_field")]
        public List<FreeDuelCardOnField> AiField { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ai_graveyard")]
        public List<string> AiGraveyard { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ai_hand_count")]
        public int AiHandCount { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("duel_ended")]
        public bool DuelEnded { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("winner")]
        public string Winner { get; set; }  // "player" or "ai" or null
    }
}
