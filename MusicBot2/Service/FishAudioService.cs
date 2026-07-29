using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    /// <summary>
    /// Fish Audio TTS �A�ȡG�N��r�ഫ���y���æb Discord �y���W�D����
    /// </summary>
    public class FishAudioService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private const string API_BASE_URL = "https://api.fish.audio/v1/tts";
        public static readonly Dictionary<string, string> ReferenceList = new Dictionary<string, string>
        {
            { "SOYO", "23d8ed0094914caa89d350c4ce803cc9" },
            { "預設", "9a9cf47702da476aa4629e2506d4a857" },
            { "ANON", "4b5eb5bb302a4cb6bf975f30b3e58c07" },
            { "更高級的soyo", "19c74d6eddb04a9b82dfd350b54e76e2" },
            { "tomo", "781d170ce2e24391a46701981228951c" },
            { "中文anon", "c5c17c9709384ba9a4b294662a2af0b1" },
            { "是我迪奧", "5a8e46f1b94c4351aebb8a66b113cec5" },
            { "中文tomo", "b4f70fdef5f943c2bf43db00e80ad680" },
            { "中文soyo", "333bb1723a3847cdb06d77a131d9576d" },
            { "中文祥子", "8ce20e146c9545619e5f6f4c95564a2d" },
        };

        // Fish Audio 音色 ID，預設中文soyo
        private string _currentReferenceId = "333bb1723a3847cdb06d77a131d9576d";

        /// <summary>
        /// 是否正在播放語音（用來避免 STT 回饋迴圈）
        /// </summary>
        public bool IsSpeaking { get; private set; }

        /// <summary>
        /// 切換當前音色，回傳音色名稱；若找不到則回傳 null
        /// </summary>
        public string SetVoice(string voiceName)
        {
            if (ReferenceList.TryGetValue(voiceName, out var id))
            {
                _currentReferenceId = id;
                return voiceName;
            }
            return null;
        }

        public string GetCurrentVoiceName()
        {
            return ReferenceList.FirstOrDefault(x => x.Value == _currentReferenceId).Key ?? "未知";
        }

        // TTS 模型：s2.1-pro-free 是免費 API 模型（至 2026/8/31），不指定的話會用付費模型導致 402
        private const string TTS_MODEL = "s2.1-pro-free";

        public FishAudioService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            _httpClient.DefaultRequestHeaders.Add("model", TTS_MODEL);
        }

        /// <summary>
        /// �N��r�ഫ���y���æb�y���W�D����
        /// </summary>
        public async Task<bool> SpeakInVoiceChannelAsync(
            string text,
            SocketGuildUser user,
            IAudioClient audioClient,
            IMessageChannel textChannel = null)
        {
            try
            {
                var preview = text.Length > 50 ? text.Substring(0, 50) : text;

                // 1. �ե� Fish Audio API �ͦ��y��
                var audioData = await GenerateSpeechAsync(text);
                if (audioData == null || audioData.Length == 0)
                {
                    Console.WriteLine("[FishAudio] TTS �ͦ�����");
                    if (textChannel != null)
                    {
                        try { await textChannel.SendMessageAsync("⚠️ TTS 生成失敗（詳細原因看 log，402 代表 Fish Audio 帳戶沒 credits）"); } catch { }
                    }
                    return false;
                }

                // 2. �O�s�{�ɭ��W�ɮ�
                var tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
                Directory.CreateDirectory(tempDir);
                var guid = Guid.NewGuid();
                var tempFile = Path.Combine(tempDir, $"tts_{guid}.mp3");
                await File.WriteAllBytesAsync(tempFile, audioData);

                Console.WriteLine($"[FishAudio] ���W�ɮפw�O�s: {tempFile}");

                // 先把語音檔傳到文字頻道，就算之後播放失敗也看得到檔案
                if (textChannel != null)
                {
                    try
                    {
                        await textChannel.SendFileAsync(tempFile, "🔊");
                    }
                    catch (Exception sendEx)
                    {
                        Console.WriteLine($"[FishAudio] 傳送語音檔失敗: {sendEx.Message}");
                    }
                }

                // 3. 在語音頻道播放
                IsSpeaking = true;
                try
                {
                    await PlayAudioAsync(audioClient, tempFile);
                }
                finally
                {
                    IsSpeaking = false;
                }

                // 4. 清理暫存檔案
                try { File.Delete(tempFile); } catch { }

                Console.WriteLine("[FishAudio] TTS ���񧹦�");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FishAudio Error] {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// �ե� Fish Audio API �ͦ��y��
        /// </summary>
        private async Task<byte[]> GenerateSpeechAsync(string text)
        {
            try
            {
                var requestBody = new
                {
                    text = text,
                    reference_id = _currentReferenceId,
                    format = "mp3",
                    latency = "normal" // �i��: "normal" �� "balanced"
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(API_BASE_URL, content);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[FishAudio API Error] {response.StatusCode}: {error}");
                    return null;
                }

                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FishAudio API Exception] {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// �b�y���W�D�����W
        /// </summary>
        private async Task PlayAudioAsync(IAudioClient audioClient, string filePath)
        {
            if (audioClient == null || audioClient.ConnectionState != ConnectionState.Connected)
            {
                Console.WriteLine("[FishAudio] ���W�Ȥ�ݥ��s��");
                return;
            }

            using var output = audioClient.CreatePCMStream(AudioApplication.Mixed);
            using var ffmpeg = CreateFfmpegProcess(filePath);

            if (ffmpeg == null)
            {
                Console.WriteLine("[FishAudio] FFmpeg �Ұʥ���");
                return;
            }

            try
            {
                byte[] buffer = new byte[4096];
                int bytesRead;

                while ((bytesRead = await ffmpeg.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await output.WriteAsync(buffer, 0, bytesRead);
                }

                await output.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FishAudio Playback Error] {ex.Message}");
            }
            finally
            {
                try
                {
                    ffmpeg.Kill();
                    ffmpeg.Dispose();
                }
                catch { }
            }
        }

        /// <summary>
        /// �Ы� FFmpeg �i�{�]�ഫ���W�� PCM�^
        /// </summary>
        private Process CreateFfmpegProcess(string filePath)
        {
            string ffmpegPath;

            if (OperatingSystem.IsWindows())
            {
                string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
                string winFfmpeg = Path.Combine(projectRoot, "ffmpeg-master-latest-win64-gpl-shared", "bin", "ffmpeg.exe");
                ffmpegPath = File.Exists(winFfmpeg) ? winFfmpeg : "ffmpeg";
            }
            else
            {
                ffmpegPath = "ffmpeg";
            }

            var arguments = $"-hide_banner -loglevel warning -i \"{filePath}\" -ac 2 -f s16le -ar 48000 pipe:1";

            try
            {
                return Process.Start(new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FishAudio FFmpeg Error] {ex.Message}");
                return null;
            }
        }
    }
}
