import { useEffect, useRef, useState, useCallback } from 'react';
import { api, type PokeListItem } from './api';
import type { TowerRun } from './types';
import { BattleScene } from './components/BattleScene';
import { PathSelector } from './components/PathSelector';
import { GenericChoices } from './components/GenericChoices';
import { GameOver } from './components/GameOver';
import { buildAuthUrl, exchangeCode, fetchUser, type DiscordUser } from './discord';

const REDIRECT_URI = import.meta.env.VITE_REDIRECT_URI ?? window.location.origin;

// ── Phase 定義 ──────────────────────────────────────────────────
type Phase = 'loading' | 'need-auth' | 'select-pokemon' | 'game' | 'no-run' | 'error';

// ── Floor progress dots ─────────────────────────────────────────
function FloorDots({ current, max }: { current: number; max: number }) {
  return (
    <div style={{ display: 'flex', gap: 3, flexWrap: 'wrap', justifyContent: 'center' }}>
      {Array.from({ length: max }, (_, i) => i + 1).map(n => (
        <div key={n} style={{
          width: n % 10 === 0 ? 10 : 6, height: n % 10 === 0 ? 10 : 6,
          borderRadius: '50%',
          background: n <= current ? (n % 10 === 0 ? '#ef4444' : '#6366f1') : '#334155',
          transition: 'background 0.3s',
        }} />
      ))}
    </div>
  );
}

// ── Pokemon 選擇畫面 ────────────────────────────────────────────
function PokemonSelector({ pokemons, onSelect, busy }: {
  pokemons: PokeListItem[];
  onSelect: (idx: number) => void;
  busy: boolean;
}) {
  const [selected, setSelected] = useState<number | null>(null);

  return (
    <div style={{ padding: 24, display: 'flex', flexDirection: 'column', gap: 16, alignItems: 'center' }}>
      <div style={{ fontSize: 22, fontWeight: 900, color: '#fff' }}>選擇你的 Pokemon</div>
      <div style={{ color: '#94a3b8', fontSize: 13 }}>選一隻出戰，挑戰爬塔！</div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, justifyContent: 'center', maxWidth: 400 }}>
        {pokemons.map(p => (
          <button key={p.index} onClick={() => setSelected(p.index)} style={{
            background: selected === p.index ? '#4f46e5' : '#1e293b',
            border: selected === p.index ? '2px solid #818cf8' : '2px solid #334155',
            borderRadius: 12, padding: '10px 14px', cursor: 'pointer',
            display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4,
            minWidth: 90, transition: 'all 0.15s',
          }}>
            <img src={p.imageUrl || `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/${p.pokeId}.png`}
              alt={p.name} style={{ width: 56, height: 56, imageRendering: 'pixelated' }} />
            <div style={{ fontSize: 12, fontWeight: 700, color: '#fff' }}>{p.displayName || p.name}</div>
            <div style={{ display: 'flex', gap: 3 }}>
              {p.types.map(t => (
                <span key={t} style={{ fontSize: 10, background: '#334155', color: '#94a3b8', borderRadius: 4, padding: '1px 5px' }}>{t}</span>
              ))}
            </div>
          </button>
        ))}
      </div>
      {pokemons.length === 0 && (
        <div style={{ color: '#ef4444', fontSize: 13 }}>你還沒有抓到任何 Pokemon！請先玩 /pokemon 系統。</div>
      )}
      <button
        disabled={selected === null || busy}
        onClick={() => selected !== null && onSelect(selected)}
        style={{
          background: selected !== null ? '#6366f1' : '#374151',
          color: '#fff', border: 'none', borderRadius: 12,
          padding: '12px 36px', fontSize: 15, fontWeight: 700,
          cursor: selected !== null && !busy ? 'pointer' : 'not-allowed',
          opacity: selected !== null && !busy ? 1 : 0.5,
          marginTop: 8,
        }}
      >
        {busy ? '⏳ 開始中…' : '⚔️ 開始挑戰！'}
      </button>
    </div>
  );
}

