using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBot2.Helpers
{
    /// <summary>
    /// 對外 API（PokeAPI 等）用的 HttpClient：
    ///   - 連線池不會無限期重用舊連線（對方先關掉閒置連線時，重用就會出現 "Connection reset by peer"）
    ///   - GET 遇到暫時性錯誤（連線被重置、逾時、429、5xx）自動重試，間隔 0.5s → 1s → 2s
    /// </summary>
    public sealed class RetryHttpHandler : DelegatingHandler
    {
        private const int MaxAttempts = 4;

        public RetryHttpHandler() : base(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            AutomaticDecompression = DecompressionMethods.All,
        })
        { }

        public static HttpClient CreateClient(TimeSpan? timeout = null)
            => new HttpClient(new RetryHttpHandler()) { Timeout = timeout ?? TimeSpan.FromSeconds(30) };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // 只重試沒有 body 的冪等請求
            if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
                return await base.SendAsync(request, cancellationToken);

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    var resp = await base.SendAsync(request, cancellationToken);
                    bool transient = resp.StatusCode == (HttpStatusCode)429 || (int)resp.StatusCode >= 500;
                    if (!transient || attempt >= MaxAttempts)
                        return resp;
                    resp.Dispose();
                }
                catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested
                                           && (ex is HttpRequestException || ex is IOException))
                {
                    Console.WriteLine($"[Http] {request.RequestUri} 第 {attempt} 次失敗，重試：{ex.Message}");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)), cancellationToken);
            }
        }
    }
}
