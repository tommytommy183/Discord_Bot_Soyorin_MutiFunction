import { useEffect, useRef, useState, useCallback } from 'react';
import { initDiscord, type DiscordUser } from './discord';
import { api, type PokeListItem } from './api';
import type { TowerRun } from './types';
import { BattleScene } from './components/BattleScene';
import { PathSelector } from './components/PathSelector';
import { GenericChoices } from './components/GenericChoices';
import { GameOver } from './components/GameOver';

type Phase = 'loading' | 'select-pokemon' | 'game' | 'error';

function FloorDots({ current, max }: { current: number; max: number }) {
  return (
    <div style={{ display: 'flex', gap: 3, flexWrap: 'wrap', justifyContent: 'center' }}>
      {Array.from({ length: max }, (_, i) => i + 1).map(n => (
        <div key={n} style={{
          width: n % 10 === 0 ? 10 : 6, height: n % 10 === 0 ? 10 : 6,
          borderRadius: '50%',
          background: n <= current ? (n % 10 === 0 ? '#ef4444' : '#6366f1') : '#334155',
        }} />
      ))}
    </div>
  );
}

function PokemonSelector({ pokemons, onSelect, busy }: {
  pokemons: PokeListItem[]; onSelect: (idx: number) => void; busy: boolean;
}) {
  const [selected, setSelected] = useState<number | null>(null);
  return (
    <div style={{ padding: 20, display: 'flex', flexDirection: 'column', gap: 14, alignItems: 'center' }}>
      <div style={{ fontSize: 20, fontWeight: 900, color: '#fff' }}>選擇你的 Pokemon</div>
      {pokemons.length === 0 && (
        <div style={{ color: '#ef4444', fontSize: 13 }}>你還沒有 Pokemon！請先玩 /pokemon 系統。</div>
      )}
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, justifyContent: 'center' }}>
        {pokemons.map(p => (
          <button key={p.index} onClick={() => setSelected(p.index)} style={{
            background: selected === p.index ? '#4f46e5' : '#1e293b',
            border: `2px solid ${selected === p.index ? '#818cf8' : '#334155'}`,
            borderRadius: 10, padding: '8px 12px', cursor: 'pointer',
            display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 3, minWidth: 80,
          }}>
            <img src={p.imageUrl} alt={p.name}
              style={{ width: 48, height: 48, imageRendering: 'pixelated' }} />
            <div style={{ fontSize: 11, fontWeight: 700, color: '#fff' }}>{p.displayName || p.name}</div>
          </button>
        ))}
      </div>
      <button disabled={selected === null || busy} onClick={() => selected !== null && onSelect(selected)}
        style={{
          background: selected !== null ? '#6366f1' : '#374151',
          color: '#fff', border: 'none', borderRadius: 10,
          padding: '10px 28px', fontSize: 14, fontWeight: 700,
          cursor: selected !== null && !busy ? 'pointer' : 'not-allowed',
          opacity: selected !== null && !busy ? 1 : 0.5,
        }}>
        {busy ? '⏳ 開始中…' : '⚔️ 開始挑戰！'}
      </button>
    </div>
  );
}

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

  useEffect(() => {
    (async () => {
      try {
        const { user: u, channelId: cId } = await initDiscord();
        setUser(u);
        setChannelId(cId);

        // 有沒有已存在的 run？
        const runRes = await api.getRun(cId);
        if (runRes.ok && runRes.data) {
          setRun(runRes.data);
          setPhase('game');
          startPolling(cId);
        } else {
          // 沒有 run → 讓使用者選 Pokemon
          const pokeRes = await api.getPokemons(u.id);
          setPokemons(pokeRes.data ?? []);
          setPhase('select-pokemon');
        }
      } catch (e) {
        setError(String(e));
        setPhase('error');
      }
    })();
  }, [startPolling]);

  async function handleSelectPokemon(pokemonIndex: number) {
    if (!user || !channelId) return;
    setBusy(true);
    const res = await api.startRun({ channelId, userId: user.id, userName: user.global_name ?? user.username, pokemonIndex });
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

  async function handleAction(customId: string) {
    if (!channelId || busy) return;
    setBusy(true);
    const res = await api.action({ channelId, customId });
    if (res.ok && res.data) setRun(res.data);
    else setError(res.error ?? '操作失敗');
    setBusy(false);
  }

  if (phase === 'loading') return (
    <div style={center}><div style={{ fontSize: 36 }}>⚔️</div><div style={{ color: '#94a3b8', marginTop: 10 }}>載入中…</div></div>
  );
  if (phase === 'error') return (
    <div style={center}><div style={{ color: '#ef4444', fontSize: 13 }}>❌ {error}</div></div>
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
        padding: '6px 12px', background: '#0f172a', borderBottom: '1px solid #1e293b', flexShrink: 0,
      }}>
        <span style={{ color: '#fff', fontWeight: 700, fontSize: 13 }}>🗼 Pokemon 爬塔</span>
        <FloorDots current={run.currentFloor} max={run.maxFloor} />
        <span style={{ color: '#fbbf24', fontSize: 12 }}>💰{run.gold}</span>
      </div>
      <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        {run.state === 'InBattle' && <BattleScene run={run} onAction={handleAction} busy={busy} />}
        {run.state === 'SelectingPath' && <PathSelector run={run} onAction={handleAction} busy={busy} />}
        {!['InBattle', 'SelectingPath', 'Victory', 'Defeated'].includes(run.state) && (
          <GenericChoices run={run} onAction={handleAction} busy={busy} />
        )}
      </div>
      {busy && (
        <div style={{ position: 'absolute', inset: 0, background: 'rgba(0,0,0,0.4)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 22 }}>⏳</div>
      )}
    </div>
  );
}

const center: React.CSSProperties = { display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', height: '100vh', background: '#0f172a', color: '#fff' };
const shell: React.CSSProperties = { display: 'flex', flexDirection: 'column', height: '100vh', background: '#0f172a', color: '#fff', position: 'relative', overflow: 'hidden' };
