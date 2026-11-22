using System;
using System.Net.Http;

namespace MusicBot2.Helpers
{
    /// <summary>
    /// 全程式共用的 HttpClient。每次 new HttpClient() 會各自開連線、用完 socket 還會卡在 TIME_WAIT，
    /// 量大時會把 socket 用光；共用一個就能重用連線池。不要 Dispose 它。
    /// </summary>
    public static class SharedHttp
    {
        public static readonly HttpClient Client = RetryHttpHandler.CreateClient(TimeSpan.FromSeconds(100));
    }
}
