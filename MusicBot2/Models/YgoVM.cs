using System.Text.Json.Serialization;

namespace MusicBot2.Models
{
    // ── YGOProDeck API 回應 ──────────────────────────────────────────────
    public class YgoApiResponse
    {
        [JsonPropertyName("data")] public List<YgoCardData> Data { get; set; } = new();
    }

    public class YgoCardData
    {
        [JsonPropertyName("id")]          public int Id { get; set; }
        [JsonPropertyName("name")]        public string Name { get; set; }
        [JsonPropertyName("type")]        public string Type { get; set; }
        [JsonPropertyName("frameType")]   public string FrameType { get; set; }
        [JsonPropertyName("desc")]        public string Desc { get; set; }
        [JsonPropertyName("atk")]         public int? Atk { get; set; }
        [JsonPropertyName("def")]         public int? Def { get; set; }
        [JsonPropertyName("level")]       public int? Level { get; set; }
        [JsonPropertyName("attribute")]   public string Attribute { get; set; }
        [JsonPropertyName("race")]        public string Race { get; set; }
        [JsonPropertyName("card_images")] public List<YgoCardImage> CardImages { get; set; } = new();
    }

    public class YgoCardImage
    {
        [JsonPropertyName("id")]              public int Id { get; set; }
        [JsonPropertyName("image_url")]       public string ImageUrl { get; set; }
        [JsonPropertyName("image_url_small")] public string ImageUrlSmall { get; set; }
    }

    // ── 遊戲內卡牌（含執行時狀態）────────────────────────────────────────
    public class YgoCard
    {
        public int ApiId { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }      // "Monster" / "Spell" / "Trap"
        public string FrameType { get; set; } // "normal","effect","spell","trap","fusion","synchro"
        public string Desc { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Level { get; set; }
        public string Attribute { get; set; }
        public string Race { get; set; }
        public string ImageUrl { get; set; }     // small thumbnail
        public string RareImageUrl { get; set; } // rarest alt-art full image

        // 執行時狀態（不需要序列化到 Redis）
        public bool FaceDown { get; set; }
        public bool IsDefensePosition { get; set; }
        public bool AttackedThisTurn { get; set; }
        public bool SummonedThisTurn { get; set; }
        public int? TempAtk { get; set; }
        public bool CannotAttack { get; set; }          // Spellbinding Circle / Nightmare Wheel
        public bool CannotChangePosition { get; set; }  // Spellbinding Circle

        public bool IsMonster  => Type == "Monster";
        public bool IsSpell    => Type == "Spell";
        public bool IsTrap     => Type == "Trap";
        public bool IsFusion   => FrameType == "fusion";
        public bool IsSynchro  => FrameType == "synchro";
        public bool IsTuner    => Race?.Contains("Tuner") == true || Desc?.Contains("Tuner") == true;

        public int TributeRequired =>
            Level >= 7 ? 2 :
            Level >= 5 ? 1 : 0;

        public int EffectiveAtk => TempAtk ?? Atk;

        [JsonIgnore]
        public string ShortName
        {
            get
            {
                if (Name.Length <= 10) return Name;
                var words = Name.Split(' ');
                if (words.Length >= 2) return string.Join(" ", words.Take(2));
                return Name[..10];
            }
        }

        public YgoCard Clone()
        {
            var c = (YgoCard)MemberwiseClone();
            c.FaceDown = false;
            c.IsDefensePosition = false;
            c.AttackedThisTurn = false;
            c.SummonedThisTurn = false;
            c.TempAtk = null;
            c.CannotAttack = false;
            c.CannotChangePosition = false;
            return c;
        }
    }

    // ── 玩家場地狀態 ──────────────────────────────────────────────────────
    public class YgoPlayerField
    {
        public ulong UserId { get; set; }
        public string UserName { get; set; }
        public bool IsAi { get; set; }
        public string DeckName { get; set; }
        public int LifePoints { get; set; } = 8000;

        // 怪獸區 5 格（null = 空）
        public List<YgoCard?> MonsterZones { get; set; } = new() { null,null,null,null,null };
        // 魔陷區 5 格（null = 空）
        public List<YgoCard?> SpellTrapZones { get; set; } = new() { null,null,null,null,null };

