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
        // ── GET /api/tower/run/{channelId} ────────────────────────────
        app.MapGet("/api/tower/run/{channelId}", (string channelId, PokeTowerService svc) =>
        {
            if (!ulong.TryParse(channelId, out var cid))
                return Results.BadRequest("invalid channelId");

            var state = svc.GetFrontendState(cid);
            if (state == null) return Results.NotFound("no active run");

            return Results.Json(state, _json);
        });

        // ── POST /api/tower/start ─────────────────────────────────────
        // 注意：StartRunAsync 需要玩家的 Pokemon 隊伍，這裡先回傳 409 提示用 bot 指令開始
        app.MapPost("/api/tower/start", (HttpContext ctx, PokeTowerService svc) =>
        {
            return Results.Json(new
            {
                error = "請先在 Discord 頻道輸入 /pokemon爬塔 指令選擇隊伍，再從語音頻道啟動活動！"
            }, _json, statusCode: 409);
        });

        // ── POST /api/tower/action ────────────────────────────────────
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
}