// ── Main App ────────────────────────────────────────────────────
export default function App() {
  const [phase, setPhase]         = useState<Phase>('loading');
  const [run, setRun]             = useState<TowerRun | null>(null);
  const [busy, setBusy]           = useState(false);
  const [error, setError]         = useState('');
  const [channelId, setChannelId] = useState('');
  const [user, setUser]           = useState<DiscordUser | null>(null);
  const [pokemons, setPokemons]   = useState<PokeListItem[]>([]);
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const startPolling = useCallback((cId: string) => {
    if (pollRef.current) clearInterval(pollRef.current);
    pollRef.current = setInterval(async () => {
      const res = await api.getRun(cId);
      if (res.ok && res.data) {
        setRun(res.data);
        if (res.data.state === 'Victory' || res.data.state === 'Defeated')
          clearInterval(pollRef.current!);
      }
    }, 3000);
  }, []);

  useEffect(() => () => { if (pollRef.current) clearInterval(pollRef.current); }, []);

  // ── 初始化 ──────────────────────────────────────────────────
  useEffect(() => {
    (async () => {
      const params = new URLSearchParams(window.location.search);
      const code    = params.get('code');
      const state   = params.get('state');   // channelId（OAuth state）
      const channel = params.get('channel');

      // ① OAuth callback: ?code=...&state=channelId
      if (code && state) {
        window.history.replaceState({}, '', `?channel=${state}`);
        try {
          const { access_token } = (await api.authToken(code, REDIRECT_URI)).data ?? {};
          if (!access_token) throw new Error('無法取得 access token');
          const discordUser = await fetchUser(access_token);
          setUser(discordUser);
          setChannelId(state);

          // 有沒有正在進行的 run？
          const runRes = await api.getRun(state);
          if (runRes.ok && runRes.data) {
            setRun(runRes.data);
            setPhase('game');
            startPolling(state);
          } else {
            // 沒有 run → 讓使用者選 Pokemon 開始
            const pokeRes = await api.getPokemons(discordUser.id);
            setPokemons(pokeRes.data ?? []);
            setPhase('select-pokemon');
          }
        } catch (e) {
          setError(String(e));
          setPhase('error');
        }
        return;
      }

      // ② 直接帶 channel（重新整理或分享連結）
      if (channel) {
        setChannelId(channel);
        const runRes = await api.getRun(channel);
        if (runRes.ok && runRes.data) {
          setRun(runRes.data);
          // 還是要先登入
          setPhase('need-auth');
        } else {
          setPhase('need-auth');
        }
        return;
      }

      // ③ 什麼都沒有
      setPhase('no-run');
    })();
  }, [startPolling]);

  // ── 選 Pokemon 後開始遊戲 ────────────────────────────────────
  async function handleSelectPokemon(pokemonIndex: number) {
    if (!user || !channelId) return;
    setBusy(true);
    const res = await api.startRun({
      channelId, userId: user.id,
      userName: user.global_name ?? user.username,
      pokemonIndex,
    });
    if (res.ok && res.data) {
      setRun(res.data);
      setPhase('game');
      startPolling(channelId);
    } else {
      setError(res.error ?? '開始失敗');
      setPhase('error');
    }
    setBusy(false);
  }

  // ── 執行遊戲動作 ─────────────────────────────────────────────
  async function handleAction(customId: string) {
    if (!channelId || busy) return;
    setBusy(true);
    const res = await api.action({ channelId, customId });
    if (res.ok && res.data) setRun(res.data);
    else setError(res.error ?? '操作失敗');
    setBusy(false);
  }

  // ── Render ──────────────────────────────────────────────────
  if (phase === 'loading') return (
    <div style={center}><div style={{ fontSize: 40 }}>⚔️</div><div style={{ color: '#94a3b8', marginTop: 12 }}>載入中…</div></div>
  );

  if (phase === 'error') return (
    <div style={center}><div style={{ color: '#ef4444', fontSize: 14 }}>❌ {error}</div></div>
  );

  if (phase === 'no-run') return (
    <div style={{ ...center, gap: 20, padding: 32, textAlign: 'center' }}>
      <div style={{ fontSize: 56 }}>🗼</div>
      <div style={{ fontSize: 24, fontWeight: 900, color: '#fff' }}>Pokemon 爬塔</div>
      <div style={{ color: '#94a3b8', fontSize: 14, maxWidth: 280 }}>
        請在 Discord 頻道輸入 <code style={{ background: '#1e293b', padding: '2px 6px', borderRadius: 4 }}>/pokemon爬塔</code><br />
        點 Bot 給的按鈕進入遊戲。
      </div>
    </div>
  );

  if (phase === 'need-auth') return (
    <div style={{ ...center, gap: 20, padding: 32, textAlign: 'center' }}>
      <div style={{ fontSize: 56 }}>🗼</div>
      <div style={{ fontSize: 22, fontWeight: 900, color: '#fff' }}>Pokemon 爬塔</div>
      <a
        href={buildAuthUrl(channelId)}
        style={{
          background: 'linear-gradient(135deg,#5865f2,#7289da)',
          color: '#fff', textDecoration: 'none', borderRadius: 12,
          padding: '14px 32px', fontSize: 16, fontWeight: 700, display: 'inline-block',
        }}
      >
        🔑 用 Discord 登入
      </a>
    </div>
  );

  if (phase === 'select-pokemon') return (
    <div style={{ ...shell, overflow: 'auto' }}>
      <PokemonSelector pokemons={pokemons} onSelect={handleSelectPokemon} busy={busy} />
    </div>
  );

  if (!run) return null;

  if (run.state === 'Victory' || run.state === 'Defeated') return (
    <div style={shell}>
      <GameOver run={run} onRestart={() => { setRun(null); setPhase('select-pokemon'); }} />
    </div>
  );

  return (
    <div style={shell}>
      <div style={{
        display: 'flex', justifyContent: 'space-between', alignItems: 'center',
        padding: '8px 12px', background: '#0f172a', borderBottom: '1px solid #1e293b', flexShrink: 0,
      }}>
        <span style={{ color: '#fff', fontWeight: 700, fontSize: 14 }}>🗼 Pokemon 爬塔</span>
        <FloorDots current={run.currentFloor} max={run.maxFloor} />
        <span style={{ color: '#fbbf24', fontSize: 13 }}>💰{run.gold}</span>
      </div>

      <div style={{ flex: 1, overflow: 'auto', padding: 14 }}>
        {run.state === 'InBattle' && <BattleScene run={run} onAction={handleAction} busy={busy} />}
        {run.state === 'SelectingPath' && <PathSelector run={run} onAction={handleAction} busy={busy} />}
        {!['InBattle', 'SelectingPath', 'Victory', 'Defeated'].includes(run.state) && (
          <GenericChoices run={run} onAction={handleAction} busy={busy} />
        )}
      </div>

      {busy && (
        <div style={{ position: 'absolute', inset: 0, background: 'rgba(0,0,0,0.4)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 24 }}>
          ⏳
        </div>
      )}
    </div>
  );
}

const center: React.CSSProperties = {
  display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
  height: '100vh', background: '#0f172a', color: '#fff',
};
const shell: React.CSSProperties = {
  display: 'flex', flexDirection: 'column', height: '100vh',
  background: '#0f172a', color: '#fff', position: 'relative', overflow: 'hidden',
};