        public List<YgoCard> Deck { get; set; } = new();
        public List<YgoCard> Hand { get; set; } = new();
        public List<YgoCard> Graveyard { get; set; } = new();
        public List<YgoCard> ExtraDeck { get; set; } = new(); // 融合/同調

        // 回合旗標
        public bool NormalSummonedThisTurn { get; set; }
        public bool DrewThisTurn { get; set; }
        public int SwordsCounter { get; set; } = 0; // 光之護封劍剩餘回合
        public bool WabokuActive { get; set; }                // Waboku — 本回合戰鬥傷害無效
        public bool CannotDeclareAttackThisTurn { get; set; } // Threatening Roar
        public int PendingEndTurnDamage { get; set; }         // Power Bond — 下回合結算傷害

        public int DeckCount => Deck.Count;
        public int HandCount => Hand.Count;

        public int FirstEmptyMonsterZone()
        {
            for (int i = 0; i < 5; i++)
                if (i >= MonsterZones.Count || MonsterZones[i] == null) return i;
            return -1;
        }

        public int FirstEmptySTZone()
        {
            for (int i = 0; i < 5; i++)
                if (i >= SpellTrapZones.Count || SpellTrapZones[i] == null) return i;
            return -1;
        }

        public List<YgoCard> GetMonstersOnField() =>
            MonsterZones.Where(c => c != null).Select(c => c!).ToList();
    }

    // ── 決鬥階段 ──────────────────────────────────────────────────────────
    public enum DuelPhase
    {
        NotStarted,
        DrawPhase,
        StandbyPhase,
        MainPhase1,
        BattlePhase,
        MainPhase2,
        EndPhase,
        GameOver
    }

    // ── 決鬥完整狀態 ──────────────────────────────────────────────────────
    public class YgoDuelState
    {
        public string DuelId { get; set; }
        public ulong ChannelId { get; set; }
        public bool IsAiDuel { get; set; } = true;

        public YgoPlayerField Field1 { get; set; } = new(); // 玩家
        public YgoPlayerField Field2 { get; set; } = new(); // AI / 對手

        public DuelPhase CurrentPhase { get; set; } = DuelPhase.NotStarted;
        public int TurnNumber { get; set; } = 1;
        public ulong CurrentTurnPlayerId { get; set; }

        public List<string> BattleLog { get; set; } = new();
        public bool IsActive { get; set; } = true;
        public string WinnerName { get; set; }
        public DateTime LastActionTime { get; set; } = DateTime.UtcNow;
        public string LastPlayedCardImageUrl { get; set; } // 最後打出的卡圖 URL
        public ulong HandMessageId { get; set; }  // 手牌訊息 ID，用於 in-place 更新

        // 多步驟動作暫存
        public int? PendingAttackerZone { get; set; }       // 攻擊選擇第一步：攻擊方格子
        public List<int> PendingTributeZones { get; set; } = new(); // 貢獻選擇
        public int? PendingSummonHandIndex { get; set; }     // 等待貢獻後要召喚的手牌索引

        [JsonIgnore]
        public YgoPlayerField CurrentField =>
            CurrentTurnPlayerId == Field1.UserId ? Field1 : Field2;

        [JsonIgnore]
        public YgoPlayerField OpponentField =>
            CurrentTurnPlayerId == Field1.UserId ? Field2 : Field1;

        public void AddLog(string entry)
        {
            BattleLog.Add(entry);
            if (BattleLog.Count > 8) BattleLog.RemoveAt(0);
        }
    }

    // ── 動漫牌組定義 ──────────────────────────────────────────────────────
    public class AnimeDeckDefinition
    {
        public string Key { get; set; }
        public string CharacterName { get; set; }
        public string Series { get; set; }
        public string Emoji { get; set; }
        public uint Color { get; set; }
        public string AiPersonality { get; set; }
        public List<string> MainDeckNames { get; set; } = new();
        public List<string> ExtraDeckNames { get; set; } = new();
    }
}
