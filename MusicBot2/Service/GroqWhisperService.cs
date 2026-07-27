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
        public async Task<string> TranscribeAudioAsync(byte[] audioData)
        {
            try
            {
                // �O�s�{�ɭ��W�ɮ�
                var tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
                Directory.CreateDirectory(tempDir);
                var guidString = Guid.NewGuid().ToString();
                var tempFile = Path.Combine(tempDir, $"voice_{guidString}.wav");
                await File.WriteAllBytesAsync(tempFile, audioData);

                // �ե� Groq API
                using var formData = new MultipartFormDataContent();
                using var fileContent = new ByteArrayContent(audioData);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                formData.Add(fileContent, "file", "audio.wav");
                formData.Add(new StringContent(MODEL), "model");
                formData.Add(new StringContent("zh"), "language"); // ����

                var response = await _httpClient.PostAsync(API_URL, formData);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();

                    // �M�z�{���ɮ�
                    try { File.Delete(tempFile); } catch { }
                    return null;
                }

                var resultJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(resultJson);
                var text = doc.RootElement.GetProperty("text").GetString();


                // �M�z�{���ɮ�
                try { File.Delete(tempFile); } catch { }

                return text?.Trim();
            }
            catch (Exception ex)
            {
                return null;
            }
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
                    try
                    {
                        var buffer = new AudioStreamBuffer();
                        _userBuffers.TryAdd((uint)userId, buffer);


                        byte[] audioBuffer = new byte[4096];
                        int bytesRead;

                        while ((bytesRead = await stream.ReadAsync(audioBuffer, 0, audioBuffer.Length, _cts.Token)) > 0)
                        {
                            buffer.Write(audioBuffer, bytesRead);

                            // �C 3 ���B�z�@���]���˴����R���^
                            if (buffer.Duration >= TimeSpan.FromSeconds(3))
                            {
                                await ProcessAudioBufferAsync(userId, buffer);
                                buffer.Clear();
                            }
                        }

                        var removed = _userBuffers.TryRemove((uint)userId, out _);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                    }
                });

                return Task.CompletedTask;
            }

            private async Task ProcessAudioBufferAsync(ulong userId, AudioStreamBuffer buffer)
            {
                try
                {
                    var audioData = buffer.GetAudioData();
                    if (audioData == null || audioData.Length < 1000)
                    {
                        return; // ���W�ӵu�A����
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
            private readonly MemoryStream _buffer = new();
            private DateTime _startTime = DateTime.Now;

            public TimeSpan Duration => DateTime.Now - _startTime;

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
                _startTime = DateTime.Now;
            }

            public void Dispose()
            {
                _buffer?.Dispose();
            }
        }
    }
}
