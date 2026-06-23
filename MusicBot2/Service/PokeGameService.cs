using Discord;
using Discord.WebSocket;
using MusicBot2.Helpers;
using MusicBot2.Models;
using MusicBot2.Service;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Channels;
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
        private static readonly Dictionary<ulong, BattleMatchmaking2V2> _memoryMatchmaking2V2 = new Dictionary<ulong, BattleMatchmaking2V2>();
        private static TeamFightBoss _memoryTeamFightBoss = null;
        private static List<int> _memoryLegendaryPokemonIds = new List<int>();

        private const string API_BASE_URL = "https://pokeapi.co/api/v2/";
        private const string PLAYER_DATA_KEY = "pokegame:player:";
        private const string MATCHMAKING_KEY = "pokegame:matchmaking";
        private const string MATCHMAKING_KEY_2V2 = "pokegame:matchmaking:2v2";
        private const string TEAM_FIGHT_BOSS_KEY = "pokegame:teamfight:boss";
        private const string LEGENDARY_POKEMON_KEY = "pokegame:legendary:ids";

        private readonly DiscordSocketClient _client;



        public PokeGameService(string redisConnectionString, OpenRouterService aiService, DiscordSocketClient client)
        {
            _httpClient = new HttpClient();
            _aiService = aiService;
            _client = client;

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

            // 初始化傳說/神話 Pokemon 列表
            _ = InitializeLegendaryPokemonAsync();
        }

        private async Task InitializeLegendaryPokemonAsync()
        {
            try
            {
                // 檢查是否已經初始化過
                if (_useRedis)
                {
                    var existingData = await _redisDb.StringGetAsync(LEGENDARY_POKEMON_KEY);
                    if (!existingData.IsNullOrEmpty)
                    {
                        Console.WriteLine("✅ 傳說/神話 Pokemon 列表已存在於 Redis");
                        return;
                    }
                }
                else if (_memoryLegendaryPokemonIds.Count > 0)
                {
                    Console.WriteLine("✅ 傳說/神話 Pokemon 列表已存在於記憶體");
                    return;
                }

                Console.WriteLine("🔄 開始載入傳說/神話 Pokemon 列表...");
                var legendaryIds = new List<int>();

                // 獲取所有 Pokemon species
                string url = $"{API_BASE_URL}pokemon-species?limit=10000&offset=0";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("❌ 無法獲取 Pokemon species 資料");
                    return;
                }

                var content = await response.Content.ReadAsStringAsync();
                var speciesList = JsonConvert.DeserializeObject<RandomResponse>(content);

                int count = 0;
                // 檢查每個 Pokemon 是否為傳說或神話
                foreach (var species in speciesList.results)
                {
                    try
                    {
                        var speciesResponse = await _httpClient.GetAsync(species.url);
                        if (speciesResponse.IsSuccessStatusCode)
                        {
                            var speciesContent = await speciesResponse.Content.ReadAsStringAsync();
                            var speciesData = JsonConvert.DeserializeObject<PokeSpecies>(speciesContent);

                            if (speciesData.is_legendary || speciesData.is_mythical)
                            {
                                legendaryIds.Add(speciesData.id);
                                count++;
                            }
                        }

                        // 避免太頻繁呼叫 API
                        await Task.Delay(50);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ 檢查 Pokemon {species.name} 時發生錯誤: {ex.Message}");
                    }
                }

                // 儲存到 Redis 或記憶體
                if (_useRedis)
                {
                    var data = JsonConvert.SerializeObject(legendaryIds);
                    await _redisDb.StringSetAsync(LEGENDARY_POKEMON_KEY, data);
                    Console.WriteLine($"✅ 已將 {count} 隻傳說/神話 Pokemon 儲存到 Redis");
                }
                else
                {
                    _memoryLegendaryPokemonIds = legendaryIds;
                    Console.WriteLine($"✅ 已將 {count} 隻傳說/神話 Pokemon 儲存到記憶體");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 初始化傳說/神話 Pokemon 列表時發生錯誤: {ex.Message}");
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

                // 檢查pokemon數量是否已達上限
                if (player.CaughtPokemon.Count >= 10)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ pokemon數量已達上限！")
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
                // 如果有閃光圖，則這支pokemon有機會為閃光pokemon
                Random randomShiny = new Random();
                // 閃光pokemon的機率約為 1/10
                isShiny = randomShiny.Next(1, 11) == 1;
            }

            string imageUrl = isShiny ? pokeData.sprites.front_shiny : (pokeData.sprites.other.official_artwork.front_default ?? pokeData.sprites.front_default);

            // 建立PokeGamePokemon物件
            var pokemon = new PokeGamePokemon
            {
                Id = pokeData.id,
                Name = chineseName,
                CustomName = null,
                ImageUrl = imageUrl,
                Back_ImageUrl = isShiny ? pokeData.sprites.back_shiny : pokeData.sprites.back_default,
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
                NextEvolutionId = null,
                Front_GIF = isShiny ? pokeData.sprites.other.showdown.front_shiny : pokeData.sprites.other.showdown.front_default,
                Back_GIF = isShiny ? pokeData.sprites.other.showdown.back_shiny : pokeData.sprites.other.showdown.back_default
            };

            // 檢查這隻pokemon是否有進化鏈
            await CheckEvolutionChainAsync(pokemon, speciesData);

            return pokemon;
        }

        // 檢查pokemon的進化鏈
        private async Task CheckEvolutionChainAsync(PokeGamePokemon pokemon, PokeSpecies speciesData)
        {
            try
            {
                // 取得進化鏈資訊
                var evolutionChainResponse = await _httpClient.GetAsync(speciesData.evolution_chain.url);
                if (!evolutionChainResponse.IsSuccessStatusCode) return;

                var evolutionChainContent = await evolutionChainResponse.Content.ReadAsStringAsync();
                var evolutionChain = JsonConvert.DeserializeObject<EvolutionChain>(evolutionChainContent);

                // 找到當前pokemon在進化鏈中的位置
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
                // 找到當前pokemon
                pokemon.EvolutionStage = stage;

                // 檢查是否有下一階段進化
                if (chainLink.evolves_to != null && chainLink.evolves_to.Count > 0)
                {
                    //下一種進化有多個的話就隨
                    Random random = new Random();
                    int index = random.Next(0, chainLink.evolves_to.Count);
                    var nextEvolution = chainLink.evolves_to[index];
                    pokemon.NextEvolutionId = int.Parse(nextEvolution.species.url.Split('/').Where(s => !string.IsNullOrEmpty(s)).Last());
                    pokemon.CanEvolve = true;
                }
                else
                {
                    // 沒有下一階段進化，是最終形態
                    pokemon.CanEvolve = false;
                    pokemon.NextEvolutionId = null;
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
                // 獲取進化後的pokemon資料
                var response = await _httpClient.GetAsync($"{API_BASE_URL}pokemon/{pokemon.NextEvolutionId.Value}");
                if (!response.IsSuccessStatusCode)
                    return pokemon;

                var content = await response.Content.ReadAsStringAsync();
                var pokeData = JsonConvert.DeserializeObject<Pokemon>(content);

                // 獲取中文名稱
                var speciesResponse = await _httpClient.GetAsync(pokeData.species.url);
                var speciesContent = await speciesResponse.Content.ReadAsStringAsync();
                var speciesData = JsonConvert.DeserializeObject<PokeSpecies>(speciesContent);

                var chineseName = speciesData.names.FirstOrDefault(n => n.language.name == "zh-hant")?.name
                    ?? pokeData.species.name;

                // 保留原本的自訂名稱和閃光狀態
                var customName = pokemon.CustomName;
                var isShiny = pokemon.isShiny;
                var caughtDate = pokemon.CaughtDate;

                // 更新pokemon資料
                pokemon.Id = pokeData.id;
                pokemon.Name = chineseName;
                pokemon.CustomName = customName;
                pokemon.ImageUrl = isShiny ? pokeData.sprites.front_shiny : (pokeData.sprites.other.official_artwork.front_default ?? pokeData.sprites.front_default);
                pokemon.HP = pokeData.stats.FirstOrDefault(s => s.stat.name == "hp")?.base_stat ?? 0;
                pokemon.Attack = pokeData.stats.FirstOrDefault(s => s.stat.name == "attack")?.base_stat ?? 0;
                pokemon.Defense = pokeData.stats.FirstOrDefault(s => s.stat.name == "defense")?.base_stat ?? 0;
                pokemon.SpecialAttack = pokeData.stats.FirstOrDefault(s => s.stat.name == "special-attack")?.base_stat ?? 0;
                pokemon.SpecialDefense = pokeData.stats.FirstOrDefault(s => s.stat.name == "special-defense")?.base_stat ?? 0;
                pokemon.Speed = pokeData.stats.FirstOrDefault(s => s.stat.name == "speed")?.base_stat ?? 0;
                pokemon.Types = pokeData.types.Select(t => t.type.name).ToList();
                pokemon.Back_ImageUrl = isShiny ? pokeData.sprites.back_shiny : pokeData.sprites.back_default;
                pokemon.Front_GIF = isShiny ? pokeData.sprites.other.showdown.front_shiny : pokeData.sprites.other.showdown.front_default;
                pokemon.Back_GIF = isShiny ? pokeData.sprites.other.showdown.back_shiny : pokeData.sprites.other.showdown.back_default;
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
                        : $"\n✨ 最終形態 (階段 {pokemon.EvolutionStage})";

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

        // 釋放pokemon
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
                    .WithDescription($"你釋放了 **{pokemonName}**！\n他將會記住你 他將會找到你 他將會回來草飼你")
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
        public async Task<(Embed embed, ComponentBuilder component)> StartBattleSearchAsync(ulong userId, string userName,int index, IMessageChannel channel)
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
                    pokemonIndex = random.Next(1, player.CaughtPokemon.Count + 1); // 隨機選擇一隻pokemon參加對戰
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
                    await RemoveFromMatchmakingAsync(opponent.UserId);

                    // 取得對手的頻道
                    var opponentChannel = _client.GetChannel(opponent.ChannelId) as IMessageChannel;

                    // ✅ 分別通知雙方頻道
                    await NotifyBattleStartAsync(channel, userId, pokemon, opponent);
                    if (opponentChannel != null && opponentChannel.Id != channel.Id)
                    {
                        await NotifyBattleStartAsync(opponentChannel, opponent.UserId, opponent.Pokemon,
                            new BattleMatchmaking { UserId = userId, UserName = userName, Pokemon = pokemon });
                    }

                    var (embed, component) = await ExecuteBattleAsync(userId, userName, pokemon,
                        opponent.UserId, opponent.UserName, opponent.Pokemon);

                    // 在對方頻道發送對戰結果
                    if(opponentChannel != null && opponentChannel.Id != channel.Id)
                    {
                        await opponentChannel.SendMessageAsync(embed: embed, components: component.Build());
                    }

                    return (embed, component);
                }
                else
                {
                    // 加入配對池時一併儲存 ChannelId
                    await AddToMatchmakingAsync(userId, userName, pokemon, channel.Id);  // ← 新增 channelId

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

        private async Task NotifyBattleStartAsync(
            IMessageChannel channel, ulong viewerUserId,
            PokeGamePokemon pokemon, BattleMatchmaking opponent)
        {
            // 發送對方的 pokemon 圖（從對方視角是敵方）
            var opponentImageUrl = opponent.Pokemon.Front_GIF ?? opponent.Pokemon.ImageUrl;
            if (!string.IsNullOrEmpty(opponentImageUrl))
                await channel.SendMessageAsync(opponentImageUrl);

            // 發送自己的 pokemon 圖（背面）
            var myImageUrl = (pokemon.Back_GIF ?? pokemon.Back_ImageUrl) ?? pokemon.ImageUrl;
            if (!string.IsNullOrEmpty(myImageUrl))
                await channel.SendMessageAsync(myImageUrl);
        }

        private async Task NotifyBattleStart2V2Async(
            IMessageChannel channel, ulong viewerUserId,
            PokeGamePokemon pokemon1, PokeGamePokemon pokemon2
            , BattleMatchmaking2V2 opponent)
        {
            //對戰前先丟一次雙方pokemon圖片
            if (!string.IsNullOrEmpty(opponent.Pokemon1.Front_GIF ?? opponent.Pokemon1.ImageUrl))
            {
                await channel.SendMessageAsync(opponent.Pokemon1.Front_GIF ?? opponent.Pokemon1.ImageUrl);

            }
            if (!string.IsNullOrEmpty(opponent.Pokemon2.Front_GIF ?? opponent.Pokemon2.ImageUrl))
            {
                await channel.SendMessageAsync(opponent.Pokemon2.Front_GIF ?? opponent.Pokemon2.ImageUrl);
            }

            await channel.SendMessageAsync("==========對上==========");

            if (!string.IsNullOrEmpty((pokemon1.Back_GIF ?? pokemon1.Back_ImageUrl) ?? pokemon1.ImageUrl))
            {
                await channel.SendMessageAsync((pokemon1.Back_GIF ?? pokemon1.Back_ImageUrl) ?? pokemon1.ImageUrl);
            }
            if (!string.IsNullOrEmpty((pokemon2.Back_GIF ?? pokemon2.Back_ImageUrl) ?? pokemon2.ImageUrl))
            {
                await channel.SendMessageAsync((pokemon2.Back_GIF ?? pokemon2.Back_ImageUrl) ?? pokemon2.ImageUrl);
            }
        }


        public async Task<(Embed embed, ComponentBuilder component)> Start2v2BattleSearchAsync(ulong userId, string userName, int index1,int index2, IMessageChannel channel)
        {
            try
            {
                var player = await GetPlayerDataAsync(userId, userName);

                if (player.CaughtPokemon.Count <= 1)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 你還沒有足夠的pokemon阿低底")
                        .WithDescription("先去抓至少兩隻pokemon")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }
                if (index1 == index2)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 無效的pokemon編號")
                        .WithDescription("想搞影分身是不是?")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }
                if (index1 < 1 || index1 > player.CaughtPokemon.Count || index2 < 1 || index2 > player.CaughtPokemon.Count)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 無效的pokemon編號！")
                        .WithDescription($"請輸入 1 到 {player.CaughtPokemon.Count} 之間的編號")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }

                var pokemon1 = player.CaughtPokemon[index1 - 1];
                var pokemon2 = player.CaughtPokemon[index2 - 1];



                // 檢查是否有其他玩家在等待
                var waitingPlayers = await GetWaitingPlayers2V2Async();
                var opponent = waitingPlayers.FirstOrDefault(p => p.UserId != userId);

                if (opponent != null)
                {
                    // 找到對手，開始對戰！
                    await RemoveFromMatchmaking2V2Async(opponent.UserId);

                    await NotifyBattleStart2V2Async(channel, userId, pokemon1, pokemon2, opponent);
                    var opponentChannel = _client.GetChannel(opponent.ChannelId) as IMessageChannel;
                    if (opponentChannel != null && opponentChannel.Id != channel.Id)
                    {
                        await NotifyBattleStart2V2Async(opponentChannel, opponent.UserId, opponent.Pokemon1, opponent.Pokemon2,
                            new BattleMatchmaking2V2 { UserId = userId, UserName = userName, Pokemon1 = pokemon1,Pokemon2 = pokemon2 });
                    }
                    var (embed, component) = await Execute2V2BattleAsync(userId, userName, pokemon1, pokemon2, opponent.UserId, opponent.UserName, opponent.Pokemon1, opponent.Pokemon2);

                    if (opponentChannel != null && opponentChannel.Id != channel.Id)
                    {
                        await opponentChannel.SendMessageAsync(embed: embed, components: component.Build());
                    }
                    return (embed, component);
                }
                else
                {
                    // 沒有對手，加入配對池
                    await AddToMatchmaking2V2Async(userId, userName, pokemon1,pokemon2, channel.Id);

                    var embed = new EmbedBuilder()
                        .WithTitle("🔍 尋找對手中...")
                        .WithDescription($"使用 **{pokemon1.CustomName ?? pokemon1.Name}** 和 **{pokemon2.CustomName ?? pokemon2.Name}** 尋找對手中！\n請等待其他玩家加入對戰...")
                        .WithThumbnailUrl(pokemon2.ImageUrl)
                        .WithImageUrl(pokemon1.ImageUrl)
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
        private async Task<(Embed embed, ComponentBuilder component)> Execute2V2BattleAsync(
            ulong player1Id, string player1Name, PokeGamePokemon pokemon1, PokeGamePokemon pokemon2,
            ulong player2Id, string player2Name, PokeGamePokemon opponentPokemon1, PokeGamePokemon opponentPokemon2)
        {
            try
            {
                // 準備對戰資訊給 AI 判斷
                string battlePrompt = $@"請模擬一場精彩的pokemon對戰，並判斷勝負。

對戰雙方：
1. {player1Name} 的 第一隻寶可夢:
    自訂名稱:{pokemon1.CustomName ?? pokemon1.Name}，真實名稱:{pokemon1.Name}
   - 屬性: {string.Join(", ", pokemon1.Types)}
   - HP: {pokemon1.HP}, 攻擊: {pokemon1.Attack}, 防禦: {pokemon1.Defense}
   - 特攻: {pokemon1.SpecialAttack}, 特防: {pokemon1.SpecialDefense}, 速度: {pokemon1.Speed}
   - 是否為閃光: {(pokemon1.isShiny ? "是" : "否")}

2. {player1Name} 的 第二隻寶可夢:
    自訂名稱:{pokemon2.CustomName ?? pokemon2.Name}，真實名稱:{pokemon2.Name}
   - 屬性: {string.Join(", ", pokemon2.Types)}
   - HP: {pokemon2.HP}, 攻擊: {pokemon2.Attack}, 防禦: {pokemon2.Defense}
   - 特攻: {pokemon2.SpecialAttack}, 特防: {pokemon2.SpecialDefense}, 速度: {pokemon2.Speed}
   - 是否為閃光: {(pokemon2.isShiny ? "是" : "否")}

========對上========

3. {player2Name} 的第一隻寶可夢:
    自訂名稱:{opponentPokemon1.CustomName ?? opponentPokemon1.Name}，真實名稱:{opponentPokemon1.Name}
   - 屬性: {string.Join(", ", opponentPokemon1.Types)}
   - HP: {opponentPokemon1.HP}, 攻擊: {opponentPokemon1.Attack}, 防禦: {opponentPokemon1.Defense}
   - 特攻: {opponentPokemon1.SpecialAttack}, 特防: {opponentPokemon1.SpecialDefense}, 速度: {opponentPokemon1.Speed}
   - 是否為閃光: {(opponentPokemon1.isShiny ? "是" : "否")}

4. {player2Name} 的第二隻寶可夢:
    自訂名稱:{opponentPokemon2.CustomName ?? opponentPokemon2.Name}，真實名稱:{opponentPokemon2.Name}
   - 屬性: {string.Join(", ", opponentPokemon2.Types)}
   - HP: {opponentPokemon2.HP}, 攻擊: {opponentPokemon2.Attack}, 防禦: {opponentPokemon2.Defense}
   - 特攻: {opponentPokemon2.SpecialAttack}, 特防: {opponentPokemon2.SpecialDefense}, 速度: {opponentPokemon2.Speed}
   - 是否為閃光: {(opponentPokemon2.isShiny ? "是" : "否")}


請根據以上數據和屬性相剋關係，判斷誰會獲勝，並用繁體中文描述一段精彩的對戰過程。
要以該pokemon真實的技能來敘述，期間有自訂名稱的話就要叫自訂名稱，沒有的話就叫真實名稱。
如果是閃光的，對話中要提到閃光的特效，但閃光完全不影響戰鬥結果。
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
                                       pokemon1.SpecialAttack + pokemon1.SpecialDefense + pokemon1.Speed +
                                       pokemon2.HP + pokemon2.Attack + pokemon2.Defense +
                                       pokemon2.SpecialAttack + pokemon2.SpecialDefense + pokemon2.Speed;
                    int opponentPokemonTotal = opponentPokemon1.HP + opponentPokemon1.Attack + opponentPokemon1.Defense +
                                       opponentPokemon1.SpecialAttack + opponentPokemon1.SpecialDefense + opponentPokemon1.Speed +
                                       opponentPokemon2.HP + opponentPokemon2.Attack + opponentPokemon2.Defense +
                                       opponentPokemon2.SpecialAttack + opponentPokemon2.SpecialDefense + opponentPokemon2.Speed;
                    player1Wins = pokemon1Total > opponentPokemonTotal;
                }

                var winnerId = player1Wins ? player1Id : player2Id;
                var winnerName = player1Wins ? player1Name : player2Name;
                var winnerPokemon1 = player1Wins ? pokemon1 : opponentPokemon1;
                var winnerPokemon2 = player1Wins ? pokemon2 : opponentPokemon2;

                var loserId = player1Wins ? player2Id : player1Id;
                var loserName = player1Wins ? player2Name : player1Name;

                var loserPokemon1 = player1Wins ? opponentPokemon1 : pokemon1;
                var loserPokemon2 = player1Wins ? opponentPokemon2 : pokemon2;

                // 更新戰績和進化點數（只更新真實玩家，ID 為 0 的是電腦對手）
                string evolutionMessage = "";

                if (winnerId != 0)
                {
                    var winner = await GetPlayerDataAsync(winnerId, winnerName);
                    winner.TotalBattles++;
                    winner.Wins++;

                    // 更新勝利者pokemon的進化點數 (+2)
                    winnerPokemon1.EvolutionPoints += 2;
                    winnerPokemon2.EvolutionPoints += 2;

                    int preId1 = winnerPokemon1.Id;
                    int preId2 = winnerPokemon2.Id;

                    // 檢查是否達到進化條件（3點）
                    if (winnerPokemon1.CanEvolve && winnerPokemon1.EvolutionPoints >= 3)
                    {
                        var oldName = winnerPokemon1.Name;
                        winnerPokemon1 = await EvolvePokemonAsync(winnerPokemon1);
                        evolutionMessage = $"\n\n✨ **恭喜！{oldName} 進化成 {winnerPokemon1.Name} 了！** ✨";

                        // 更新玩家資料中的pokemon
                        var pokemonInList = winner.CaughtPokemon.FirstOrDefault(p => p.Id == preId1 && p.CaughtDate == winnerPokemon1.CaughtDate);
                        if (pokemonInList != null)
                        {
                            var index = winner.CaughtPokemon.IndexOf(pokemonInList);
                            winner.CaughtPokemon[index] = winnerPokemon1;
                        }
                    }

                    if (winnerPokemon2.CanEvolve && winnerPokemon2.EvolutionPoints >= 3)
                    {
                        var oldName = winnerPokemon2.Name;
                        winnerPokemon2 = await EvolvePokemonAsync(winnerPokemon2);
                        evolutionMessage = $"\n\n✨ **恭喜！{oldName} 進化成 {winnerPokemon2.Name} 了！** ✨";
                        // 更新玩家資料中的pokemon
                        var pokemonInList = winner.CaughtPokemon.FirstOrDefault(p => p.Id == preId2 && p.CaughtDate == winnerPokemon2.CaughtDate);
                        if (pokemonInList != null)
                        {
                            var index = winner.CaughtPokemon.IndexOf(pokemonInList);
                            winner.CaughtPokemon[index] = winnerPokemon2;
                        }
                    }

                    winner.CaughtPokemon = winner.CaughtPokemon.Select(p =>
                    {
                        if (p.Id == preId1)
                            return winnerPokemon1;
                        if (p.Id == preId2)
                            return winnerPokemon2;
                        return p;
                    }).ToList();

                    await SavePlayerDataAsync(winner);
                }

                if (loserId != 0)
                {
                    var loser = await GetPlayerDataAsync(loserId, loserName);
                    loser.TotalBattles++;
                    loser.Losses++;

                    // 更新失敗者pokemon的進化點數 (+1)
                    loserPokemon1.EvolutionPoints += 1;
                    loserPokemon2.EvolutionPoints += 1;

                    int preId1 = loserPokemon1.Id;
                    int preId2 = loserPokemon2.Id;

                    // 檢查是否達到進化條件（3點）
                    if (loserPokemon1.CanEvolve && loserPokemon1.EvolutionPoints >= 3)
                    {
                        var oldName = loserPokemon1.Name;
                        loserPokemon1 = await EvolvePokemonAsync(loserPokemon1);
                        if (string.IsNullOrEmpty(evolutionMessage))
                            evolutionMessage = $"\n\n✨ **雖然快4了，但 {oldName} 進化成 {loserPokemon1.Name} 了！** ✨";
                        else
                            evolutionMessage += $"\n✨ **{oldName} 也進化成 {loserPokemon1.Name} 了！** ✨";

                        // 更新玩家資料中的pokemon
                        var pokemonInList = loser.CaughtPokemon.FirstOrDefault(p => p.Id == preId1 && p.CaughtDate == loserPokemon1.CaughtDate);
                        if (pokemonInList != null)
                        {
                            var index = loser.CaughtPokemon.IndexOf(pokemonInList);
                            loser.CaughtPokemon[index] = loserPokemon1;
                        }
                    }

                    if (loserPokemon2.CanEvolve && loserPokemon2.EvolutionPoints >= 3)
                    {
                        var oldName = loserPokemon2.Name;
                        loserPokemon2 = await EvolvePokemonAsync(loserPokemon2);
                        if (string.IsNullOrEmpty(evolutionMessage))
                            evolutionMessage = $"\n\n✨ **雖然快4了，但 {oldName} 進化成 {loserPokemon2.Name} 了！** ✨";
                        else
                            evolutionMessage += $"\n✨ **{oldName} 也進化成 {loserPokemon2.Name} 了！** ✨";

                        // 更新玩家資料中的pokemon
                        var pokemonInList = loser.CaughtPokemon.FirstOrDefault(p => p.Id == preId2 && p.CaughtDate == loserPokemon2.CaughtDate);
                        if (pokemonInList != null)
                        {
                            var index = loser.CaughtPokemon.IndexOf(pokemonInList);
                            loser.CaughtPokemon[index] = loserPokemon2;
                        }
                    }

                    loser.CaughtPokemon = loser.CaughtPokemon.Select(p =>
                    {
                        if (p.Id == preId1)
                            return loserPokemon1;
                        if (p.Id == preId2)
                            return loserPokemon2;
                        return p;
                    }).ToList();

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
                        $"{winnerPokemon1.CustomName ?? winnerPokemon1.Name}\n" +
                        $"和他的戰友 {winnerPokemon2.CustomName ?? winnerPokemon2.Name}\n" +
                        $"戰績: {winnerStats.Wins}勝 {winnerStats.Losses}敗", true);
                }
                else
                {
                    embedBuilder.AddField($"🏆 勝者: {winnerName}",
                        $"{winnerPokemon1.CustomName ?? winnerPokemon1.Name}\n" +
                        $"和他的戰友 {winnerPokemon2.CustomName ?? winnerPokemon2.Name}\n" +
                        $"(電腦對手)", true);
                }

                // 敗者資訊
                if (loserStats != null)
                {
                    embedBuilder.AddField($"😢 敗者: {loserName}",
                        $"{loserPokemon1.CustomName ?? loserPokemon1.Name}\n" +
                        $"和他的盧蛇好朋友 {loserPokemon2.CustomName ?? loserPokemon2.Name}\n" +
                        $"戰績: {loserStats.Wins}勝 {loserStats.Losses}敗", true);
                }
                else
                {
                    embedBuilder.AddField($"😢 敗者: {loserName}",
                        $"{loserPokemon1.CustomName ?? loserPokemon1.Name}\n" +
                        $"和他的盧蛇好朋友 {loserPokemon2.CustomName ?? loserPokemon2.Name}\n" +
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
   - 是否為閃光: {(pokemon1.isShiny ? "是" : "否")}

2. {player2Name} 的 自訂名稱:{pokemon2.CustomName ?? pokemon2.Name}，真實名稱:{pokemon2.Name}
   - 屬性: {string.Join(", ", pokemon2.Types)}
   - HP: {pokemon2.HP}, 攻擊: {pokemon2.Attack}, 防禦: {pokemon2.Defense}
   - 特攻: {pokemon2.SpecialAttack}, 特防: {pokemon2.SpecialDefense}, 速度: {pokemon2.Speed}
   - 是否為閃光: {(pokemon2.isShiny ? "是" : "否")}

請根據以上數據和屬性相剋關係，判斷誰會獲勝，並用繁體中文描述一段精彩的對戰過程。
要以該pokemon真實的技能來敘述，期間有自訂名稱的話就要叫自訂名稱，沒有的話就叫真實名稱。
如果是閃光的，對話中要提到閃光的特效，但閃光完全不影響戰鬥結果。
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

                    // 更新勝利者pokemon的進化點數 (+2)
                    winnerPokemon.EvolutionPoints += 2;

                    int preId = winnerPokemon.Id;
                    // 檢查是否達到進化條件（3點）
                    if (winnerPokemon.CanEvolve && winnerPokemon.EvolutionPoints >= 3)
                    {
                        var oldName = winnerPokemon.Name;
                        winnerPokemon = await EvolvePokemonAsync(winnerPokemon);
                        evolutionMessage = $"\n\n✨ **恭喜！{oldName} 進化成 {winnerPokemon.Name} 了！** ✨";

                        // 更新玩家資料中的pokemon
                        var pokemonInList = winner.CaughtPokemon.FirstOrDefault(p => p.Id == pokemon1.Id && p.CaughtDate == pokemon1.CaughtDate);
                        if (pokemonInList != null)
                        {
                            var index = winner.CaughtPokemon.IndexOf(pokemonInList);
                            winner.CaughtPokemon[index] = winnerPokemon;
                        }
                    }

                    winner.CaughtPokemon = winner.CaughtPokemon.Select(p =>
                    {
                        if (p.Id == preId)
                            return winnerPokemon;
                        return p;
                    }).ToList();

                    await SavePlayerDataAsync(winner);
                }

                if (loserId != 0)
                {
                    var loser = await GetPlayerDataAsync(loserId, loserName);
                    loser.TotalBattles++;
                    loser.Losses++;

                    // 更新失敗者pokemon的進化點數 (+1)
                    loserPokemon.EvolutionPoints += 1;

                    int preId = loserPokemon.Id;
                    // 檢查是否達到進化條件（3點）
                    if (loserPokemon.CanEvolve && loserPokemon.EvolutionPoints >= 3)
                    {
                        var oldName = loserPokemon.Name;
                        loserPokemon = await EvolvePokemonAsync(loserPokemon);
                        if (string.IsNullOrEmpty(evolutionMessage))
                            evolutionMessage = $"\n\n✨ **雖然快4了，但 {oldName} 進化成 {loserPokemon.Name} 了！** ✨";
                        else
                            evolutionMessage += $"\n✨ **{oldName} 也進化成 {loserPokemon.Name} 了！** ✨";

                        // 更新玩家資料中的pokemon
                        var pokemonInList = loser.CaughtPokemon.FirstOrDefault(p => p.Id == pokemon2.Id && p.CaughtDate == pokemon2.CaughtDate);
                        if (pokemonInList != null)
                        {
                            var index = loser.CaughtPokemon.IndexOf(pokemonInList);
                            loser.CaughtPokemon[index] = loserPokemon;
                        }
                    }

                    loser.CaughtPokemon = loser.CaughtPokemon.Select(p =>
                    {
                        if (p.Id == preId)
                            return loserPokemon;
                        return p;
                    }).ToList();

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

        private async Task AddToMatchmakingAsync(ulong userId, string userName, PokeGamePokemon pokemon, ulong channelId)
        {
            var matchmaking = new BattleMatchmaking
            {
                UserId = userId,
                UserName = userName,
                Pokemon = pokemon,
                ChannelId = channelId,
                SearchStartTime = DateTime.UtcNow
            };

            if (_useRedis)
            {
                try
                {
                    var data = JsonConvert.SerializeObject(matchmaking);
                    await _redisDb.HashSetAsync(MATCHMAKING_KEY, userId.ToString(), data);

                    // 設定 1440 分鐘過期 (24 小時)
                    await _redisDb.KeyExpireAsync(MATCHMAKING_KEY, TimeSpan.FromMinutes(1440));
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
                            // 只返回 1440 分鐘內的搜尋 (24 小時)
                            if ((DateTime.UtcNow - matchmaking.SearchStartTime).TotalMinutes < 1440)
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
                        if ((DateTime.UtcNow - kvp.Value.SearchStartTime).TotalMinutes < 1440)
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
                    if ((DateTime.UtcNow - kvp.Value.SearchStartTime).TotalMinutes < 1440)
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

        private async Task AddToMatchmaking2V2Async(ulong userId, string userName, PokeGamePokemon pokemon1, PokeGamePokemon pokemon2,ulong channel)
        {
            var matchmaking = new BattleMatchmaking2V2
            {
                UserId = userId,
                UserName = userName,
                Pokemon1 = pokemon1,
                Pokemon2 = pokemon2,
                ChannelId = channel,
                SearchStartTime = DateTime.UtcNow
            };

            if (_useRedis)
            {
                try
                {
                    var data = JsonConvert.SerializeObject(matchmaking);
                    await _redisDb.HashSetAsync(MATCHMAKING_KEY_2V2, userId.ToString(), data);

                    // 設定 1440 分鐘過期 (24 小時)
                    await _redisDb.KeyExpireAsync(MATCHMAKING_KEY_2V2, TimeSpan.FromMinutes(1440));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Redis 配對寫入失敗，切換到記憶體儲存: {ex.Message}");
                    // Redis 失敗時降級到記憶體儲存
                    _memoryMatchmaking2V2[userId] = matchmaking;
                }
            }
            else
            {
                // 使用記憶體儲存
                _memoryMatchmaking2V2[userId] = matchmaking;
                await Task.CompletedTask;
            }
        }

        private async Task<List<BattleMatchmaking2V2>> GetWaitingPlayers2V2Async()
        {
            var result = new List<BattleMatchmaking2V2>();

            if (_useRedis)
            {
                try
                {
                    var entries = await _redisDb.HashGetAllAsync(MATCHMAKING_KEY_2V2);

                    foreach (var entry in entries)
                    {
                        try
                        {
                            var matchmaking = JsonConvert.DeserializeObject<BattleMatchmaking2V2>(entry.Value);
                            // 只返回 1440 分鐘內的搜尋 (24 小時)
                            if ((DateTime.UtcNow - matchmaking.SearchStartTime).TotalMinutes < 1440)
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
                    foreach (var kvp in _memoryMatchmaking2V2)
                    {
                        if ((DateTime.UtcNow - kvp.Value.SearchStartTime).TotalMinutes < 1440)
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
                        _memoryMatchmaking2V2.Remove(key);
                    }
                }
            }
            else
            {
                // 使用記憶體儲存
                var expiredKeys = new List<ulong>();
                foreach (var kvp in _memoryMatchmaking2V2)
                {
                    if ((DateTime.UtcNow - kvp.Value.SearchStartTime).TotalMinutes < 1440)
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
                    _memoryMatchmaking2V2.Remove(key);
                }

                await Task.CompletedTask;
            }

            return result;
        }

        private async Task RemoveFromMatchmaking2V2Async(ulong userId)
        {
            if (_useRedis)
            {
                try
                {
                    await _redisDb.HashDeleteAsync(MATCHMAKING_KEY_2V2, userId.ToString());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Redis 配對刪除失敗，切換到記憶體儲存: {ex.Message}");
                    // Redis 失敗時降級到記憶體儲存
                    _memoryMatchmaking2V2.Remove(userId);
                }
            }
            else
            {
                // 使用記憶體儲存
                _memoryMatchmaking2V2.Remove(userId);
                await Task.CompletedTask;
            }
        }

        #region 團戰功能
        public async Task<(Embed embed, ComponentBuilder component)> JoinOrCreateTeamFightAsync(ulong userId, string userName, int pokemonIndex, ulong channelId)
        {
            try
            {
                // 獲取玩家資料
                var player = await GetPlayerDataAsync(userId, userName);
                if (player.CaughtPokemon.Count == 0)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 你還沒有 Pokemon")
                        .WithDescription("請先使用 `/抓pokemon` 來獲得你的第一隻 Pokemon！")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }

                if (pokemonIndex < 0 || pokemonIndex >= player.CaughtPokemon.Count)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ Pokemon 編號錯誤")
                        .WithDescription($"請選擇 0 到 {player.CaughtPokemon.Count - 1} 之間的 Pokemon！")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }

                var selectedPokemon = player.CaughtPokemon[pokemonIndex];

                // 檢查是否已有等待中的團戰
                var currentBoss = await GetCurrentTeamFightBossAsync();

                if (currentBoss == null || !currentBoss.IsActive)
                {
                    // 沒有團戰，創建新的
                    var bossPokemon = await GetRandomLegendaryPokemonAsync();
                    if (bossPokemon == null)
                    {
                        var errorEmbed = new EmbedBuilder()
                            .WithTitle("❌ 無法生成團戰 Boss")
                            .WithDescription("傳說/神話 Pokemon 列表尚未載入完成，請稍後再試。")
                            .WithColor(Color.Red)
                            .Build();
                        return (errorEmbed, new ComponentBuilder());
                    }

                    // 提升 Boss 的數值
                    bossPokemon.HP = (int)(bossPokemon.HP * 5);
                    bossPokemon.Attack = (int)(bossPokemon.Attack * 2);
                    bossPokemon.Defense = (int)(bossPokemon.Defense * 2);
                    bossPokemon.SpecialAttack = (int)(bossPokemon.SpecialAttack * 1.5);
                    bossPokemon.SpecialDefense = (int)(bossPokemon.SpecialDefense * 1.5);
                    bossPokemon.Speed = (int)(bossPokemon.Speed * 1.5);

                    currentBoss = new TeamFightBoss
                    {
                        BossPokemon = bossPokemon,
                        CurrentHP = bossPokemon.HP,
                        MaxHP = bossPokemon.HP,
                        Participants = new List<TeamFightParticipant>(),
                        StartTime = DateTime.UtcNow,
                        ChannelId = channelId,
                        IsActive = true
                    };
                }

                // 檢查團戰是否已超時（30 分鐘）
                if ((DateTime.UtcNow - currentBoss.StartTime).TotalMinutes > 30)
                {
                    currentBoss.IsActive = false;
                    await SaveTeamFightBossAsync(currentBoss);

                    var timeoutEmbed = new EmbedBuilder()
                        .WithTitle("❌ 上一場團戰已過期")
                        .WithDescription("正在創建新的團戰...")
                        .WithColor(Color.Red)
                        .Build();

                    // 遞迴呼叫創建新團戰
                    return await JoinOrCreateTeamFightAsync(userId, userName, pokemonIndex, channelId);
                }

                // 檢查玩家是否已參與
                if (currentBoss.Participants.Any(p => p.UserId == userId))
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 你已經參與了這場團戰")
                        .WithDescription("等待其他玩家加入，或使用 `/開始傳說pokemon團戰` 開始戰鬥！")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }

                // 加入參與者
                var participant = new TeamFightParticipant
                {
                    UserId = userId,
                    UserName = userName,
                    Pokemon = selectedPokemon,
                    DamageDealt = 0,
                    JoinTime = DateTime.UtcNow
                };

                currentBoss.Participants.Add(participant);
                await SaveTeamFightBossAsync(currentBoss);

                var embed = new EmbedBuilder()
                    .WithTitle("✅ 成功加入團戰！")
                    .WithDescription($"{userName} 的 **{selectedPokemon.CustomName ?? selectedPokemon.Name}** 已準備好戰鬥！")
                    .WithThumbnailUrl(currentBoss.BossPokemon.ImageUrl)
                    .WithImageUrl(selectedPokemon.ImageUrl)
                    .WithColor(Color.Green)
                    .AddField("Boss", $"{currentBoss.BossPokemon.Name}")
                    .AddField("屬性", string.Join(", ", currentBoss.BossPokemon.Types), true)
                    .AddField("目前參與人數", currentBoss.Participants.Count, true)
                    .AddField("參與者", string.Join("\n", currentBoss.Participants.Select(p => 
                        $"{p.UserName} - {p.Pokemon.CustomName ?? p.Pokemon.Name}")))
                    .WithFooter("等待更多訓練師加入，或使用 `/開始傳說pokemon團戰` 開始戰鬥！")
                    .WithCurrentTimestamp()
                    .Build();

                return (embed, new ComponentBuilder());
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"加入團戰時發生錯誤: {ex.Message}").Item2, new ComponentBuilder());
            }
        }

        public async Task<(Embed embed, ComponentBuilder component)> StartTeamFightBattleAsync(IMessageChannel channel)
        {
            try
            {
                // 檢查是否有等待中的團戰
                var currentBoss = await GetCurrentTeamFightBossAsync();
                if (currentBoss == null || !currentBoss.IsActive)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 目前沒有等待中的團戰")
                        .WithDescription("請先使用 `/參與或開啟團戰` 來創建或加入團戰！")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }

                // 檢查參與人數
                if (currentBoss.Participants.Count == 0)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ 沒有參與者")
                        .WithDescription("需要至少一位訓練師參與戰鬥！")
                        .WithColor(Color.Red)
                        .Build();
                    return (errorEmbed, new ComponentBuilder());
                }

                //對戰前先丟一次Boss 和玩家 pokemon圖片

                if (!string.IsNullOrEmpty(currentBoss.BossPokemon.Front_GIF ?? currentBoss.BossPokemon.ImageUrl))
                {
                    await channel.SendMessageAsync(currentBoss.BossPokemon.Front_GIF ?? currentBoss.BossPokemon.ImageUrl);
                }
                await channel.SendMessageAsync("==========對上==========");

                foreach(var participant in currentBoss.Participants)
                {
                    if (!string.IsNullOrEmpty(participant.Pokemon.Back_GIF ?? participant.Pokemon.Back_ImageUrl ?? participant.Pokemon.ImageUrl))
                    {
                        await channel.SendMessageAsync(participant.Pokemon.Back_GIF ?? participant.Pokemon.Back_ImageUrl ?? participant.Pokemon.ImageUrl);
                    }
                }

                // 準備 AI 判斷的戰鬥資訊
                var participantsInfo = string.Join("\n", currentBoss.Participants.Select((p, index) => 
                    $"{index + 1}. {p.UserName} 的 {p.Pokemon.CustomName ?? p.Pokemon.Name} (真實名稱: {p.Pokemon.Name})" +
                    $"\n   - 屬性: {string.Join(", ", p.Pokemon.Types)}" +
                    $"\n   - 能力: HP:{p.Pokemon.HP}, 攻擊:{p.Pokemon.Attack}, 防禦:{p.Pokemon.Defense}, 特攻:{p.Pokemon.SpecialAttack}, 特防:{p.Pokemon.SpecialDefense}, 速度:{p.Pokemon.Speed}" +
                    $"\n   - 是否為閃光: {(p.Pokemon.isShiny ? "是" : "否")}"));

                string battlePrompt = $@"請模擬一場精彩的傳說級 Pokemon 團戰，並判斷勝負。

Boss Pokemon：
名稱: {currentBoss.BossPokemon.Name}
屬性: {string.Join(", ", currentBoss.BossPokemon.Types)}
能力: HP:{currentBoss.BossPokemon.HP}, 攻擊:{currentBoss.BossPokemon.Attack}, 防禦:{currentBoss.BossPokemon.Defense}, 特攻:{currentBoss.BossPokemon.SpecialAttack}, 特防:{currentBoss.BossPokemon.SpecialDefense}, 速度:{currentBoss.BossPokemon.Speed}

挑戰者們：
{participantsInfo}

請根據以上數據和屬性相剋關係，判斷這場團戰的勝負。
要求：
1. 描述一段精彩的團戰過程（繁體中文）
2. 要提到每個參與者的 Pokemon 的表現和使用的真實技能
3. 有自訂名稱的就叫自訂名稱，沒有的就叫真實名稱
4. 如果有閃光的 Pokemon，要提到閃光特效（但不影響戰鬥結果）
5. 最後明確說明結果，格式為「勝者：[訓練師們/Boss]」
6. 一定要公平，判斷誰會贏就誰會贏";

                // 呼叫 AI 判斷對戰結果
                var aiResponse = await _aiService.GenerateSimpleTextAsync(battlePrompt);

                // 解析 AI 回應，判斷勝者
                bool trainersWin = aiResponse.Contains("勝者：訓練師") || aiResponse.Contains("勝者：挑戰者") || 
                                   (!aiResponse.Contains("勝者：Boss") && !aiResponse.Contains("勝者：" + currentBoss.BossPokemon.Name));

                currentBoss.IsActive = false;
                await SaveTeamFightBossAsync(currentBoss);

                if (trainersWin)
                {
                    // 訓練師們獲勝，給所有參與者獎勵
                    foreach (var p in currentBoss.Participants)
                    {
                        var participantPlayer = await GetPlayerDataAsync(p.UserId, p.UserName);
                        participantPlayer.LastCatchDate = null; // 給一次抓寶機會
                        participantPlayer.Wins++; // 增加勝場
                        await SavePlayerDataAsync(participantPlayer);
                    }

                    var victoryEmbed = new EmbedBuilder()
                        .WithTitle("🎉 男同幫們獲勝了🎉")
                        .WithDescription(aiResponse)
                        .WithThumbnailUrl(currentBoss.BossPokemon.ImageUrl)
                        .WithColor(Color.Gold)
                        .AddField("參與者", string.Join("\n", currentBoss.Participants.Select(p => 
                            $"{p.UserName} - {p.Pokemon.CustomName ?? p.Pokemon.Name}")))
                        .AddField("🎁 獎勵", "所有參與者獲得：\n✅ 一次額外抓 Pokemon 的機會\n✅ 勝場 +1")
                        .WithCurrentTimestamp()
                        .Build();

                    return (victoryEmbed, new ComponentBuilder());
                }
                else
                {
                    // Boss 獲勝
                    foreach (var p in currentBoss.Participants)
                    {
                        var participantPlayer = await GetPlayerDataAsync(p.UserId, p.UserName);
                        participantPlayer.Losses++; // 增加敗場
                        await SavePlayerDataAsync(participantPlayer);
                    }

                    var defeatEmbed = new EmbedBuilder()
                        .WithTitle($"😢 {currentBoss.BossPokemon.Name} 太強大了...而且還沒有使出全力的樣子，就算沒有飛葉快刀也會贏，我甚至覺得有些對不起他")
                        .WithDescription(aiResponse)
                        .WithThumbnailUrl(currentBoss.BossPokemon.ImageUrl)
                        .WithColor(Color.Red)
                        .AddField("參與者", string.Join("\n", currentBoss.Participants.Select(p => 
                            $"{p.UserName} - {p.Pokemon.CustomName ?? p.Pokemon.Name}")))
                        .AddField("💔 結果", "所有參與者敗場 +1\n 慘遭2.5")
                        .WithCurrentTimestamp()
                        .Build();

                    return (defeatEmbed, new ComponentBuilder());
                }
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"開始團戰時發生錯誤: {ex.Message}").Item2, new ComponentBuilder());
            }
        }

        private async Task<PokeGamePokemon> GetRandomLegendaryPokemonAsync()
        {
            try
            {
                List<int> legendaryIds;

                // 從 Redis 或記憶體中獲取傳說 Pokemon ID 列表
                if (_useRedis)
                {
                    var data = await _redisDb.StringGetAsync(LEGENDARY_POKEMON_KEY);
                    if (data.IsNullOrEmpty)
                        return null;

                    legendaryIds = JsonConvert.DeserializeObject<List<int>>(data);
                }
                else
                {
                    legendaryIds = _memoryLegendaryPokemonIds;
                }

                if (legendaryIds == null || legendaryIds.Count == 0)
                    return null;

                // 隨機選一隻
                Random random = new Random();
                int randomId = legendaryIds[random.Next(legendaryIds.Count)];

                // 獲取 Pokemon 資料
                var response = await _httpClient.GetAsync($"{API_BASE_URL}pokemon/{randomId}");
                if (!response.IsSuccessStatusCode)
                    return null;

                var content = await response.Content.ReadAsStringAsync();
                var pokeData = JsonConvert.DeserializeObject<Pokemon>(content);

                // 獲取中文名稱
                var speciesResponse = await _httpClient.GetAsync(pokeData.species.url);
                var speciesContent = await speciesResponse.Content.ReadAsStringAsync();
                var speciesData = JsonConvert.DeserializeObject<PokeSpecies>(speciesContent);

                var chineseName = speciesData.names.FirstOrDefault(n => n.language.name == "zh-hant")?.name
                    ?? pokeData.species.name;

                // Boss 不會是閃光
                var pokemon = new PokeGamePokemon
                {
                    Id = pokeData.id,
                    Name = chineseName,
                    CustomName = null,
                    ImageUrl = pokeData.sprites.other.official_artwork.front_default ?? pokeData.sprites.front_default,
                    Back_ImageUrl = pokeData.sprites.back_default,
                    HP = pokeData.stats.FirstOrDefault(s => s.stat.name == "hp")?.base_stat ?? 0,
                    Attack = pokeData.stats.FirstOrDefault(s => s.stat.name == "attack")?.base_stat ?? 0,
                    Defense = pokeData.stats.FirstOrDefault(s => s.stat.name == "defense")?.base_stat ?? 0,
                    SpecialAttack = pokeData.stats.FirstOrDefault(s => s.stat.name == "special-attack")?.base_stat ?? 0,
                    SpecialDefense = pokeData.stats.FirstOrDefault(s => s.stat.name == "special-defense")?.base_stat ?? 0,
                    Speed = pokeData.stats.FirstOrDefault(s => s.stat.name == "speed")?.base_stat ?? 0,
                    Types = pokeData.types.Select(t => t.type.name).ToList(),
                    CaughtDate = DateTime.UtcNow,
                    isShiny = false,
                    EvolutionPoints = 0,
                    EvolutionStage = 0,
                    CanEvolve = false,
                    NextEvolutionId = null,
                    Front_GIF = pokeData.sprites.other.showdown.front_default,
                    Back_GIF = pokeData.sprites.other.showdown.back_default
                };

                return pokemon;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"獲取隨機傳說 Pokemon 時發生錯誤: {ex.Message}");
                return null;
            }
        }

        private async Task<TeamFightBoss> GetCurrentTeamFightBossAsync()
        {
            if (_useRedis)
            {
                try
                {
                    var data = await _redisDb.StringGetAsync(TEAM_FIGHT_BOSS_KEY);
                    if (data.IsNullOrEmpty)
                        return null;

                    return JsonConvert.DeserializeObject<TeamFightBoss>(data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Redis 讀取團戰 Boss 失敗，切換到記憶體儲存: {ex.Message}");
                    return _memoryTeamFightBoss;
                }
            }
            else
            {
                return await Task.FromResult(_memoryTeamFightBoss);
            }
        }

        private async Task SaveTeamFightBossAsync(TeamFightBoss boss)
        {
            if (_useRedis)
            {
                try
                {
                    var data = JsonConvert.SerializeObject(boss);
                    await _redisDb.StringSetAsync(TEAM_FIGHT_BOSS_KEY, data);

                    // 設定 30 分鐘過期
                    await _redisDb.KeyExpireAsync(TEAM_FIGHT_BOSS_KEY, TimeSpan.FromMinutes(30));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Redis 儲存團戰 Boss 失敗，切換到記憶體儲存: {ex.Message}");
                    _memoryTeamFightBoss = boss;
                }
            }
            else
            {
                _memoryTeamFightBoss = boss;
                await Task.CompletedTask;
            }
        }
        #endregion
        #endregion
    }
}
