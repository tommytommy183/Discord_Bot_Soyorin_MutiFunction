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
    /// Fish Audio TTS 服務：將文字轉換為語音並在 Discord 語音頻道播放
    /// </summary>
    public class FishAudioService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private const string API_BASE_URL = "https://api.fish.audio/v1/tts";

        // Fish Audio 的 Soyo 聲音模型 ID
        private const string SOYO_REFERENCE_ID = "19c74d6eddb04a9b82dfd350b54e76e2";

        public FishAudioService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        /// <summary>
        /// 將文字轉換為語音並在語音頻道播放
        /// </summary>
        public async Task<bool> SpeakInVoiceChannelAsync(
            string text, 
            SocketGuildUser user, 
            IAudioClient audioClient)
        {
            try
            {
                var preview = text.Length > 50 ? text.Substring(0, 50) : text;
                Console.WriteLine($"[FishAudio] 開始 TTS: {preview}...");

                // 1. 調用 Fish Audio API 生成語音
                var audioData = await GenerateSpeechAsync(text);
                if (audioData == null || audioData.Length == 0)
                {
                    Console.WriteLine("[FishAudio] TTS 生成失敗");
                    return false;
                }

                // 2. 保存臨時音頻檔案
                var tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
                Directory.CreateDirectory(tempDir);
                var tempFile = Path.Combine(tempDir, $"tts_{Guid.NewGuid()}.mp3");
                await File.WriteAllBytesAsync(tempFile, audioData);

                Console.WriteLine($"[FishAudio] 音頻檔案已保存: {tempFile}");

                // 3. 在語音頻道播放
                await PlayAudioAsync(audioClient, tempFile);

                // 4. 清理臨時檔案
                try { File.Delete(tempFile); } catch { }

                Console.WriteLine("[FishAudio] TTS 播放完成");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FishAudio Error] {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 調用 Fish Audio API 生成語音
        /// </summary>
        private async Task<byte[]> GenerateSpeechAsync(string text)
        {
            try
            {
                var requestBody = new
                {
                    text = text,
                    reference_id = SOYO_REFERENCE_ID,
                    format = "mp3",
                    latency = "normal" // 可選: "normal" 或 "balanced"
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
        /// 在語音頻道播放音頻
        /// </summary>
        private async Task PlayAudioAsync(IAudioClient audioClient, string filePath)
        {
            if (audioClient == null || audioClient.ConnectionState != ConnectionState.Connected)
            {
                Console.WriteLine("[FishAudio] 音頻客戶端未連接");
                return;
            }

            using var output = audioClient.CreatePCMStream(AudioApplication.Mixed);
            using var ffmpeg = CreateFfmpegProcess(filePath);

            if (ffmpeg == null)
            {
                Console.WriteLine("[FishAudio] FFmpeg 啟動失敗");
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
        /// 創建 FFmpeg 進程（轉換音頻為 PCM）
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
