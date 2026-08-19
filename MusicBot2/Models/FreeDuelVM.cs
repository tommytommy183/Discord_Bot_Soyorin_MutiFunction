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
        public string ImageUrl { get; set; } = "";
    }

    public class FreeDuelMessage
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = "";
    }

    public class FreeDuelState
    {
        public ulong ChannelId { get; set; }
        public ulong PlayerId { get; set; }
        public string PlayerName { get; set; } = "";
        public string PlayerDeckKey { get; set; } = "yugi";
        public string AiCharacterKey { get; set; } = "kaiba";
        public string AiCharacterName { get; set; } = "海馬瀬人";

        public int PlayerHp { get; set; } = 8000;
        public int AiHp { get; set; } = 8000;

        // Player state (program-managed)
        public List<string> PlayerHand { get; set; } = new();
        public List<FreeDuelCardOnField> PlayerField { get; set; } = new();
        public List<string> PlayerGraveyard { get; set; } = new();

        // AI state (program-managed, AI hand count only visible to player)
        public int AiHandCount { get; set; } = 5;
        public List<FreeDuelCardOnField> AiField { get; set; } = new();
        public List<string> AiGraveyard { get; set; } = new();

        public int TurnNumber { get; set; } = 1;
        public bool IsDuelEnded { get; set; }
        public string Winner { get; set; } = "";

        // Conversation history (trimmed to last 8 exchanges)
        public List<FreeDuelMessage> History { get; set; } = new();
        public DateTime LastActionTime { get; set; } = DateTime.UtcNow;

        // For hand view message dedup
        public ulong HandMessageId { get; set; }
    }

    // What the AI returns — small, event-driven
    public class FreeDuelAiTurn
    {
        public string Dialogue { get; set; } = "";
        public List<FreeDuelEvent> Events { get; set; } = new();
    }

    public class FreeDuelEvent
    {
        // Event types:
        // draw           – target draws; card = name (player only, AI just increments count)
        // summon         – place monster face-up; target, card, atk, def, position("attack"/"defense")
        // set_monster    – place face-down monster; target, card
        // set_st         – place face-down S/T; target, card
        // activate_spell – use spell from hand; target, card
        // flip           – flip face-down monster; target, card, atk, def
        // attack         – monster attacks; attacker_owner("player"/"ai"), attacker, defender_owner, defender(null=direct)
        // destroy        – card destroyed; target("player"/"ai"), card, zone("field"/"hand"/"deck")
        // damage         – LP damage; target("player"/"ai"), amount
        // heal           – LP gain; target, amount
        // discard        – discard from hand; target, card
        // end_duel       – duel ends; winner("player"/"ai")
        public string Type { get; set; } = "";
        public string Target { get; set; } = "";        // "player" or "ai"
        public string Card { get; set; } = "";
        public int Atk { get; set; }
        public int Def { get; set; }
        public string Position { get; set; } = "attack"; // "attack" or "defense"
        public string AttackerOwner { get; set; } = "";
        public string Attacker { get; set; } = "";
        public string DefenderOwner { get; set; } = "";
        public string Defender { get; set; } = "";       // empty = direct attack
        public int Amount { get; set; }
        public string Winner { get; set; } = "";
        public string Zone { get; set; } = "field";
    }
}
