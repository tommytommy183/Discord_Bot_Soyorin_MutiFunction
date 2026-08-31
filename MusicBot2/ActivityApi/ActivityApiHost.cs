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
        app.MapPost("/auth/token", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            var req  = JsonSerializer.Deserialize<TokenRequest>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (req?.Code == null)
                return Results.BadRequest("missing code");

            var clientId     = Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID")     ?? "";
            var clientSecret = Environment.GetEnvironmentVariable("DISCORD_CLIENT_SECRET") ?? "";
            // Discord Embedded Activity 的 redirect_uri 固定是這個
            var redirectUri = "https://discord.com/api/oauth2/authorize";

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

        // ── /sprite/{kind}/{pokeId}  （Pokemon sprite proxy，繞過 Discord Activity CSP） ──
        app.MapGet("/sprite/{kind}/{pokeId}", async (string kind, int pokeId) =>
        {
            var url = kind switch
            {
                "back"  => $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/back/{pokeId}.png",
                "shiny" => $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/shiny/{pokeId}.png",
                _       => $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{pokeId}.png",
            };
            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "PokeTower/1.0");
                var bytes = await httpClient.GetByteArrayAsync(url);
                return Results.Bytes(bytes, "image/png");
            }
            catch
            {
                return Results.NotFound();
            }
        });

        TowerApiRoutes.Map(app);

        app.Urls.Add($"http://0.0.0.0:{port}");
        Console.WriteLine($"[ActivityApi] HTTP API 啟動於 port {port}");

        // 不 await，讓它在背景跑
        _ = app.RunAsync();
    }

    private record TokenRequest(string Code, string? RedirectUri);
}
