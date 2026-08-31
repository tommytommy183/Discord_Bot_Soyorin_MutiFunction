using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MusicBot2.Service;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MusicBot2.ActivityApi;

/// <summary>
/// 在 Discord Bot 旁邊啟動一個輕量 HTTP API，供 Activity 前端呼叫。
/// 呼叫方式：await ActivityApiHost.StartAsync(services, port: 5000);
/// </summary>
public static class ActivityApiHost
{
    public static async Task StartAsync(IServiceProvider services, int port = 5000)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(services.GetRequiredService<PokeTowerService>());
        builder.Services.AddSingleton(services.GetRequiredService<PokeGameService>());

        // CORS：允許前端 localhost 與 Discord proxy
        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
            p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        var app = builder.Build();
        app.UseCors();

        // ── /api/auth/token  （Discord OAuth code → access_token） ────
        app.MapPost("/api/auth/token", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            var req  = JsonSerializer.Deserialize<TokenRequest>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (req?.Code == null)
                return Results.BadRequest("missing code");

            var clientId     = Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID")     ?? "";
            var clientSecret = Environment.GetEnvironmentVariable("DISCORD_CLIENT_SECRET") ?? "";
            // 優先用前端傳來的 redirectUri（需與 Discord Portal 完全一致）
            // fallback 到環境變數
            var redirectUri  = req.RedirectUri
                ?? Environment.GetEnvironmentVariable("DISCORD_REDIRECT_URI")
                ?? "https://poketower-activity.pages.dev";

            using var http = new HttpClient();
            var form = new Dictionary<string, string>
            {
                ["client_id"]     = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"]    = "authorization_code",
                ["code"]          = req.Code,
                ["redirect_uri"]  = redirectUri,
            };
            var resp = await http.PostAsync(
                "https://discord.com/api/oauth2/token",
                new FormUrlEncodedContent(form));

            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return Results.Problem(json, statusCode: (int)resp.StatusCode);

            return Results.Text(json, "application/json");
        });

        TowerApiRoutes.Map(app);

        app.Urls.Add($"http://0.0.0.0:{port}");
        Console.WriteLine($"[ActivityApi] HTTP API 啟動於 port {port}");

        // 不 await，讓它在背景跑
        _ = app.RunAsync();
    }

    private record TokenRequest(string Code, string? RedirectUri);
}
