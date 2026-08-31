import type { TowerRun, ActionRequest, ApiResponse } from './types';

const BASE = '/api/tower';

async function call<T>(path: string, method = 'GET', body?: unknown): Promise<ApiResponse<T>> {
  try {
    const res = await fetch(BASE + path, {
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
  getRun:   (channelId: string) => call<TowerRun>(`/run/${channelId}`),
  startRun: (channelId: string, userId: string, userName: string) =>
    call<TowerRun>('/start', 'POST', { channelId, userId, userName }),
  action:   (req: ActionRequest) => call<TowerRun>('/action', 'POST', req),
};
