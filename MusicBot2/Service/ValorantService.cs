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

                // 建立按鈕
                var componentBuilder = new ComponentBuilder()
                    .WithButton("📝 輸入答案", $"valorant_answer_{userId}", ButtonStyle.Primary);

                // 建立 Embed
                var embedBuilder = new EmbedBuilder()
                    .WithTitle("🎮 各位瓦學弟們 猜猜這是哪個角色？")
                    .WithDescription("我是誰?")
                    .WithColor(Discord.Color.Red)
                    .WithImageUrl("attachment://silhouette.png")
                    .WithFooter("請點擊下方按鈕輸入角色名稱")
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

                // 建立按鈕
                var componentBuilder = new ComponentBuilder()
                    .WithButton("📝 輸入技能名稱", $"valorant_answer_ability_{channelId}", ButtonStyle.Primary);

                // 建立 Embed
                var embedBuilder = new EmbedBuilder()
                    .WithTitle("🎮 猜猜這個技能叫什麼名字？")
                    .WithDescription($"**角色：** {correctAgent.displayName}\n**技能類型：** {randomAbility.slot}\n\n{randomAbility.description}")
                    .WithColor(Discord.Color.Red)
                    .WithThumbnailUrl(randomAbility.displayIcon)
                    .WithFooter("所有人都可以點擊按鈕輸入技能名稱來猜！")
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
        /// 處理按鈕點擊 - 開啟 Modal（猜角色圖片）
        /// </summary>
        public async Task ShowAnswerModalAsync(SocketMessageComponent component, ulong userId)
        {
            var modal = new ModalBuilder()
                .WithTitle("輸入你的答案")
                .WithCustomId($"valorant_modal_agent_{userId}")
                .AddTextInput("請輸入角色名稱", "answer_input",
                    placeholder: "例如：Jett, Sage, Phoenix...",
                    minLength: 2,
                    maxLength: 20)
                .Build();

            await component.RespondWithModalAsync(modal);
        }

        /// <summary>
        /// 處理按鈕點擊 - 開啟 Modal（猜技能名稱）
        /// </summary>
        public async Task ShowAbilityModalAsync(SocketMessageComponent component, ulong channelId)
        {
            var modal = new ModalBuilder()
                .WithTitle("輸入技能名稱")
                .WithCustomId($"valorant_modal_ability_{channelId}")
                .AddTextInput("請輸入技能的名稱", "ability_input",
                    placeholder: "例如：Cloudburst, Tailwind, Blade Storm...",
                    minLength: 2,
                    maxLength: 30)
                .Build();

            await component.RespondWithModalAsync(modal);
        }

        /// <summary>
        /// 處理 Modal 提交（猜角色圖片）
        /// </summary>
        public async Task<((ComponentBuilder? component, Embed embed), bool isError, ulong messageId)> HandleAgentModalSubmitAsync(SocketModal modal, ulong userId)
        {
            // 檢查是否有遊戲進行中
            if (!_activeImageGames.ContainsKey(userId))
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("⚠️ 提示")
                    .WithColor(Discord.Color.Orange)
                    .WithDescription("找不到遊戲，請重新開始！")
                    .Build();
                return ((null, errorEmbed), true, 0);
            }

            var session = _activeImageGames[userId];

            // 獲取使用者輸入
            var userAnswer = modal.Data.Components
                .First(x => x.CustomId == "answer_input").Value.Trim();

            // 檢查答案是否正確（不區分大小寫）
            bool isCorrect = string.Equals(userAnswer, session.CorrectAgent.displayName, StringComparison.OrdinalIgnoreCase);

            if (isCorrect)
            {
                // 答對了
                _activeImageGames.Remove(userId);

                var embedBuilder = new EmbedBuilder()
                    .WithTitle("🎉 答對了！")
                    .WithColor(Discord.Color.Green)
                    .WithDescription($"正確答案就是 **{session.CorrectAgent.displayName}**！")
                    .AddField("角色定位", session.CorrectAgent.role?.displayName ?? "未知", inline: true)
                    .AddField("恭喜", $"Valorant 大師 **{modal.User.Mention}** {CommonHelper.GetUserFace(modal.User.Id.ToString())}", inline: true)
                    .AddField("獎勵", await RewardsHelpers.GetRandomRewards(modal.Channel, modal.User as SocketGuildUser))
                    .WithImageUrl(session.CorrectAgent.fullPortrait)
                    .WithTimestamp(DateTimeOffset.Now);

                return ((new ComponentBuilder(), embedBuilder.Build()), false, session.MessageId);
            }
            else
            {
                // 答錯了
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ 答錯了！")
                    .WithColor(Discord.Color.Red)
                    .WithDescription($"{modal.User.Mention} {CommonHelper.GetUserFace(modal.User.Id.ToString())} 答：**{userAnswer}**\n\n請再試一次！")
                    .Build();
                return ((null, errorEmbed), true, 0);
            }
        }

        /// <summary>
        /// 處理 Modal 提交（猜技能名稱）- 返回訊息內容而不是 Embed
        /// </summary>
        public async Task<(string message, bool isCorrect, string correctAnswer, string agentName)> HandleAbilityModalSubmitAsync(SocketModal modal, ulong channelId)
        {
            // 檢查是否有遊戲進行中
            if (!_activeAbilityGames.ContainsKey(channelId))
            {
                return ("找不到遊戲，請重新開始！", false, "", "");
            }

            var session = _activeAbilityGames[channelId];

            // 獲取使用者輸入
            var userAnswer = modal.Data.Components
                .First(x => x.CustomId == "ability_input").Value.Trim();

            // 檢查答案是否正確（不區分大小寫）
            bool isCorrect = string.Equals(userAnswer, session.SelectedAbility.displayName, StringComparison.OrdinalIgnoreCase);

            if (isCorrect)
            {
                // 答對了 - 移除遊戲狀態
                _activeAbilityGames.Remove(channelId);
                return ("", true, session.SelectedAbility.displayName, session.CorrectAgent.displayName);
            }
            else
            {
                // 答錯了 - 遊戲繼續
                return (userAnswer, false, "", "");
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
