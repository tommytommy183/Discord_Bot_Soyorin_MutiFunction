import { DiscordSDK } from '@discord/embedded-app-sdk';

// 這個 CLIENT_ID 要填你的 Discord Application ID
export const CLIENT_ID = import.meta.env.VITE_DISCORD_CLIENT_ID ?? '';

export const discordSdk = new DiscordSDK(CLIENT_ID);

export interface DiscordUser {
  id: string;
  username: string;
  discriminator: string;
  avatar?: string;
  global_name?: string;
}

export async function initDiscord(): Promise<{ user: DiscordUser; channelId: string }> {
  await discordSdk.ready();

  // OAuth2 認證
  const { code } = await discordSdk.commands.authorize({
    client_id: CLIENT_ID,
    response_type: 'code',
    state: '',
    prompt: 'none',
    scope: ['identify', 'guilds'],
  });

  // 把 code 換 token（需要你的後端 /api/auth/token 端點）
  const tokenRes = await fetch('/api/auth/token', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code }),
  });
  const { access_token } = await tokenRes.json();

  await discordSdk.commands.authenticate({ access_token });

  // 取得目前使用者
  const userRes = await fetch('https://discord.com/api/users/@me', {
    headers: { Authorization: `Bearer ${access_token}` },
  });
  const user: DiscordUser = await userRes.json();

  const channelId = discordSdk.channelId ?? '';
  return { user, channelId };
}
