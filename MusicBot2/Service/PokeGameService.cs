using Discord;
using Discord.WebSocket;
using MusicBot2.Helpers;
using MusicBot2.Models;
using MusicBot2.Service;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class PokeGameService
    {
        private readonly HttpClient _httpClient;
        private readonly IDatabase _redisDb;
        private readonly OpenRouterService _aiService;
        private const string API_BASE_URL = "https://pokeapi.co/api/v2/";
        private const string PLAYER_DATA_KEY = "pokegame:player:";
        private const string MATCHMAKING_KEY = "pokegame:matchmaking";

        public PokeGameService(string redisConnectionString, OpenRouterService aiService)
        {
            _httpClient = new HttpClient();
            var redis = ConnectionMultiplexer.Connect(redisConnectionString);
            _redisDb = redis.GetDatabase();
            _aiService = aiService;
        }

        #region 抓pokemon
        public async Task<(Embed embed, ComponentBuilder component)> CatchPokemonAsync(ulong userId, string userName)
        {
            try
            {
                // 檢查今天是否已經抓過
                var player = await GetPlayerDataAsync(userId, userName);

                if (player.LastCatchDate.HasValue && player.LastCatchDate.Value.Date == DateTime.UtcNow.Date)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 今天已經抓過pokemon了！")
                        .WithDescription($"每天只能抓一隻pokemon喔！\n明天再來吧～")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }

                // 隨機抓一隻pokemon
                var pokemon = await GetRandomPokemonAsync();

                // 儲存到玩家資料
                player.CaughtPokemon.Add(pokemon);
                player.LastCatchDate = DateTime.UtcNow;
                await SavePlayerDataAsync(player);

                // 建立回應訊息
                var embed = new EmbedBuilder()
                    .WithTitle($"🎉 恭喜抓到pokemon！")
                    .WithDescription($"**{pokemon.Name}** 加入了你的隊伍！")
                    .WithThumbnailUrl(pokemon.ImageUrl)
                    .WithColor(Color.Green)
                    .AddField("屬性", string.Join(", ", pokemon.Types), true)
                    .AddField("抓到時間", pokemon.CaughtDate.ToString("yyyy-MM-dd HH:mm"), true)
                    .AddField("能力值", 
                        $"HP: {pokemon.HP}\n" +
                        $"攻擊: {pokemon.Attack}\n" +
                        $"防禦: {pokemon.Defense}\n" +
                        $"特攻: {pokemon.SpecialAttack}\n" +
                        $"特防: {pokemon.SpecialDefense}\n" +
                        $"速度: {pokemon.Speed}")
                    .WithFooter($"目前共有 {player.CaughtPokemon.Count} 隻pokemon")
                    .WithCurrentTimestamp()
                    .Build();

                return (embed, new ComponentBuilder());
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"抓pokemon時發生錯誤: {ex.Message}").Item2, new ComponentBuilder());
            }
        }

        private async Task<PokeGamePokemon> GetRandomPokemonAsync()
        {
            // 隨機選擇一隻pokemon (1-898 是前幾代的pokemon)
            var random = new Random();
            int pokemonId = random.Next(1, 899);

            // 獲取pokemon詳細資料
            var response = await _httpClient.GetAsync($"{API_BASE_URL}pokemon/{pokemonId}");
            if (!response.IsSuccessStatusCode)
                throw new Exception("無法獲取pokemon資料");

            var content = await response.Content.ReadAsStringAsync();
            var pokeData = JsonConvert.DeserializeObject<Pokemon>(content);

            // 獲取中文名稱
            var speciesResponse = await _httpClient.GetAsync(pokeData.species.url);
            var speciesContent = await speciesResponse.Content.ReadAsStringAsync();
            var speciesData = JsonConvert.DeserializeObject<PokeSpecies>(speciesContent);

            var chineseName = speciesData.names.FirstOrDefault(n => n.language.name == "zh-Hant")?.name 
                ?? pokeData.species.name;

            // 建立PokeGamePokemon物件
            var pokemon = new PokeGamePokemon
            {
                Id = pokeData.id,
                Name = chineseName,
                CustomName = null,
                ImageUrl = pokeData.sprites.other.official_artwork.front_default 
                    ?? pokeData.sprites.front_default,
                HP = pokeData.stats.FirstOrDefault(s => s.stat.name == "hp")?.base_stat ?? 0,
                Attack = pokeData.stats.FirstOrDefault(s => s.stat.name == "attack")?.base_stat ?? 0,
                Defense = pokeData.stats.FirstOrDefault(s => s.stat.name == "defense")?.base_stat ?? 0,
                SpecialAttack = pokeData.stats.FirstOrDefault(s => s.stat.name == "special-attack")?.base_stat ?? 0,
                SpecialDefense = pokeData.stats.FirstOrDefault(s => s.stat.name == "special-defense")?.base_stat ?? 0,
                Speed = pokeData.stats.FirstOrDefault(s => s.stat.name == "speed")?.base_stat ?? 0,
                Types = pokeData.types.Select(t => t.type.name).ToList(),
                CaughtDate = DateTime.UtcNow
            };

            return pokemon;
        }
        #endregion

        #region 自定義pokemon
        public async Task<(Embed embed, ComponentBuilder component)> CustomizePokemonAsync(ulong userId, string userName, int pokemonIndex, string customName)
        {
            try
            {
                var player = await GetPlayerDataAsync(userId, userName);

                if (player.CaughtPokemon.Count == 0)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 你還沒有任何pokemon！")
                        .WithDescription("先去抓一隻pokemon吧！")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }

                if (pokemonIndex < 1 || pokemonIndex > player.CaughtPokemon.Count)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 無效的pokemon編號！")
                        .WithDescription($"請輸入 1 到 {player.CaughtPokemon.Count} 之間的編號")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }

                var pokemon = player.CaughtPokemon[pokemonIndex - 1];
                var oldName = pokemon.CustomName ?? pokemon.Name;
                pokemon.CustomName = customName;
                await SavePlayerDataAsync(player);

                var embed = new EmbedBuilder()
                    .WithTitle("✅ 成功自定義pokemon名稱！")
                    .WithDescription($"**{oldName}** → **{customName}**")
                    .WithThumbnailUrl(pokemon.ImageUrl)
                    .WithColor(Color.Blue)
                    .Build();

                return (embed, new ComponentBuilder());
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"自定義pokemon時發生錯誤: {ex.Message}").Item2, new ComponentBuilder());
            }
        }

        public async Task<(Embed embed, ComponentBuilder component)> ListPokemonAsync(ulong userId, string userName)
        {
            try
            {
                var player = await GetPlayerDataAsync(userId, userName);

                if (player.CaughtPokemon.Count == 0)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("你還沒有任何pokemon！")
                        .WithDescription("使用 `/抓pokemon` 來抓取你的第一隻pokemon吧！")
                        .WithColor(Color.Orange)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }

                var embed = new EmbedBuilder()
                    .WithTitle($"{userName} 的pokemon圖鑑")
                    .WithColor(Color.Gold)
                    .WithFooter($"總共 {player.CaughtPokemon.Count} 隻pokemon | 戰績: {player.Wins}勝 {player.Losses}敗");

                for (int i = 0; i < player.CaughtPokemon.Count; i++)
                {
                    var pokemon = player.CaughtPokemon[i];
                    var displayName = pokemon.CustomName ?? pokemon.Name;
                    embed.AddField(
                        $"{i + 1}. {displayName}",
                        $"原名: {pokemon.Name}\n" +
                        $"屬性: {string.Join(", ", pokemon.Types)}\n" +
                        $"HP: {pokemon.HP} | 攻: {pokemon.Attack} | 防: {pokemon.Defense}\n" +
                        $"抓到時間: {pokemon.CaughtDate:yyyy-MM-dd}",
                        false
                    );
                }

                return (embed.Build(), new ComponentBuilder());
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"列出pokemon時發生錯誤: {ex.Message}").Item2, new ComponentBuilder());
            }
        }
        #endregion

        #region 對戰系統
        public async Task<(Embed embed, ComponentBuilder component)> StartBattleSearchAsync(ulong userId, string userName, int pokemonIndex)
        {
            try
            {
                var player = await GetPlayerDataAsync(userId, userName);

                if (player.CaughtPokemon.Count == 0)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 你還沒有任何pokemon！")
                        .WithDescription("先去抓一隻pokemon吧！")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }

                if (pokemonIndex < 1 || pokemonIndex > player.CaughtPokemon.Count)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 無效的pokemon編號！")
                        .WithDescription($"請輸入 1 到 {player.CaughtPokemon.Count} 之間的編號")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }

                var pokemon = player.CaughtPokemon[pokemonIndex - 1];

                // 檢查是否有其他玩家在等待
                var waitingPlayers = await GetWaitingPlayersAsync();
                var opponent = waitingPlayers.FirstOrDefault(p => p.UserId != userId);

                if (opponent != null)
                {
                    // 找到對手，開始對戰！
                    await RemoveFromMatchmakingAsync(opponent.UserId);
                    return await ExecuteBattleAsync(userId, userName, pokemon, opponent.UserId, opponent.UserName, opponent.Pokemon);
                }
                else
                {
                    // 沒有對手，加入配對池
                    await AddToMatchmakingAsync(userId, userName, pokemon);

                    var embed = new EmbedBuilder()
                        .WithTitle("🔍 尋找對手中...")
                        .WithDescription($"使用 **{pokemon.CustomName ?? pokemon.Name}** 尋找對手中！\n請等待其他玩家加入對戰...")
                        .WithThumbnailUrl(pokemon.ImageUrl)
                        .WithColor(Color.Blue)
                        .Build();

                    return (embed, new ComponentBuilder());
                }
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"開始對戰搜尋時發生錯誤: {ex.Message}").Item2, new ComponentBuilder());
            }
        }

        private async Task<(Embed embed, ComponentBuilder component)> ExecuteBattleAsync(
            ulong player1Id, string player1Name, PokeGamePokemon pokemon1,
            ulong player2Id, string player2Name, PokeGamePokemon pokemon2)
        {
            try
            {
                // 準備對戰資訊給 AI 判斷
                string battlePrompt = $@"請模擬一場精彩的pokemon對戰，並判斷勝負。

對戰雙方：
1. {player1Name} 的 {pokemon1.CustomName ?? pokemon1.Name}
   - 屬性: {string.Join(", ", pokemon1.Types)}
   - HP: {pokemon1.HP}, 攻擊: {pokemon1.Attack}, 防禦: {pokemon1.Defense}
   - 特攻: {pokemon1.SpecialAttack}, 特防: {pokemon1.SpecialDefense}, 速度: {pokemon1.Speed}

2. {player2Name} 的 {pokemon2.CustomName ?? pokemon2.Name}
   - 屬性: {string.Join(", ", pokemon2.Types)}
   - HP: {pokemon2.HP}, 攻擊: {pokemon2.Attack}, 防禦: {pokemon2.Defense}
   - 特攻: {pokemon2.SpecialAttack}, 特防: {pokemon2.SpecialDefense}, 速度: {pokemon2.Speed}

請根據以上數據和屬性相剋關係，判斷誰會獲勝，並用繁體中文描述一段精彩的對戰過程（約150-200字）。
最後請在描述的最後一行明確說明勝者是誰，格式為「勝者：[玩家名稱]」";

                // 呼叫 AI 判斷對戰結果
                var aiResponse = await _aiService.GenerateSimpleTextAsync(battlePrompt, null, false, "pokebattle");

                // 解析 AI 回應，判斷勝者
                bool player1Wins = aiResponse.Contains($"勝者：{player1Name}") || 
                                   aiResponse.Contains($"勝者: {player1Name}");

                // 如果 AI 沒有明確指出勝者，則根據數值判斷
                if (!player1Wins && !aiResponse.Contains($"勝者：{player2Name}") && !aiResponse.Contains($"勝者: {player2Name}"))
                {
                    int pokemon1Total = pokemon1.HP + pokemon1.Attack + pokemon1.Defense + 
                                       pokemon1.SpecialAttack + pokemon1.SpecialDefense + pokemon1.Speed;
                    int pokemon2Total = pokemon2.HP + pokemon2.Attack + pokemon2.Defense + 
                                       pokemon2.SpecialAttack + pokemon2.SpecialDefense + pokemon2.Speed;
                    player1Wins = pokemon1Total > pokemon2Total;
                }

                var winnerId = player1Wins ? player1Id : player2Id;
                var winnerName = player1Wins ? player1Name : player2Name;
                var winnerPokemon = player1Wins ? pokemon1 : pokemon2;
                var loserId = player1Wins ? player2Id : player1Id;
                var loserName = player1Wins ? player2Name : player1Name;
                var loserPokemon = player1Wins ? pokemon2 : pokemon1;

                // 更新戰績
                var winner = await GetPlayerDataAsync(winnerId, winnerName);
                winner.TotalBattles++;
                winner.Wins++;
                await SavePlayerDataAsync(winner);

                var loser = await GetPlayerDataAsync(loserId, loserName);
                loser.TotalBattles++;
                loser.Losses++;
                await SavePlayerDataAsync(loser);

                // 建立對戰結果訊息
                var embed = new EmbedBuilder()
                    .WithTitle("⚔️ pokemon對戰結果 ⚔️")
                    .WithDescription(aiResponse)
                    .WithColor(Color.Gold)
                    .AddField($"🏆 勝者: {winnerName}", 
                        $"{winnerPokemon.CustomName ?? winnerPokemon.Name}\n" +
                        $"戰績: {winner.Wins}勝 {winner.Losses}敗", true)
                    .AddField($"😢 敗者: {loserName}", 
                        $"{loserPokemon.CustomName ?? loserPokemon.Name}\n" +
                        $"戰績: {loser.Wins}勝 {loser.Losses}敗", true)
                    .AddField("🎁 獲勝獎勵", "恭喜獲得一次額外抓pokemon的機會！", false)
                    .WithCurrentTimestamp()
                    .Build();

                // 給勝者一次額外的抓寶機會（重置今日抓寶紀錄）
                winner.LastCatchDate = null;
                await SavePlayerDataAsync(winner);

                return (embed, new ComponentBuilder());
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"執行對戰時發生錯誤: {ex.Message}").Item2, new ComponentBuilder());
            }
        }
        #endregion

        #region Redis 資料操作
        private async Task<PokeGamePlayer> GetPlayerDataAsync(ulong userId, string userName)
        {
            var key = $"{PLAYER_DATA_KEY}{userId}";
            var data = await _redisDb.StringGetAsync(key);

            if (data.IsNullOrEmpty)
            {
                return new PokeGamePlayer
                {
                    UserId = userId,
                    UserName = userName,
                    CaughtPokemon = new List<PokeGamePokemon>(),
                    LastCatchDate = null,
                    TotalBattles = 0,
                    Wins = 0,
                    Losses = 0
                };
            }

            return JsonConvert.DeserializeObject<PokeGamePlayer>(data);
        }

        private async Task SavePlayerDataAsync(PokeGamePlayer player)
        {
            var key = $"{PLAYER_DATA_KEY}{player.UserId}";
            var data = JsonConvert.SerializeObject(player);
            await _redisDb.StringSetAsync(key, data);
        }

        private async Task AddToMatchmakingAsync(ulong userId, string userName, PokeGamePokemon pokemon)
        {
            var matchmaking = new BattleMatchmaking
            {
                UserId = userId,
                UserName = userName,
                Pokemon = pokemon,
                SearchStartTime = DateTime.UtcNow
            };

            var data = JsonConvert.SerializeObject(matchmaking);
            await _redisDb.HashSetAsync(MATCHMAKING_KEY, userId.ToString(), data);

            // 設定 5 分鐘過期
            await _redisDb.KeyExpireAsync(MATCHMAKING_KEY, TimeSpan.FromMinutes(5));
        }

        private async Task<List<BattleMatchmaking>> GetWaitingPlayersAsync()
        {
            var entries = await _redisDb.HashGetAllAsync(MATCHMAKING_KEY);
            var result = new List<BattleMatchmaking>();

            foreach (var entry in entries)
            {
                try
                {
                    var matchmaking = JsonConvert.DeserializeObject<BattleMatchmaking>(entry.Value);
                    // 只返回 5 分鐘內的搜尋
                    if ((DateTime.UtcNow - matchmaking.SearchStartTime).TotalMinutes < 5)
                    {
                        result.Add(matchmaking);
                    }
                }
                catch { }
            }

            return result;
        }

        private async Task RemoveFromMatchmakingAsync(ulong userId)
        {
            await _redisDb.HashDeleteAsync(MATCHMAKING_KEY, userId.ToString());
        }
        #endregion
    }
}
