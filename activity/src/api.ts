import type { TowerRun, ActionRequest, ApiResponse } from './types';

// 生產環境用環境變數指定 bot API URL（例如 Northflank）
// 本機開發用 vite proxy，所以留空
const API_BASE = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

async function call<T>(path: string, method = 'GET', body?: unknown): Promise<ApiResponse<T>> {
  try {
    const res = await fetch(API_BASE + path, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: body ? JSON.stringify(body) : undefined,
    });
    if (!res.ok) {
      const text = await res.text();
      return { ok: false, error: text || res.statusText };
    }
    const data = await res.json() as T;
    return { ok: true, data };
  } catch (e) {
    return { ok: false, error: String(e) };
  }
}

export const api = {
  getRun:      (channelId: string) => call<TowerRun>(`/api/tower/run/${channelId}`),
  getPokemons: (userId: string)    => call<PokeListItem[]>(`/api/tower/pokemon/${userId}`),
  startRun:    (body: StartRunRequest) => call<TowerRun>('/api/tower/start', 'POST', body),
  action:      (req: ActionRequest)    => call<TowerRun>('/api/tower/action', 'POST', req),
  authToken:   (code: string, redirectUri: string) =>
    call<{ access_token: string }>('/api/auth/token', 'POST', { code, redirectUri }),
};

export interface PokeListItem {
  index: number;
  pokeId: number;
  name: string;
  displayName: string;
  imageUrl: string;
  types: string[];
  isShiny: boolean;
}

export interface StartRunRequest {
  channelId: string;
  userId: string;
  userName: string;
  pokemonIndex: number;
}
