using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    /// <summary>
    /// Groq Whisper STT �A�ȡG��ť�y���W�D���ഫ�y������r
    /// </summary>
    public class GroqWhisperService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private const string API_URL = "https://api.groq.com/openai/v1/audio/transcriptions";
        private const string MODEL = "whisper-large-v3";

        // �O�����b��ť���y���W�D
        private readonly ConcurrentDictionary<ulong, VoiceChannelListener> _activeListeners = new();
        
        public GroqWhisperService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        /// <summary>
        /// �}�l��ť�y���W�D
        /// </summary>
        public async Task<IAudioClient> StartListeningAsync(
            IVoiceChannel voiceChannel,
            Func<string, SocketGuildUser, Task> onSpeechRecognized)
        {
            try
            {
                if (_activeListeners.ContainsKey(voiceChannel.Id))
                {
                    return null;
                }


                var audioClient = await voiceChannel.ConnectAsync(selfDeaf: false, selfMute: false);
                var listener = new VoiceChannelListener(audioClient, voiceChannel, this, onSpeechRecognized);

                _activeListeners.TryAdd(voiceChannel.Id, listener);
                await listener.StartAsync();

                return audioClient;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GroqWhisper] 開始監聽失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// �����ť�y���W�D
        /// </summary>
        public async Task StopListeningAsync(ulong channelId)
        {
            if (_activeListeners.TryRemove(channelId, out var listener))
            {
                await listener.StopAsync();
            }
        }

        /// <summary>
        /// �N���W�ഫ����r�]�ե� Groq Whisper API�^
        /// </summary>
        public async Task<string> TranscribeAudioAsync(byte[] pcmData)
        {
            try
            {
                // Discord 語音解碼後是 48kHz 16-bit 立體聲 PCM，必須包上 WAV header Groq 才讀得懂
                var wavData = CreateWavFile(pcmData);

                using var formData = new MultipartFormDataContent();
                using var fileContent = new ByteArrayContent(wavData);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                formData.Add(fileContent, "file", "audio.wav");
                formData.Add(new StringContent(MODEL), "model");
                formData.Add(new StringContent("zh"), "language"); // 中文

                var response = await _httpClient.PostAsync(API_URL, formData);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[GroqWhisper API Error] {response.StatusCode}: {error}");
                    return null;
                }

                var resultJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(resultJson);
                var text = doc.RootElement.GetProperty("text").GetString();

                return text?.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GroqWhisper Error] {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 把裸 PCM 包成 WAV（48kHz / 16-bit / 立體聲）
        /// </summary>
        private static byte[] CreateWavFile(byte[] pcmData)
        {
            const int sampleRate = 48000;
            const short channels = 2;
            const short bitsPerSample = 16;
            const int byteRate = sampleRate * channels * (bitsPerSample / 8);
            const short blockAlign = channels * (bitsPerSample / 8);

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcmData.Length);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1); // PCM 格式
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write(bitsPerSample);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(pcmData.Length);
            writer.Write(pcmData);
            writer.Flush();
            return ms.ToArray();
        }

        /// <summary>
        /// �y���W�D��ť���]�������^
        /// </summary>
        private class VoiceChannelListener
        {
            private readonly IAudioClient _audioClient;
            private readonly IVoiceChannel _voiceChannel;
            private readonly GroqWhisperService _service;
            private readonly Func<string, SocketGuildUser, Task> _onSpeechRecognized;
            private readonly CancellationTokenSource _cts;
            private readonly ConcurrentDictionary<uint, AudioStreamBuffer> _userBuffers = new();

            public VoiceChannelListener(
                IAudioClient audioClient,
                IVoiceChannel voiceChannel,
                GroqWhisperService service,
                Func<string, SocketGuildUser, Task> onSpeechRecognized)
            {
                _audioClient = audioClient;
                _voiceChannel = voiceChannel;
                _service = service;
                _onSpeechRecognized = onSpeechRecognized;
                _cts = new CancellationTokenSource();
            }

            public async Task StartAsync()
            {
                try
                {
                    // �q�\�y���ƾڨƥ�
                    _audioClient.StreamCreated += OnStreamCreated;
                }
                catch (Exception ex)
                {
                }

                await Task.CompletedTask;
            }

            public async Task StopAsync()
            {
                _cts.Cancel();
                _audioClient.StreamCreated -= OnStreamCreated;

                foreach (var buffer in _userBuffers.Values)
                {
                    buffer.Dispose();
                }
                _userBuffers.Clear();

                await _audioClient.StopAsync();
            }

            private Task OnStreamCreated(ulong userId, AudioInStream stream)
            {
                _ = Task.Run(async () =>
                {
                    OpusDotNet.OpusDecoder decoder = null;
                    try
                    {
                        // Discord 傳來的是 Opus 編碼封包，要先解碼成 PCM 才能給 Whisper
                        decoder = new OpusDotNet.OpusDecoder(48000, 2);
                        var buffer = new AudioStreamBuffer();
                        _userBuffers.TryAdd((uint)userId, buffer);

                        Task<RTPFrame> pendingRead = null;

                        while (!_cts.IsCancellationRequested)
                        {
                            pendingRead ??= stream.ReadFrameAsync(_cts.Token);
                            var completed = await Task.WhenAny(pendingRead, Task.Delay(800, _cts.Token));

                            if (completed != pendingRead)
                            {
                                // 800ms 沒有新音訊 → 視為一句話講完，送去辨識
                                if (buffer.Duration >= TimeSpan.FromMilliseconds(400))
                                {
                                    await ProcessAudioBufferAsync(userId, buffer);
                                }
                                buffer.Clear();
                                continue;
                            }

                            var frame = await pendingRead;
                            pendingRead = null;

                            if (frame.Payload == null || frame.Payload.Length == 0) continue;

                            try
                            {
                                var pcm = decoder.Decode(frame.Payload, frame.Payload.Length, out var decodedLength);
                                buffer.Write(pcm, decodedLength);
                            }
                            catch
                            {
                                continue; // 壞掉的 frame 直接略過
                            }

                            // 講太久的保護：滿 8 秒就先送一段
                            if (buffer.Duration >= TimeSpan.FromSeconds(8))
                            {
                                await ProcessAudioBufferAsync(userId, buffer);
                                buffer.Clear();
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GroqWhisper Stream Error] {ex.Message}");
                    }
                    finally
                    {
                        decoder?.Dispose();
                        _userBuffers.TryRemove((uint)userId, out _);
                    }
                });

                return Task.CompletedTask;
            }

            private async Task ProcessAudioBufferAsync(ulong userId, AudioStreamBuffer buffer)
            {
                try
                {
                    var audioData = buffer.GetAudioData();
                    if (audioData == null || audioData.Length < 48000) // 不到 0.25 秒就略過
                    {
                        return;
                    }

                    var text = await _service.TranscribeAudioAsync(audioData);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return;
                    }

                    // �ˬd�O�_���� soyo
                    if (text.ToLower().Contains("soyo") ||
                        text.Contains("�j�ժL") ||
                        text.Contains("�n�@"))
                    {

                        // ���o���ܪ��Τ�
                        var guild = (_voiceChannel as SocketVoiceChannel)?.Guild;
                        var user = guild?.GetUser(userId);

                        if (user != null)
                        {
                            await _onSpeechRecognized(text, user);
                        }
                    }
                }
                catch (Exception ex)
                {
                }
            }
        }

        /// <summary>
        /// ���W�w�İϡ]�������^
        /// </summary>
        private class AudioStreamBuffer : IDisposable
        {
            // 48kHz * 2 聲道 * 16-bit = 每秒 192000 bytes
            private const double BYTES_PER_SECOND = 48000.0 * 2 * 2;

            private readonly MemoryStream _buffer = new();

            // 用實際錄到的 PCM 長度換算時長，不受中間停頓影響
            public TimeSpan Duration => TimeSpan.FromSeconds(_buffer.Length / BYTES_PER_SECOND);

            public void Write(byte[] data, int count)
            {
                _buffer.Write(data, 0, count);
            }

            public byte[] GetAudioData()
            {
                return _buffer.ToArray();
            }

            public void Clear()
            {
                _buffer.SetLength(0);
            }

            public void Dispose()
            {
                _buffer?.Dispose();
            }
        }
    }
}
