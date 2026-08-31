// 標準 Discord OAuth2 helper（不使用 Embedded App SDK）

export const CLIENT_ID = import.meta.env.VITE_DISCORD_CLIENT_ID ?? '';
export const REDIRECT_URI = import.meta.env.VITE_REDIRECT_URI ?? window.location.origin;

export interface DiscordUser {
  id: string;
  username: string;
  discriminator: string;
  avatar?: string;
  global_name?: string;
}

/** 建立 Discord OAuth2 授權 URL */
export function buildAuthUrl(channelId: string): string {
  const params = new URLSearchParams({
    client_id:     CLIENT_ID,
    redirect_uri:  REDIRECT_URI,
    response_type: 'code',
    scope:         'identify',
    state:         channelId,
  });
  return `https://discord.com/oauth2/authorize?${params}`;
}

/** 用 code 換 access_token（呼叫 bot API） */
export async function exchangeCode(code: string): Promise<string> {
  const res = await fetch('/api/auth/token', {
    method:  'POST',
    headers: { 'Content-Type': 'application/json' },
    body:    JSON.stringify({ code, redirectUri: REDIRECT_URI }),
  });
  if (!res.ok) throw new Error(`token exchange failed: ${await res.text()}`);
  const { access_token } = await res.json();
  return access_token as string;
}

/** 取得目前 Discord 使用者 */
export async function fetchUser(accessToken: string): Promise<DiscordUser> {
  const res = await fetch('https://discord.com/api/users/@me', {
    headers: { Authorization: `Bearer ${accessToken}` },
  });
  return res.json();
}
