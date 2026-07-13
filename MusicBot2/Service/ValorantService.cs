using Discord;
using Discord.WebSocket;
using MusicBot2.Helpers;
using MusicBot2.Models;
using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    //https://valorant-api.com/v1/agents?language=zh-TW
    public class ValorantService
    {
        private readonly HttpClient _httpClient;
        private const string API_BASE_URL = "https://valorant-api.com/v1/agents?language=zh-TW";

        public ValorantService()
        {
            _httpClient = new HttpClient();
        }

        #region 猜角色圖片
        // 儲存遊戲狀態
        private readonly Dictionary<ulong, ValorantGameSession> _activeImageGames = new Dictionary<ulong, ValorantGameSession>();
        private readonly Dictionary<ulong, ValorantGameSession> _activeAbilityGames = new Dictionary<ulong, ValorantGameSession>();

        // 中英文名稱映射（從 API 動態獲取）
        private Dictionary<string, string> _nameMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 開始猜 Valorant 角色圖片遊戲
        /// </summary>
        public async Task<((ComponentBuilder component, Embed embed), Stream silhouette)> StartGuessAgentImageAsync(ulong userId)
        {
            try
            {
                var response = await _httpClient.GetAsync(API_BASE_URL);

                if (!response.IsSuccessStatusCode)
                    return (CommonHelper.BuildErrorResponse("無法獲取 Valorant 角色資料"), null);

                var responseContent = await response.Content.ReadAsStringAsync();
                var agentsResponse = JsonConvert.DeserializeObject<ValorantAgentsResponse>(responseContent);

                // 過濾出可玩角色
                var playableAgents = agentsResponse.data
                    .Where(a => a.isPlayableCharacter && !string.IsNullOrEmpty(a.fullPortrait))
                    .ToList();

                if (playableAgents.Count == 0)
                    return (CommonHelper.BuildErrorResponse("找不到可用的角色資料"), null);

                Random random = new Random();

                // 隨機選擇正確答案
                var correctAgent = playableAgents[random.Next(playableAgents.Count)];

                // 儲存遊戲狀態
                _activeImageGames[userId] = new ValorantGameSession
                {
                    CorrectAgent = correctAgent,
                    IsImageMode = true
                };

                // 塗黑圖片（保留白色部分）
                var silhouette = await MakeBlackSilhouette(correctAgent.fullPortrait);

                // 不顯示按鈕，提示使用 slash command
                var componentBuilder = new ComponentBuilder();

                // 建立 Embed
                var embedBuilder = new EmbedBuilder()
                    .WithTitle("🎮 各位瓦學弟們 猜猜這是哪個角色？")
                    .WithDescription("我是誰?\n\n請使用指令回答：`/回答valorant角色 答案:[角色名稱]`\n中文英文都可以！")
                    .WithColor(Discord.Color.Red)
                    .WithImageUrl("attachment://silhouette.png")
                    .WithFooter($"遊戲ID: {userId}")
                    .WithCurrentTimestamp();

                return ((componentBuilder, embedBuilder.Build()), silhouette);
            }
            catch (Exception ex)
            {
                return (CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}"), null);
            }
        }

        /// <summary>
        /// 將圖片非白色部分塗黑
        /// </summary>
        private async Task<Stream> MakeBlackSilhouette(string imageUrl)
        {
            var bytes = await _httpClient.GetByteArrayAsync(imageUrl);
            using var bitmap = SKBitmap.Decode(bytes);
            using var result = new SKBitmap(bitmap.Width, bitmap.Height);

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);

                    // 如果是透明或接近白色，保留白色背景
                    if (pixel.Alpha < 25 || IsNearWhite(pixel))
                        result.SetPixel(x, y, SKColors.White);
                    else
                        result.SetPixel(x, y, SKColors.Black);  // 其他部分塗黑
                }
            }

            using var image = SKImage.FromBitmap(result);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            var output = new MemoryStream();
            data.SaveTo(output);
            output.Position = 0;
            return output;
        }

        /// <summary>
        /// 判斷像素是否接近白色
        /// </summary>
        private bool IsNearWhite(SKColor color)
        {
            // 如果 RGB 值都大於 250，視為白色
            return color.Red > 250 && color.Green > 250 && color.Blue > 250;
        }
        #endregion

        #region 猜角色技能
        /// <summary>
        /// 開始猜 Valorant 技能名稱遊戲（所有人都可以猜）
        /// </summary>
        public async Task<(ComponentBuilder component, Embed embed)> StartGuessAbilityNameAsync(ulong channelId)
        {
            try
            {
                var response = await _httpClient.GetAsync(API_BASE_URL);

                if (!response.IsSuccessStatusCode)
                    return CommonHelper.BuildErrorResponse("無法獲取 Valorant 角色資料");

                var responseContent = await response.Content.ReadAsStringAsync();
                var agentsResponse = JsonConvert.DeserializeObject<ValorantAgentsResponse>(responseContent);

                // 過濾出可玩角色且有技能的
                var playableAgents = agentsResponse.data
                    .Where(a => a.isPlayableCharacter && a.abilities != null && a.abilities.Any())
                    .ToList();

                if (playableAgents.Count == 0)
                    return CommonHelper.BuildErrorResponse("找不到可用的角色資料");

                Random random = new Random();

                // 隨機選擇正確答案
                var correctAgent = playableAgents[random.Next(playableAgents.Count)];

                // 隨機選擇該角色的一個技能（排除 Passive）
                var validAbilities = correctAgent.abilities
                    .Where(a => !string.IsNullOrEmpty(a.displayIcon) && a.slot != "Passive")
                    .ToList();

                if (validAbilities.Count == 0)
                    return CommonHelper.BuildErrorResponse("該角色沒有可用的技能圖示");

                var randomAbility = validAbilities[random.Next(validAbilities.Count)];

                // 儲存遊戲狀態（使用 channelId 而不是 userId，讓所有人都能猜）
                _activeAbilityGames[channelId] = new ValorantGameSession
                {
                    CorrectAgent = correctAgent,
                    SelectedAbility = randomAbility,
                    IsImageMode = false
                };

                // 不顯示按鈕
                var componentBuilder = new ComponentBuilder();

                // 建立 Embed
                var embedBuilder = new EmbedBuilder()
                    .WithTitle("🎮 猜猜這個技能叫什麼名字？")
                    .WithDescription($"**角色：** {correctAgent.displayName}\n**技能類型：** {randomAbility.slot}\n\n{randomAbility.description}\n\n請使用指令回答：`/回答valorant技能 答案:[技能名稱]`\n中文英文都可以！")
                    .WithColor(Discord.Color.Red)
                    .WithThumbnailUrl(randomAbility.displayIcon)
                    .WithFooter($"頻道ID: {channelId}")
                    .WithCurrentTimestamp();

                return (componentBuilder, embedBuilder.Build());
            }
            catch (Exception ex)
            {
                return CommonHelper.BuildErrorResponse($"發生錯誤: {ex.Message}");
            }
        }
        #endregion

        #region 共用
        /// <summary>
        /// 處理角色猜謎答案（Slash Command）
        /// </summary>
        public async Task<(Embed embed, bool isCorrect, string correctAnswer)> HandleAgentAnswerAsync(ulong userId, string userAnswer)
        {
            // 檢查是否有遊戲進行中
            if (!_activeImageGames.ContainsKey(userId))
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("⚠️ 提示")
                    .WithColor(Discord.Color.Orange)
                    .WithDescription("找不到你的遊戲！請先使用 `/猜猜我是誰瓦學弟ver` 開始遊戲。")
                    .Build();
                return (errorEmbed, false, "");
            }

            var session = _activeImageGames[userId];
            var correctAgent = session.CorrectAgent;

            // 檢查答案（支援中英文，不區分大小寫）
            bool isCorrect = string.Equals(userAnswer.Trim(), correctAgent.displayName, StringComparison.OrdinalIgnoreCase);

            if (isCorrect)
            {
                // 答對了 - 移除遊戲狀態
                _activeImageGames.Remove(userId);

                var successEmbed = new EmbedBuilder()
                    .WithTitle("🎉 答對了！")
                    .WithColor(Discord.Color.Green)
                    .WithDescription($"正確答案就是 **{correctAgent.displayName}**！")
                    .AddField("角色定位", correctAgent.role?.displayName ?? "未知", inline: true)
                    .WithImageUrl(correctAgent.fullPortrait)
                    .WithTimestamp(DateTimeOffset.Now)
                    .Build();

                return (successEmbed, true, correctAgent.displayName);
            }
            else
            {
                // 答錯了 - 遊戲繼續
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ 答錯了！")
                    .WithColor(Discord.Color.Red)
                    .WithDescription($"你的答案：**{userAnswer}**\n\n請再試一次！使用 `/回答valorant角色 答案:[角色名稱]`")
                    .Build();

                return (errorEmbed, false, "");
            }
        }

        /// <summary>
        /// 處理角色圖片答案（slash command）
        /// </summary>
        public async Task<(bool isCorrect, Embed embed, string? correctName)> HandleAgentAnswerAsync(ulong userId, string userAnswer, SocketGuildUser user, ISocketMessageChannel channel)
        {
            // 檢查是否有遊戲進行中
            if (!_activeImageGames.ContainsKey(userId))
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("⚠️ 提示")
                    .WithColor(Discord.Color.Orange)
                    .WithDescription("找不到遊戲，請先使用 `/猜猜我是誰瓦學弟ver` 開始遊戲！")
                    .Build();
                return (false, errorEmbed, null);
            }

            var session = _activeImageGames[userId];
            userAnswer = userAnswer.Trim();

            // 檢查答案（支援中英文，不區分大小寫）
            bool isCorrect = await CheckAgentAnswerAsync(session.CorrectAgent, userAnswer);

            if (isCorrect)
            {
                // 答對了
                _activeImageGames.Remove(userId);

                var rewardText = await RewardsHelpers.GetRandomRewards(channel, user);

                var embedBuilder = new EmbedBuilder()
                    .WithTitle("🎉 答對了！")
                    .WithColor(Discord.Color.Green)
                    .WithDescription($"正確答案就是 **{session.CorrectAgent.displayName}**！")
                    .AddField("角色定位", session.CorrectAgent.role?.displayName ?? "未知", inline: true)
                    .AddField("恭喜", $"Valorant 大師 **{user.Mention}** {CommonHelper.GetUserFace(user.Id.ToString())}", inline: true)
                    .AddField("獎勵", rewardText)
                    .WithImageUrl(session.CorrectAgent.fullPortrait)
                    .WithTimestamp(DateTimeOffset.Now);

                return (true, embedBuilder.Build(), session.CorrectAgent.displayName);
            }
            else
            {
                // 答錯了
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ 答錯了！")
                    .WithColor(Discord.Color.Red)
                    .WithDescription($"{user.Mention} {CommonHelper.GetUserFace(user.Id.ToString())} 答：**{userAnswer}**\n\n請再試一次！")
                    .Build();
                return (false, errorEmbed, null);
            }
        }

        /// <summary>
        /// 處理技能名稱答案（slash command）
        /// </summary>
        public async Task<(bool isCorrect, string? wrongAnswer, string? correctAnswer, string? agentName)> HandleAbilityAnswerAsync(ulong channelId, string userAnswer)
        {
            // 檢查是否有遊戲進行中
            if (!_activeAbilityGames.ContainsKey(channelId))
            {
                return (false, null, null, null);
            }

            var session = _activeAbilityGames[channelId];
            userAnswer = userAnswer.Trim();

            // 檢查答案（支援中英文，不區分大小寫）
            bool isCorrect = await CheckAbilityAnswerAsync(session.SelectedAbility, userAnswer);

            if (isCorrect)
            {
                // 答對了 - 移除遊戲狀態
                _activeAbilityGames.Remove(channelId);
                return (true, null, session.SelectedAbility.displayName, session.CorrectAgent.displayName);
            }
            else
            {
                // 答錯了 - 遊戲繼續
                return (false, userAnswer, null, null);
            }
        }

        /// <summary>
        /// 取得遊戲狀態（用於儲存訊息ID）
        /// </summary>
        public void SetMessageId(ulong id, ulong messageId, bool isImageMode)
        {
            if (isImageMode && _activeImageGames.ContainsKey(id))
            {
                _activeImageGames[id].MessageId = messageId;
            }
            else if (!isImageMode && _activeAbilityGames.ContainsKey(id))
            {
                _activeAbilityGames[id].MessageId = messageId;
            }
        }

        /// <summary>
        /// 檢查角色答案（支援中英文）
        /// </summary>
        private async Task<bool> CheckAgentAnswerAsync(ValorantAgent agent, string userAnswer)
        {
            // 先確保有最新的 API 資料（如果 _nameMapping 為空）
            if (_nameMapping.Count == 0)
            {
                await LoadNameMappingAsync();
            }

            // 直接比對中文名（API displayName）
            if (string.Equals(userAnswer, agent.displayName, StringComparison.OrdinalIgnoreCase))
                return true;

            // 嘗試從映射表找英文名
            if (_nameMapping.TryGetValue(agent.displayName, out string? englishName))
            {
                if (string.Equals(userAnswer, englishName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 檢查技能答案（支援中英文）
        /// </summary>
        private async Task<bool> CheckAbilityAnswerAsync(ValorantAbility ability, string userAnswer)
        {
            // 先確保有最新的 API 資料
            if (_nameMapping.Count == 0)
            {
                await LoadNameMappingAsync();
            }

            // 直接比對中文名
            if (string.Equals(userAnswer, ability.displayName, StringComparison.OrdinalIgnoreCase))
                return true;

            // 嘗試從映射表找英文名
            if (_nameMapping.TryGetValue(ability.displayName, out string? englishName))
            {
                if (string.Equals(userAnswer, englishName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 載入中英文名稱映射（從 API 動態獲取）
        /// </summary>
        private async Task LoadNameMappingAsync()
        {
            try
            {                // 取得中文版（zh-TW）
                var zhResponse = await _httpClient.GetAsync("https://valorant-api.com/v1/agents?language=zh-TW");
                var zhContent = await zhResponse.Content.ReadAsStringAsync();
                var zhData = JsonConvert.DeserializeObject<ValorantAgentsResponse>(zhContent);

                // 取得英文版（en-US）
                var enResponse = await _httpClient.GetAsync("https://valorant-api.com/v1/agents?language=en-US");
                var enContent = await enResponse.Content.ReadAsStringAsync();
                var enData = JsonConvert.DeserializeObject<ValorantAgentsResponse>(enContent);

                // 建立映射表 (中文 -> 英文)
                _nameMapping.Clear();
                foreach (var zhAgent in zhData.data)
                {
                    var enAgent = enData.data.FirstOrDefault(a => a.uuid == zhAgent.uuid);
                    if (enAgent != null)
                    {
                        // 角色名稱
                        _nameMapping[zhAgent.displayName] = enAgent.displayName;

                        // 技能名稱
                        if (zhAgent.abilities != null && enAgent.abilities != null)
                        {
                            for (int i = 0; i < zhAgent.abilities.Count && i < enAgent.abilities.Count; i++)
                            {
                                var zhAbility = zhAgent.abilities[i];
                                var enAbility = enAgent.abilities[i];
                                if (!string.IsNullOrEmpty(zhAbility.displayName) && !string.IsNullOrEmpty(enAbility.displayName))
                                {
                                    _nameMapping[zhAbility.displayName] = enAbility.displayName;
                                }
                            }
                        }
                    }
                }

                Console.WriteLine($"[ValorantService] 已載入 {_nameMapping.Count} 組中英文名稱映射");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ValorantService] 載入名稱映射失敗: {ex.Message}");
            }
        }
        #endregion
    }

    /// <summary>
    /// Valorant 遊戲狀態
    /// </summary>
    public class ValorantGameSession
    {
        public ValorantAgent CorrectAgent { get; set; }
        public ValorantAbility SelectedAbility { get; set; }
        public bool IsImageMode { get; set; }
        public ulong MessageId { get; set; }
    }
}
