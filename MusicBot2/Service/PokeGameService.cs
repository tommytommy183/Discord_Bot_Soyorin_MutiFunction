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
        private readonly bool _useRedis;

        // 記憶體儲存 (當 Redis 無法連線時使用)
        private static readonly Dictionary<ulong, PokeGamePlayer> _memoryPlayers = new Dictionary<ulong, PokeGamePlayer>();
        private static readonly Dictionary<ulong, BattleMatchmaking> _memoryMatchmaking = new Dictionary<ulong, BattleMatchmaking>();

        private const string API_BASE_URL = "https://pokeapi.co/api/v2/";
        private const string PLAYER_DATA_KEY = "pokegame:player:";
        private const string MATCHMAKING_KEY = "pokegame:matchmaking";

        public PokeGameService(string redisConnectionString, OpenRouterService aiService)
        {
            _httpClient = new HttpClient();
            _aiService = aiService;

            // 嘗試連線 Redis，如果失敗則使用記憶體儲存
            try
            {
                if (!string.IsNullOrEmpty(redisConnectionString))
                {
                    var options = ConfigurationOptions.Parse(redisConnectionString);
                    options.ConnectTimeout = 10000; // 10秒連線超時
                    options.SyncTimeout = 10000; // 10秒同步操作超時
                    options.AsyncTimeout = 10000; // 10秒非同步操作超時
                    options.ConnectRetry = 3; // 重試3次
                    options.AbortOnConnectFail = false; // 連線失敗時不中止
                    options.KeepAlive = 60; // 保持連線 60 秒

                    var redis = ConnectionMultiplexer.Connect(options);
                    _redisDb = redis.GetDatabase();
                    _useRedis = true;
                    Console.WriteLine("✅ Redis 連線成功");
                }
                else
                {
                    _useRedis = false;
                    Console.WriteLine("⚠️ Redis 未設定，使用記憶體儲存");
                }
            }
            catch (Exception ex)
            {
                _useRedis = false;
                Console.WriteLine($"⚠️ Redis 連線失敗，使用記憶體儲存: {ex.Message}");
            }
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

                // 檢查寶可夢數量是否已達上限
                if (player.CaughtPokemon.Count >= 10)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 寶可夢數量已達上限！")
                        .WithDescription($"你已經有 10 隻pokemon了！\n請使用 `/蛋雕一隻pokemon` 指令釋放一隻後再來抓取新的。")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }

                // 隨機抓一隻pokemon
                var pokemon = await GetRandomPokemonAsync();
                string ShinyText = pokemon.isShiny ? "✨襪烙勒是閃的寶貝✨" : "";
                // 儲存到玩家資料
                player.CaughtPokemon.Add(pokemon);
                player.LastCatchDate = DateTime.UtcNow;
                await SavePlayerDataAsync(player);

                // 建立回應訊息
                var embed = new EmbedBuilder()
                    .WithTitle($"🎉 恭喜抓到pokemon！{ShinyText}")
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
            bool isShiny = false;

            //先全部取出來，再隨機挑
            string url = $"{API_BASE_URL}pokemon?limit=10000&offset=0";
            var responseRandom = await _httpClient.GetAsync(url);

            if (!responseRandom.IsSuccessStatusCode)
                throw new Exception("無法獲取pokemon資料");

            var responseContent = await responseRandom.Content.ReadAsStringAsync();
            var pokeResponses = JsonConvert.DeserializeObject<RandomResponse>(responseContent);

            Random randomPoke = new Random();

            int randomIndex = randomPoke.Next(0, pokeResponses.results.Count);

            // 獲取pokemon詳細資料
            var response = await _httpClient.GetAsync(pokeResponses.results[randomIndex].url);
            if (!response.IsSuccessStatusCode)
                throw new Exception("無法獲取pokemon資料");

            var content = await response.Content.ReadAsStringAsync();
            var pokeData = JsonConvert.DeserializeObject<Pokemon>(content);

            // 獲取中文名稱
            var speciesResponse = await _httpClient.GetAsync(pokeData.species.url);
            var speciesContent = await speciesResponse.Content.ReadAsStringAsync();
            var speciesData = JsonConvert.DeserializeObject<PokeSpecies>(speciesContent);

            var chineseName = speciesData.names.FirstOrDefault(n => n.language.name == "zh-hant")?.name
                ?? pokeData.species.name;


            if (!string.IsNullOrEmpty(pokeData.sprites.front_shiny))
            {
                // 如果有閃光圖，則這支pokemon有機會為閃光寶可夢
                Random randomShiny = new Random();
                // 閃光寶可夢的機率約為 1/10
                isShiny = randomShiny.Next(1, 11) == 1;
            }

            string imageUrl = isShiny ? pokeData.sprites.front_shiny : pokeData.sprites.front_default;

            // 建立PokeGamePokemon物件
            var pokemon = new PokeGamePokemon
            {
                Id = pokeData.id,
                Name = chineseName,
                CustomName = null,
                ImageUrl = imageUrl,
                HP = pokeData.stats.FirstOrDefault(s => s.stat.name == "hp")?.base_stat ?? 0,
                Attack = pokeData.stats.FirstOrDefault(s => s.stat.name == "attack")?.base_stat ?? 0,
                Defense = pokeData.stats.FirstOrDefault(s => s.stat.name == "defense")?.base_stat ?? 0,
                SpecialAttack = pokeData.stats.FirstOrDefault(s => s.stat.name == "special-attack")?.base_stat ?? 0,
                SpecialDefense = pokeData.stats.FirstOrDefault(s => s.stat.name == "special-defense")?.base_stat ?? 0,
                Speed = pokeData.stats.FirstOrDefault(s => s.stat.name == "speed")?.base_stat ?? 0,
                Types = pokeData.types.Select(t => t.type.name).ToList(),
                CaughtDate = DateTime.UtcNow,
                isShiny = isShiny,
                EvolutionPoints = 0,
                EvolutionStage = 0,
                CanEvolve = false,
                NextEvolutionId = null
            };

            // 檢查這隻寶可夢是否有進化鏈
            await CheckEvolutionChainAsync(pokemon, speciesData);

            return pokemon;
        }

        // 檢查寶可夢的進化鏈
        private async Task CheckEvolutionChainAsync(PokeGamePokemon pokemon, PokeSpecies speciesData)
        {
            try
            {
                // 取得進化鏈資訊
                var evolutionChainResponse = await _httpClient.GetAsync(speciesData.evolution_chain.url);
                if (!evolutionChainResponse.IsSuccessStatusCode) return;

                var evolutionChainContent = await evolutionChainResponse.Content.ReadAsStringAsync();
                var evolutionChain = JsonConvert.DeserializeObject<EvolutionChain>(evolutionChainContent);

                // 找到當前寶可夢在進化鏈中的位置
                FindPokemonInChain(pokemon, evolutionChain.chain, 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"檢查進化鏈時發生錯誤: {ex.Message}");
            }
        }

        private void FindPokemonInChain(PokeGamePokemon pokemon, ChainLink chainLink, int stage)
        {
            // 取得當前節點的 Pokemon ID
            var currentId = int.Parse(chainLink.species.url.Split('/').Where(s => !string.IsNullOrEmpty(s)).Last());

            if (currentId == pokemon.Id)
            {
                // 找到當前寶可夢
                pokemon.EvolutionStage = stage;

                // 檢查是否有下一階段進化
                if (chainLink.evolves_to != null && chainLink.evolves_to.Count > 0)
                {
                    var nextEvolution = chainLink.evolves_to[0];
                    pokemon.NextEvolutionId = int.Parse(nextEvolution.species.url.Split('/').Where(s => !string.IsNullOrEmpty(s)).Last());
                    pokemon.CanEvolve = true;
                }
                return;
            }

            // 遞迴檢查下一階段
            if (chainLink.evolves_to != null)
            {
                foreach (var nextLink in chainLink.evolves_to)
                {
                    FindPokemonInChain(pokemon, nextLink, stage + 1);
                }
            }
        }

        // 執行進化
        private async Task<PokeGamePokemon> EvolvePokemonAsync(PokeGamePokemon pokemon)
        {
            if (!pokemon.CanEvolve || !pokemon.NextEvolutionId.HasValue)
                return pokemon;

            try
            {
                // 獲取進化後的寶可夢資料
                var response = await _httpClient.GetAsync($"{API_BASE_URL}pokemon/{pokemon.NextEvolutionId.Value}");
                if (!response.IsSuccessStatusCode)
                    return pokemon;

                var content = await response.Content.ReadAsStringAsync();
                var pokeData = JsonConvert.DeserializeObject<Pokemon>(content);

                // 獲取中文名稱
                var speciesResponse = await _httpClient.GetAsync(pokeData.species.url);
                var speciesContent = await speciesResponse.Content.ReadAsStringAsync();
                var speciesData = JsonConvert.DeserializeObject<PokeSpecies>(speciesContent);

                var chineseName = speciesData.names.FirstOrDefault(n => n.language.name == "zh-Hant")?.name
                    ?? pokeData.species.name;

                // 保留原本的自訂名稱和閃光狀態
                var customName = pokemon.CustomName;
                var isShiny = pokemon.isShiny;
                var caughtDate = pokemon.CaughtDate;

                // 更新寶可夢資料
                pokemon.Id = pokeData.id;
                pokemon.Name = chineseName;
                pokemon.CustomName = customName;
                pokemon.ImageUrl = isShiny ? pokeData.sprites.front_shiny : pokeData.sprites.front_default;
                pokemon.HP = pokeData.stats.FirstOrDefault(s => s.stat.name == "hp")?.base_stat ?? 0;
                pokemon.Attack = pokeData.stats.FirstOrDefault(s => s.stat.name == "attack")?.base_stat ?? 0;
                pokemon.Defense = pokeData.stats.FirstOrDefault(s => s.stat.name == "defense")?.base_stat ?? 0;
                pokemon.SpecialAttack = pokeData.stats.FirstOrDefault(s => s.stat.name == "special-attack")?.base_stat ?? 0;
                pokemon.SpecialDefense = pokeData.stats.FirstOrDefault(s => s.stat.name == "special-defense")?.base_stat ?? 0;
                pokemon.Speed = pokeData.stats.FirstOrDefault(s => s.stat.name == "speed")?.base_stat ?? 0;
                pokemon.Types = pokeData.types.Select(t => t.type.name).ToList();
                pokemon.isShiny = isShiny;
                pokemon.CaughtDate = caughtDate;
                pokemon.EvolutionPoints = 0; // 重置進化點數
                pokemon.EvolutionStage++;

                // 檢查新的進化鏈
                await CheckEvolutionChainAsync(pokemon, speciesData);

                return pokemon;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"進化時發生錯誤: {ex.Message}");
                return pokemon;
            }
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
                    var evolutionInfo = pokemon.CanEvolve
                        ? $"\n進化進度: {pokemon.EvolutionPoints}/3 ⭐"
                        : "\n無法進化";

                    embed.AddField(
                        $"{i + 1}. {displayName}",
                        $"原名: {pokemon.Name}\n" +
                        $"屬性: {string.Join(", ", pokemon.Types)}\n" +
                        $"HP: {pokemon.HP} | 攻: {pokemon.Attack} | 防: {pokemon.Defense}\n" +
                        $"抓到時間: {pokemon.CaughtDate:yyyy-MM-dd}" +
                        evolutionInfo,
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

        // 釋放寶可夢
        public async Task<(Embed embed, ComponentBuilder component)> ReleasePokemonAsync(ulong userId, string userName, int pokemonIndex)
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

                if (player.CaughtPokemon.Count < 6)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 你的pokemon數量不足！")
                        .WithDescription("至少需要 6 隻pokemon才能蛋雕喔！")
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
                var pokemonName = pokemon.CustomName ?? pokemon.Name;

                player.CaughtPokemon.RemoveAt(pokemonIndex - 1);
                await SavePlayerDataAsync(player);

                var embed = new EmbedBuilder()
                    .WithTitle("👋 釋放pokemon")
                    .WithDescription($"你釋放了 **{pokemonName}**！\n他將會記住 他將會找到你 他將會回來草飼你")
                    .WithThumbnailUrl(pokemon.ImageUrl)
                    .WithColor(Color.Blue)
                    .WithFooter($"目前剩餘 {player.CaughtPokemon.Count} 隻pokemon")
                    .Build();

                return (embed, new ComponentBuilder());
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"釋放pokemon時發生錯誤: {ex.Message}").Item2, new ComponentBuilder());
            }
        }
        #endregion

        #region 對戰系統
        public async Task<(Embed embed, ComponentBuilder component)> StartBattleSearchAsync(ulong userId, string userName,int index)
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
                int pokemonIndex = index;

                if (index == 0)
                {
                    Random random = new Random();
                    pokemonIndex = random.Next(1, player.CaughtPokemon.Count + 1); // 隨機選擇一隻寶可夢參加對戰
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
1. {player1Name} 的 自訂名稱:{pokemon1.CustomName ?? pokemon1.Name}，真實名稱:{pokemon1.Name}
   - 屬性: {string.Join(", ", pokemon1.Types)}
   - HP: {pokemon1.HP}, 攻擊: {pokemon1.Attack}, 防禦: {pokemon1.Defense}
   - 特攻: {pokemon1.SpecialAttack}, 特防: {pokemon1.SpecialDefense}, 速度: {pokemon1.Speed}

2. {player2Name} 的 自訂名稱:{pokemon2.CustomName ?? pokemon2.Name}，真實名稱:{pokemon2.Name}
   - 屬性: {string.Join(", ", pokemon2.Types)}
   - HP: {pokemon2.HP}, 攻擊: {pokemon2.Attack}, 防禦: {pokemon2.Defense}
   - 特攻: {pokemon2.SpecialAttack}, 特防: {pokemon2.SpecialDefense}, 速度: {pokemon2.Speed}

請根據以上數據和屬性相剋關係，判斷誰會獲勝，並用繁體中文描述一段精彩的對戰過程。
要以該寶可夢真實的技能來敘述，期間有自訂名稱的話就要叫自訂名稱，沒有的話就叫真實名稱。
最後請在描述的最後一行明確說明勝者是誰，格式為「勝者：[玩家名稱]」";

                // 呼叫 AI 判斷對戰結果
                var aiResponse = await _aiService.GenerateSimpleTextAsync(battlePrompt);

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

                // 更新戰績和進化點數（只更新真實玩家，ID 為 0 的是電腦對手）
                string evolutionMessage = "";

                if (winnerId != 0)
                {
                    var winner = await GetPlayerDataAsync(winnerId, winnerName);
                    winner.TotalBattles++;
                    winner.Wins++;

                    // 更新勝利者寶可夢的進化點數 (+2)
                    winnerPokemon.EvolutionPoints += 2;

                    // 檢查是否達到進化條件（3點）
                    if (winnerPokemon.CanEvolve && winnerPokemon.EvolutionPoints >= 3)
                    {
                        var oldName = winnerPokemon.Name;
                        winnerPokemon = await EvolvePokemonAsync(winnerPokemon);
                        evolutionMessage = $"\n\n✨ **恭喜！{oldName} 進化成 {winnerPokemon.Name} 了！** ✨";

                        // 更新玩家資料中的寶可夢
                        var pokemonInList = winner.CaughtPokemon.FirstOrDefault(p => p.Id == pokemon1.Id && p.CaughtDate == pokemon1.CaughtDate);
                        if (pokemonInList != null)
                        {
                            var index = winner.CaughtPokemon.IndexOf(pokemonInList);
                            winner.CaughtPokemon[index] = winnerPokemon;
                        }
                    }

                    await SavePlayerDataAsync(winner);
                }

                if (loserId != 0)
                {
                    var loser = await GetPlayerDataAsync(loserId, loserName);
                    loser.TotalBattles++;
                    loser.Losses++;

                    // 更新失敗者寶可夢的進化點數 (+1)
                    loserPokemon.EvolutionPoints += 1;

                    // 檢查是否達到進化條件（3點）
                    if (loserPokemon.CanEvolve && loserPokemon.EvolutionPoints >= 3)
                    {
                        var oldName = loserPokemon.Name;
                        loserPokemon = await EvolvePokemonAsync(loserPokemon);
                        if (string.IsNullOrEmpty(evolutionMessage))
                            evolutionMessage = $"\n\n✨ **雖然快4了，但 {oldName} 進化成 {loserPokemon.Name} 了！** ✨";
                        else
                            evolutionMessage += $"\n✨ **{oldName} 也進化成 {loserPokemon.Name} 了！** ✨";

                        // 更新玩家資料中的寶可夢
                        var pokemonInList = loser.CaughtPokemon.FirstOrDefault(p => p.Id == pokemon2.Id && p.CaughtDate == pokemon2.CaughtDate);
                        if (pokemonInList != null)
                        {
                            var index = loser.CaughtPokemon.IndexOf(pokemonInList);
                            loser.CaughtPokemon[index] = loserPokemon;
                        }
                    }

                    await SavePlayerDataAsync(loser);
                }

                // 取得戰績資訊（用於顯示）
                var winnerStats = winnerId != 0 ? await GetPlayerDataAsync(winnerId, winnerName) : null;
                var loserStats = loserId != 0 ? await GetPlayerDataAsync(loserId, loserName) : null;

                // 建立對戰結果訊息
                var embedBuilder = new EmbedBuilder()
                    .WithTitle("⚔️ pokemon對戰結果 ⚔️")
                    .WithDescription(aiResponse + evolutionMessage)
                    .WithColor(Color.Gold);

                // 勝者資訊
                if (winnerStats != null)
                {
                    embedBuilder.AddField($"🏆 勝者: {winnerName}",
                        $"{winnerPokemon.CustomName ?? winnerPokemon.Name}\n" +
                        $"戰績: {winnerStats.Wins}勝 {winnerStats.Losses}敗", true);
                }
                else
                {
                    embedBuilder.AddField($"🏆 勝者: {winnerName}",
                        $"{winnerPokemon.CustomName ?? winnerPokemon.Name}\n" +
                        $"(電腦對手)", true);
                }

                // 敗者資訊
                if (loserStats != null)
                {
                    embedBuilder.AddField($"😢 敗者: {loserName}",
                        $"{loserPokemon.CustomName ?? loserPokemon.Name}\n" +
                        $"戰績: {loserStats.Wins}勝 {loserStats.Losses}敗", true);
                }
                else
                {
                    embedBuilder.AddField($"😢 敗者: {loserName}",
                        $"{loserPokemon.CustomName ?? loserPokemon.Name}\n" +
                        $"(電腦對手)", true);
                }

                // 只有真實玩家獲勝才給獎勵
                if (winnerId != 0)
                {
                    embedBuilder.AddField("🎁 獲勝獎勵", "恭喜獲得一次額外抓pokemon的機會！", false);

                    // 給勝者一次額外的抓寶機會（重置今日抓寶紀錄）
                    var winnerForReward = await GetPlayerDataAsync(winnerId, winnerName);
                    winnerForReward.LastCatchDate = null;
                    await SavePlayerDataAsync(winnerForReward);
                }

                embedBuilder.WithCurrentTimestamp();
                var embed = embedBuilder.Build();

                return (embed, new ComponentBuilder());
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"執行對戰時發生錯誤: {ex.Message}").Item2, new ComponentBuilder());
            }
        }

        // 測試用對戰 - 生成假對手
        public async Task<(Embed embed, ComponentBuilder component)> StartTestBattleAsync(ulong userId, string userName, int pokemonIndex)
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

                var playerPokemon = player.CaughtPokemon[pokemonIndex - 1];

                // 生成一個隨機的假對手pokemon
                var opponentPokemon = await GetRandomPokemonAsync();
                var opponentName = "電腦對手";

                // 開始測試對戰
                return await ExecuteBattleAsync(
                    userId, userName, playerPokemon,
                    0, opponentName, opponentPokemon  // 使用 0 作為電腦對手的 ID
                );
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"測試對戰時發生錯誤: {ex.Message}").Item2, new ComponentBuilder());
            }
        }
        #endregion

        #region 資料操作 (支援 Redis 或記憶體儲存)
        private async Task<PokeGamePlayer> GetPlayerDataAsync(ulong userId, string userName)
        {
            if (_useRedis)
            {
                try
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
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Redis 讀取失敗，切換到記憶體儲存: {ex.Message}");
                    // Redis 失敗時降級到記憶體儲存
                    if (!_memoryPlayers.ContainsKey(userId))
                    {
                        _memoryPlayers[userId] = new PokeGamePlayer
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
                    return await Task.FromResult(_memoryPlayers[userId]);
                }
            }
            else
            {
                // 使用記憶體儲存
                if (!_memoryPlayers.ContainsKey(userId))
                {
                    _memoryPlayers[userId] = new PokeGamePlayer
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
                return await Task.FromResult(_memoryPlayers[userId]);
            }
        }

        private async Task SavePlayerDataAsync(PokeGamePlayer player)
        {
            if (_useRedis)
            {
                try
                {
                    var key = $"{PLAYER_DATA_KEY}{player.UserId}";
                    var data = JsonConvert.SerializeObject(player);
                    await _redisDb.StringSetAsync(key, data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Redis 寫入失敗，切換到記憶體儲存: {ex.Message}");
                    // Redis 失敗時降級到記憶體儲存
                    _memoryPlayers[player.UserId] = player;
                }
            }
            else
            {
                // 使用記憶體儲存
                _memoryPlayers[player.UserId] = player;
                await Task.CompletedTask;
            }
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

            if (_useRedis)
            {
                try
                {
                    var data = JsonConvert.SerializeObject(matchmaking);
                    await _redisDb.HashSetAsync(MATCHMAKING_KEY, userId.ToString(), data);

                    // 設定 5 分鐘過期
                    await _redisDb.KeyExpireAsync(MATCHMAKING_KEY, TimeSpan.FromMinutes(5));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Redis 配對寫入失敗，切換到記憶體儲存: {ex.Message}");
                    // Redis 失敗時降級到記憶體儲存
                    _memoryMatchmaking[userId] = matchmaking;
                }
            }
            else
            {
                // 使用記憶體儲存
                _memoryMatchmaking[userId] = matchmaking;
                await Task.CompletedTask;
            }
        }

        private async Task<List<BattleMatchmaking>> GetWaitingPlayersAsync()
        {
            var result = new List<BattleMatchmaking>();

            if (_useRedis)
            {
                try
                {
                    var entries = await _redisDb.HashGetAllAsync(MATCHMAKING_KEY);

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
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Redis 配對讀取失敗，切換到記憶體儲存: {ex.Message}");
                    // Redis 失敗時降級到記憶體儲存
                    var expiredKeys = new List<ulong>();
                    foreach (var kvp in _memoryMatchmaking)
                    {
                        if ((DateTime.UtcNow - kvp.Value.SearchStartTime).TotalMinutes < 5)
                        {
                            result.Add(kvp.Value);
                        }
                        else
                        {
                            expiredKeys.Add(kvp.Key);
                        }
                    }

                    foreach (var key in expiredKeys)
                    {
                        _memoryMatchmaking.Remove(key);
                    }
                }
            }
            else
            {
                // 使用記憶體儲存
                var expiredKeys = new List<ulong>();
                foreach (var kvp in _memoryMatchmaking)
                {
                    if ((DateTime.UtcNow - kvp.Value.SearchStartTime).TotalMinutes < 5)
                    {
                        result.Add(kvp.Value);
                    }
                    else
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                // 清理過期的配對
                foreach (var key in expiredKeys)
                {
                    _memoryMatchmaking.Remove(key);
                }

                await Task.CompletedTask;
            }

            return result;
        }

        private async Task RemoveFromMatchmakingAsync(ulong userId)
        {
            if (_useRedis)
            {
                try
                {
                    await _redisDb.HashDeleteAsync(MATCHMAKING_KEY, userId.ToString());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Redis 配對刪除失敗，切換到記憶體儲存: {ex.Message}");
                    // Redis 失敗時降級到記憶體儲存
                    _memoryMatchmaking.Remove(userId);
                }
            }
            else
            {
                // 使用記憶體儲存
                _memoryMatchmaking.Remove(userId);
                await Task.CompletedTask;
            }
        }
        #endregion
    }
}
