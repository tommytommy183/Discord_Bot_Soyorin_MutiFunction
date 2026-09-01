using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MusicBot2.Service;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MusicBot2.ActivityApi;

public static class TowerApiRoutes
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Session token store: token → (userId, userName, expiry)
    private static readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();
    private record SessionInfo(ulong UserId, string UserName, DateTimeOffset Expiry);

    /// <summary>產生一個短效 session token，Bot 在發連結前呼叫</summary>
    public static string CreateSession(ulong userId, string userName)
    {
        // 清理過期的 session
        foreach (var key in _sessions.Keys.ToList())
            if (_sessions.TryGetValue(key, out var s) && s.Expiry < DateTimeOffset.UtcNow)
                _sessions.TryRemove(key, out _);

        var token = Guid.NewGuid().ToString("N");
        _sessions[token] = new SessionInfo(userId, userName, DateTimeOffset.UtcNow.AddMinutes(10));
        return token;
    }

    public static void Map(WebApplication app)
    {
        // ── GET /api/auth/session/{token} ─────────────────────────────
        // 前端用這個換使用者資訊（取代 Discord OAuth）
        app.MapGet("/auth/session/{token}", (string token) =>
        {
            if (!_sessions.TryRemove(token, out var info))
                return Results.Json(new { error = "token 無效或已過期" }, _json, statusCode: 401);
            if (info.Expiry < DateTimeOffset.UtcNow)
                return Results.Json(new { error = "token 已過期" }, _json, statusCode: 401);

            return Results.Json(new { userId = info.UserId.ToString(), userName = info.UserName }, _json);
        });

        // ── GET /api/tower/passives ────────────────────────────────────
        app.MapGet("/tower/passives", (PokeTowerService towerSvc) =>
            Results.Json(towerSvc.GetRandomPassives(), _json));

        // ── GET /api/tower/run/{channelId} ─────────────────────────────
        app.MapGet("/tower/run/{channelId}", (string channelId, PokeTowerService svc) =>
        {
            if (!ulong.TryParse(channelId, out var cid))
                return Results.BadRequest("invalid channelId");

            var state = svc.GetFrontendState(cid);
            if (state == null) return Results.NotFound("no active run");
            return Results.Json(state, _json);
        });

        // ── GET /api/tower/pokemon/{userId} ────────────────────────────
        app.MapGet("/tower/pokemon/{userId}", async (string userId, PokeGameService pokeSvc) =>
        {
            if (!ulong.TryParse(userId, out var uid))
                return Results.BadRequest("invalid userId");

            var player = await pokeSvc.GetPlayerAsync(uid, "");
            if (player == null)
                return Results.Json(Array.Empty<object>(), _json);

            var list = player.CaughtPokemon.Select((p, i) => new
            {
                index       = i,
                pokeId      = p.Id,
                name        = p.Name,
                displayName = p.CustomName ?? p.Name,
                imageUrl    = p.ImageUrl ?? $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{p.Id}.png",
                types       = p.Types ?? new List<string>(),
                isShiny     = p.isShiny,
            });

            return Results.Json(list, _json);
        });

        // ── POST /api/tower/start ──────────────────────────────────────
        app.MapPost("/tower/start", async (HttpContext ctx, PokeTowerService towerSvc, PokeGameService pokeSvc) =>
        {
            var req = await ReadJson<StartRequest>(ctx);
            if (req == null
                || !ulong.TryParse(req.ChannelId, out var channelId)
                || !ulong.TryParse(req.UserId, out var userId))
                return Results.BadRequest("invalid request");

            var player = await pokeSvc.GetPlayerAsync(userId, req.UserName ?? "");
            if (player == null || player.CaughtPokemon.Count == 0)
                return Results.Problem("你還沒有抓到任何 Pokemon！請先玩 /pokemon 系統。", statusCode: 400);

            var idx = req.PokemonIndex;
            if (idx < 0 || idx >= player.CaughtPokemon.Count)
                return Results.BadRequest("invalid pokemonIndex");

            var src = player.CaughtPokemon[idx];
            await towerSvc.StartRunAsync(channelId, userId, req.UserName ?? player.UserName ?? "Player", src, req.PassiveId ?? "");

            var state = towerSvc.GetFrontendState(channelId);
            return Results.Json(state, _json);
        });

        // ── POST /api/tower/action ─────────────────────────────────────
        app.MapPost("/tower/action", async (HttpContext ctx, PokeTowerService svc) =>
        {
            var req = await ReadJson<ActionRequest>(ctx);
            if (req == null || !ulong.TryParse(req.ChannelId, out var cid))
                return Results.BadRequest("invalid request");

            if (!svc.HasActiveRun(cid))
                return Results.NotFound("no active run");

            var state = await svc.HandleApiActionAsync(cid, req.CustomId);
            return Results.Json(state, _json);
        });
    }

    private static async Task<T?> ReadJson<T>(HttpContext ctx)
    {
        try
        {
            using var r = new StreamReader(ctx.Request.Body);
            var body = await r.ReadToEndAsync();
            return JsonSerializer.Deserialize<T>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return default; }
    }

    private record ActionRequest(string ChannelId, string CustomId);
    private record StartRequest(string ChannelId, string UserId, string? UserName, int PokemonIndex, string? PassiveId);
}
