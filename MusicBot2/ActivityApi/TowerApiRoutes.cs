using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MusicBot2.Service;
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

    public static void Map(WebApplication app)
    {
        // ── GET /api/tower/run/{channelId} ─────────────────────────────
        app.MapGet("/api/tower/run/{channelId}", (string channelId, PokeTowerService svc) =>
        {
            if (!ulong.TryParse(channelId, out var cid))
                return Results.BadRequest("invalid channelId");

            var state = svc.GetFrontendState(cid);
            if (state == null) return Results.NotFound("no active run");
            return Results.Json(state, _json);
        });

        // ── GET /api/tower/pokemon/{userId} ────────────────────────────
        // 回傳玩家的 Pokemon 清單，供網頁選擇介面使用
        app.MapGet("/api/tower/pokemon/{userId}", async (string userId, PokeGameService pokeSvc) =>
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
        app.MapPost("/api/tower/start", async (HttpContext ctx, PokeTowerService towerSvc, PokeGameService pokeSvc) =>
        {
            var req = await ReadJson<StartRequest>(ctx);
            if (req == null
                || !ulong.TryParse(req.ChannelId, out var channelId)
                || !ulong.TryParse(req.UserId, out var userId))
                return Results.BadRequest("invalid request");

            var player = await pokeSvc.GetPlayerAsync(userId, req.UserName ?? "");
            if (player == null || player.CaughtPokemon.Count == 0)
                return Results.Problem("你還沒有抓到任何 Pokemon！", statusCode: 400);

            var idx = req.PokemonIndex;
            if (idx < 0 || idx >= player.CaughtPokemon.Count)
                return Results.BadRequest("invalid pokemonIndex");

            var src = player.CaughtPokemon[idx];
            var (_, _) = await towerSvc.StartRunAsync(channelId, userId, req.UserName ?? player.UserName ?? "Player", src);

            var state = towerSvc.GetFrontendState(channelId);
            return Results.Json(state, _json);
        });

        // ── POST /api/tower/action ─────────────────────────────────────
        app.MapPost("/api/tower/action", async (HttpContext ctx, PokeTowerService svc) =>
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
    private record StartRequest(string ChannelId, string UserId, string? UserName, int PokemonIndex);
}
