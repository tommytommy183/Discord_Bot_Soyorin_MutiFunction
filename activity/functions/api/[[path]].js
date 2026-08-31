/**
 * Cloudflare Pages Function — 把 /api/* 代理到 bot API（Northflank）
 * 環境變數 BOT_API_URL 需在 CF Pages → Settings → Environment variables 設定
 */
export async function onRequest(context) {
  const botApiUrl = context.env.BOT_API_URL ?? 'https://p01--soyorin--p2mfw9lq5dgw.code.run';
  const url = new URL(context.request.url);
  const target = `${botApiUrl}${url.pathname}${url.search}`;

  const req = new Request(target, {
    method:  context.request.method,
    headers: context.request.headers,
    body:    ['GET', 'HEAD'].includes(context.request.method) ? undefined : context.request.body,
  });

  const resp = await fetch(req);

  // 確保 CORS header 都在
  const headers = new Headers(resp.headers);
  headers.set('Access-Control-Allow-Origin', '*');
  headers.set('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  headers.set('Access-Control-Allow-Headers', 'Content-Type');

  if (context.request.method === 'OPTIONS') {
    return new Response(null, { status: 204, headers });
  }

  return new Response(resp.body, { status: resp.status, headers });
}
