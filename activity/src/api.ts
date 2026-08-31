import type { TowerRun, ActionRequest, ApiResponse } from './types';

async function call<T>(path: string, method = 'GET', body?: unknown): Promise<ApiResponse<T>> {
  try {
    const res = await fetch(path, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: body ? JSON.stringify(body) : undefined,
    });
    if (!res.ok) return { ok: false, error: await res.text() || res.statusText };
    return { ok: true, data: await res.json() as T };
  } catch (e) {
    return { ok: false, error: String(e) };
  }
}

export interface PokeListItem {
  index: number; pokeId: number; name: string; displayName: string;
  imageUrl: string; types: string[]; isShiny: boolean;
}
export interface StartRunRequest {
  channelId: string; userId: string; userName: string; pokemonIndex: number;
}

export const api = {
  getRun:      (channelId: string) => call<TowerRun>(`/api/tower/run/${channelId}`),
  getPokemons: (userId: string)    => call<PokeListItem[]>(`/api/tower/pokemon/${userId}`),
  startRun:    (req: StartRunRequest) => call<TowerRun>('/api/tower/start', 'POST', req),
  action:      (req: ActionRequest)   => call<TowerRun>('/api/tower/action', 'POST', req),
};
