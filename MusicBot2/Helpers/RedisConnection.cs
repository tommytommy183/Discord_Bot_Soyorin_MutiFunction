using StackExchange.Redis;
using System;
using System.Collections.Concurrent;

namespace MusicBot2.Helpers
{
    /// <summary>
    /// 全程式共用的 Redis 連線。
    /// 以前每個 service 各自開一條連線（共 10 條），每條都有自己的 socket、緩衝區和重連機制；
    /// ConnectionMultiplexer 本來就是設計成整個程式共用一個，而且是 thread-safe。
    /// </summary>
    public static class RedisConnection
    {
        private static readonly ConcurrentDictionary<string, Lazy<ConnectionMultiplexer>> _conns = new();

        public static ConnectionMultiplexer Get(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Redis connection string is empty", nameof(connectionString));

            var lazy = _conns.GetOrAdd(connectionString, cs => new Lazy<ConnectionMultiplexer>(() =>
            {
                var options = ConfigurationOptions.Parse(cs);
                options.ConnectTimeout = 10000;
                options.SyncTimeout = 10000;
                options.AsyncTimeout = 10000;
                options.ConnectRetry = 3;
                options.AbortOnConnectFail = false;
                options.KeepAlive = 60;
                return ConnectionMultiplexer.Connect(options);
            }));

            try
            {
                return lazy.Value;
            }
            catch
            {
                // 連線建立失敗就不要快取，下次再試
                _conns.TryRemove(connectionString, out _);
                throw;
            }
        }
    }
}
