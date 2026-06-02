using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System.Net.Http.Headers;
using System.Text;
using System.Diagnostics;
using NAudio.Wave;

namespace MusicBot2.Service
{
    public class ElevenLabsService
    {
        private readonly DiscordSocketClient _client;
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _audioStoragePath;
        private readonly string _ffmpegPath;

        public ElevenLabsService(DiscordSocketClient client, string apiKey)
        {
            _client = client;
            _apiKey = apiKey;

            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("xi-api-key", _apiKey);

            _audioStoragePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TTS_Audio");
            Directory.CreateDirectory(_audioStoragePath);

            string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            _ffmpegPath = Path.Combine(projectRoot, "ffmpeg-master-latest-win64-gpl-shared", "bin", "ffmpeg.exe");
            
            Console.WriteLine($"🎬 FFmpeg 路徑: {_ffmpegPath}");
            
            if (!File.Exists(_ffmpegPath))
            {
                Console.WriteLine($"⚠️ 警告: FFmpeg 不存在於 {_ffmpegPath}");
                _ffmpegPath = "ffmpeg";
            }
        }

        public async Task SpeakAsync(IVoiceChannel userChannel, string text, string model, string voiceID)
        {
            if (userChannel == null)
                throw new ArgumentNullException(nameof(userChannel), "User not in voice channel");

            string? audioFile = null;
            IAudioClient? audioClient = null;

            try
            {
                // 1️⃣ 調用 ElevenLabs API 產生語音
                var audioData = await GenerateSpeech(text, model, voiceID);

                // 2️⃣ 儲存音訊檔案
                audioFile = Path.Combine(_audioStoragePath, $"{DateTime.Now:yyyyMMdd_HHmmss}.mp3");
                await File.WriteAllBytesAsync(audioFile, audioData);

                //測試用
                //audioFile = "";

                // 3️⃣ Bot 連接語音頻道
                audioClient = await userChannel.ConnectAsync();
                await Task.Delay(1000);

                // 4️⃣ 播放音訊
                await SendAudioAsync(audioClient, audioFile);
                
                Console.WriteLine("✅ TTS 播放完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 語音播放失敗: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                throw new Exception($"語音播放失敗: {ex.Message}", ex);
            }
            finally
            {
                if (audioClient != null)
                {
                    await Task.Delay(500);
                    await audioClient.StopAsync();
                    Console.WriteLine("🔌 已斷開語音連接");
                }
            }
        }

        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            return sanitized.Length > 50 ? sanitized.Substring(0, 50) : sanitized;
        }

        public void CleanOldFiles(int daysToKeep = 7)
        {
            var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
            var files = Directory.GetFiles(_audioStoragePath, "*.mp3");

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTime < cutoffDate)
                {
                    try
                    {
                        File.Delete(file);
                        Console.WriteLine($"🗑️ 刪除舊檔案: {file}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ 無法刪除檔案 {file}: {ex.Message}");
                    }
                }
            }
        }

        private async Task<byte[]> GenerateSpeech(string text, string model, string voiceID)
        {
            var request = new
            {
                text = text,
                model_id = model,
                voice_settings = new
                {
                    stability = 0.5,
                    similarity_boost = 0.75,
                    style = 0.2,
                    use_speaker_boost = true
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(request);

            Console.WriteLine($"📡 POST https://api.elevenlabs.io/v1/text-to-speech/{voiceID}");

            var response = await _http.PostAsync(
                $"https://api.elevenlabs.io/v1/text-to-speech/{voiceID}",
                new StringContent(json, Encoding.UTF8, "application/json")
            );

            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"❌ ElevenLabs Error: {content}");
                throw new Exception($"ElevenLabs API 錯誤: {content}");
            }

            Console.WriteLine($"✅ Success: {response.StatusCode}");

            return await response.Content.ReadAsByteArrayAsync();
        }

        private async Task SendAudioAsync(IAudioClient client, string path)
        {
            var output = client.CreatePCMStream(AudioApplication.Mixed);

            try
            {
                Console.WriteLine("🎵 開始播放 TTS...");
                
                using (var audioFile = new AudioFileReader(path))
                {
                    Console.WriteLine($"📊 原始音訊: {audioFile.WaveFormat.SampleRate}Hz, {audioFile.WaveFormat.Channels} channels");
                    Console.WriteLine($"⏱️ 長度: {audioFile.TotalTime.TotalSeconds:F2} 秒");

                    var channels = audioFile.WaveFormat.Channels;
                    
                    // 先重採樣到 48kHz（保持原本聲道數）
                    using (var resampler = new MediaFoundationResampler(audioFile, new WaveFormat(48000, channels)))
                    {
                        resampler.ResamplerQuality = 60;
                        
                        byte[] buffer = new byte[4096];
                        int bytesRead;
                        int totalBytesRead = 0;

                        while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            totalBytesRead += bytesRead;
                            
                            // 如果是單聲道，手動轉換為立體聲
                            if (channels == 1)
                            {
                                byte[] stereoBuffer = new byte[bytesRead * 2];
                                
                                for (int i = 0; i < bytesRead / 2; i++)
                                {
                                    // 複製左右聲道（單聲道轉立體聲）
                                    stereoBuffer[i * 4] = buffer[i * 2];         // Left Low
                                    stereoBuffer[i * 4 + 1] = buffer[i * 2 + 1]; // Left High
                                    stereoBuffer[i * 4 + 2] = buffer[i * 2];     // Right Low
                                    stereoBuffer[i * 4 + 3] = buffer[i * 2 + 1]; // Right High
                                }
                                
                                await output.WriteAsync(stereoBuffer, 0, stereoBuffer.Length);
                            }
                            else
                            {
                                // 已經是立體聲，直接寫入
                                await output.WriteAsync(buffer, 0, bytesRead);
                            }
                        }

                        Console.WriteLine($"📊 總共傳送: {totalBytesRead / 1024} KB");
                        await output.FlushAsync();
                    }
                }
                
                Console.WriteLine("✅ TTS 播放流程完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Audio error: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");
                throw;
            }
            finally
            {
                output.Dispose();
            }
        }
    }
}
