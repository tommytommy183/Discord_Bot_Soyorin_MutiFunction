import { DiscordSDK } from '@discord/embedded-app-sdk';

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

  const { code } = await discordSdk.commands.authorize({
    client_id: CLIENT_ID,
    response_type: 'code',
    state: '',
    prompt: 'none',
    scope: ['identify'],
  });

  const res = await fetch('/api/auth/token', {  // proxy strips /api → /auth/token on Northflank
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code }),
  });
  const { access_token } = await res.json();

  await discordSdk.commands.authenticate({ access_token });

  const userRes = await fetch('https://discord.com/api/users/@me', {
    headers: { Authorization: `Bearer ${access_token}` },
  });
  const user: DiscordUser = await userRes.json();

  const channelId = discordSdk.channelId ?? '';
  return { user, channelId };
}
