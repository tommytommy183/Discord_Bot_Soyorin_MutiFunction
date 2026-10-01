using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class AIImageService
    {
        private readonly HttpClient _httpClient;
        private const string CfWorkerUrl = "https://broken-queen-beaa.tommytommy183.workers.dev";
        private const string CfApiKey = "8f7c2d91e6a44b0f9c3e8d72a1f65b9c4e7d2a8f";

        public AIImageService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        }

        public async Task<Stream> GenerateImageAsync(string prompt)
        {
            try
            {
                    string encodedPrompt = WebUtility.UrlEncode(prompt);
                    string url = $"{CfWorkerUrl}/?prompt={encodedPrompt}";
                    Console.WriteLine($"[AIImage] Cloudflare Worker: {url[..Math.Min(80, url.Length)]}");

                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CfApiKey);
                    using var response = await _httpClient.SendAsync(req);
                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        var ms = new MemoryStream(bytes);
                        ms.Position = 0;
                        Console.WriteLine($"[AIImage] Cloudflare Worker 成功，{bytes.Length} bytes");
                        return ms;
                    }
                    Console.WriteLine($"[AIImage] Cloudflare Worker error {(int)response.StatusCode}，fallback");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIImage] Cloudflare Worker 失敗: {ex.Message}，fallback");
            }

            // Fallback: Pollinations
            return await CallPollinationsAsync(prompt);
        }

        private async Task<Stream> CallPollinationsAsync(string prompt)
        {
            string encodedPrompt = WebUtility.UrlEncode(prompt);
            string url = $"https://image.pollinations.ai/prompt/{encodedPrompt}?model=flux";
            Console.WriteLine("[AIImage] Pollinations fallback");

            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            var ms = new MemoryStream(bytes);
            ms.Position = 0;
            return ms;
        }
    }
}
