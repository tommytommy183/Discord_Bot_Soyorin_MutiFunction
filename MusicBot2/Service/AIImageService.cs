using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class AIImageService
    {
        private readonly HttpClient _httpClient;
        private readonly List<string> _googleApiKeys;

        // Google image models in priority order
        private static readonly string[] _googleImageModels = new[]
        {
            "gemini-3.1-flash-image",
            "gemini-3.1-flash-lite-image",
            "gemini-2.5-flash-image",
        };

        public AIImageService(string googleApiKeys = null)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _googleApiKeys = string.IsNullOrWhiteSpace(googleApiKeys)
                ? new List<string>()
                : googleApiKeys.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
        }

        public async Task<Stream> GenerateImageAsync(string prompt)
        {
            // Try Google AI image models first
            if (_googleApiKeys.Count > 0)
            {
                foreach (var model in _googleImageModels)
                {
                    foreach (var key in _googleApiKeys)
                    {
                        try
                        {
                            var stream = await CallGoogleImageAsync(prompt, model, key);
                            if (stream != null)
                            {
                                Console.WriteLine($"[AIImage] Google model:{model} 成功");
                                return stream;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[AIImage] Google model:{model} key:...{key[^6..]} 失敗: {ex.Message}");
                        }
                    }
                }
                Console.WriteLine("[AIImage] 所有 Google image models 失敗，fallback 到 Pollinations");
            }

            // Fallback: Pollinations
            return await CallPollinationsAsync(prompt);
        }

        private async Task<Stream> CallGoogleImageAsync(string prompt, string model, string apiKey)
        {
            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                },
                generationConfig = new
                {
                    responseModalities = new[] { "IMAGE", "TEXT" }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            Console.WriteLine($"[AIImage] Google model:{model} key:...{apiKey[^6..]}");

            using var response = await _httpClient.PostAsync(
                url, new StringContent(json, Encoding.UTF8, "application/json"));

            var resultJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[AIImage] Google error {(int)response.StatusCode}: {resultJson[..Math.Min(200, resultJson.Length)]}");
                return null;
            }

            using var doc = JsonDocument.Parse(resultJson);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return null;

            var parts = candidates[0]
                .GetProperty("content")
                .GetProperty("parts");

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("inlineData", out var inlineData))
                {
                    var b64 = inlineData.GetProperty("data").GetString();
                    if (!string.IsNullOrWhiteSpace(b64))
                    {
                        var bytes = Convert.FromBase64String(b64);
                        var ms = new MemoryStream(bytes);
                        ms.Position = 0;
                        return ms;
                    }
                }
            }

            return null;
        }

        private async Task<Stream> CallPollinationsAsync(string prompt)
        {
            string encodedPrompt = WebUtility.UrlEncode(prompt);
            string url = $"https://image.pollinations.ai/prompt/{encodedPrompt}?model=flux";

            Console.WriteLine($"[AIImage] Pollinations fallback");
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            var ms = new MemoryStream(bytes);
            ms.Position = 0;
            return ms;
        }
    }
}
