import { useEffect, useRef, useState, useCallback } from 'react';
import { initDiscord, type DiscordUser } from './discord';
import { api, type PokeListItem } from './api';
import { spriteUrl } from './utils';
import type { TowerRun } from './types';
import { BattleScene } from './components/BattleScene';
import { PathSelector } from './components/PathSelector';
import { GenericChoices } from './components/GenericChoices';
import { GameOver } from './components/GameOver';
import { HpBar } from './components/HpBar';
import { TypeBadge } from './components/TypeBadge';

type Phase = 'loading' | 'select-pokemon' | 'game' | 'error';

// ── Loading Screen ──────────────────────────────────────────────────────────
function LoadingScreen({ message }: { message: string }) {
  return (
    <div style={fullCenter}>
      <div style={{ fontSize: 56, animation: 'bounce 1.5s ease-in-out infinite' }}>⚔️</div>
      <div style={{
        fontFamily: "'Press Start 2P', monospace",
        fontSize: 11, color: '#6366f1', marginTop: 20, letterSpacing: '0.1em',
        animation: 'pulse 1.5s ease-in-out infinite',
      }}>POKEMON 爬塔</div>
      <div style={{ color: '#475569', fontSize: 12, marginTop: 12 }}>{message}</div>
      <div style={{ display: 'flex', gap: 6, marginTop: 16 }}>
        {[0, 1, 2].map(i => (
          <div key={i} style={{
            width: 8, height: 8, borderRadius: '50%', background: '#6366f1',
            animation: `pulse 1.2s ease-in-out ${i * 0.2}s infinite`,
          }} />
        ))}
      </div>
    </div>
  );
}

// ── Pokemon Selector ─────────────────────────────────────────────────────────
function PokemonSelector({ pokemons, onSelect, busy }: {
  pokemons: PokeListItem[];
  onSelect: (idx: number) => void;
  busy: boolean;
}) {
  const [selected, setSelected] = useState<number | null>(null);
  const selPoke = pokemons.find(p => p.index === selected);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      {/* Header */}
      <div style={{
        padding: '16px 20px 12px',
        background: 'linear-gradient(180deg, #1a1f35 0%, #0a0e1a 100%)',
        borderBottom: '1px solid #1e293b', flexShrink: 0,
      }}>
        <div style={{
          fontFamily: "'Press Start 2P', monospace",
          fontSize: 10, color: '#6366f1', letterSpacing: '0.1em', marginBottom: 6,
        }}>POKEMON 爬塔</div>
        <div style={{ fontSize: 16, fontWeight: 900, color: '#fff' }}>選擇出戰的 Pokemon</div>
        <div style={{ fontSize: 12, color: '#475569', marginTop: 4 }}>挑戰 {20} 層的塔樓！</div>
      </div>

      {pokemons.length === 0 ? (
        <div style={{ flex: 1, ...fullCenter, gap: 12 }}>
          <div style={{ fontSize: 40 }}>😢</div>
          <div style={{ color: '#ef4444', fontSize: 13, textAlign: 'center' }}>
            你還沒有 Pokemon！<br />請先玩 /pokemon 系統捕捉寶可夢。
          </div>
        </div>
      ) : (
        <div style={{ flex: 1, overflow: 'auto', padding: '12px 16px', display: 'flex', flexDirection: 'column', gap: 12 }}>
          {/* Pokemon grid */}
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
            {pokemons.map(p => {
              const isSel = selected === p.index;
              return (
                <button
                  key={p.index}
                  className="btn-hover"
                  onClick={() => setSelected(p.index)}
                  style={{
                    background: isSel ? 'linear-gradient(135deg, #4f46e5, #6366f1)' : '#0f172a',
                    border: `2px solid ${isSel ? '#818cf8' : '#1e293b'}`,
                    borderRadius: 12, padding: '10px 14px', cursor: 'pointer',
                    display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6,
                    minWidth: 80, flex: '1 1 80px', maxWidth: 110,
                    boxShadow: isSel ? '0 4px 16px #6366f155' : undefined,
                  }}
                >
                  <div style={{ position: 'relative' }}>
                    <img
                      src={p.isShiny ? spriteUrl(p.pokeId, 'shiny') : spriteUrl(p.pokeId)}
                      alt={p.name}
                      style={{
                        width: 52, height: 52, imageRendering: 'pixelated',
                        filter: isSel ? 'drop-shadow(0 0 8px #818cf8)' : undefined,
                        animation: isSel ? 'bounce 1.5s ease-in-out infinite' : undefined,
                      }}
                    />
                    {p.isShiny && (
                      <span style={{ position: 'absolute', top: -4, right: -4, fontSize: 12 }}>✨</span>
                    )}
                  </div>
                  <div style={{
                    fontSize: 10, fontWeight: 700, color: isSel ? '#e0e7ff' : '#94a3b8',
                    textAlign: 'center', lineHeight: 1.3,
                    maxWidth: 80, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                  }}>
                    {p.displayName || p.name}
                  </div>
                  <div style={{ display: 'flex', gap: 3, flexWrap: 'wrap', justifyContent: 'center' }}>
                    {p.types.map(t => <TypeBadge key={t} type={t} />)}
                  </div>
                </button>
              );
            })}
          </div>

          {/* Selected preview */}
          {selPoke && (
            <div className="anim-fade-in" style={{
              background: '#0f172a', borderRadius: 12, padding: 14,
              border: '1px solid #4f46e5',
            }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                <img
                  src={selPoke.isShiny ? spriteUrl(selPoke.pokeId, 'shiny') : spriteUrl(selPoke.pokeId)}
                  alt={selPoke.name}
                  style={{ width: 72, height: 72, imageRendering: 'pixelated', animation: 'bounce 1.8s ease-in-out infinite' }}
                />
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 900, fontSize: 17, color: '#fff' }}>
                    {selPoke.displayName || selPoke.name}
                    {selPoke.isShiny && <span style={{ marginLeft: 6 }}>✨</span>}
                  </div>
                  <div style={{ display: 'flex', gap: 4, marginTop: 4 }}>
                    {selPoke.types.map(t => <TypeBadge key={t} type={t} />)}
                  </div>
                  <div style={{ fontSize: 11, color: '#475569', marginTop: 6 }}>準備好了嗎？</div>
                </div>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Confirm button */}
      <div style={{ padding: '12px 16px', borderTop: '1px solid #1e293b', flexShrink: 0 }}>
        <button
          className="btn-hover"
          disabled={selected === null || busy}
          onClick={() => selected !== null && onSelect(selected)}
          style={{
            width: '100%',
            background: selected !== null
              ? 'linear-gradient(135deg, #6366f1, #a855f7)'
              : '#1e293b',
            color: '#fff', border: 'none', borderRadius: 12,
            padding: '14px', fontSize: 15, fontWeight: 700,
            cursor: selected !== null && !busy ? 'pointer' : 'not-allowed',
            opacity: selected !== null && !busy ? 1 : 0.4,
            boxShadow: selected !== null ? '0 4px 20px #6366f155' : undefined,
          }}
        >
          {busy ? '⏳ 開始中…' : selected !== null ? '⚔️ 出發挑戰！' : '👆 選擇一隻 Pokemon'}
        </button>
      </div>
    </div>
  );
}

// ── Header bar ─────────────────────────────────────────────────────────────
function GameHeader({ run }: { run: TowerRun }) {
  const progress = run.currentFloor / run.maxFloor;
  return (
    <div style={{
      display: 'flex', alignItems: 'center', gap: 10,
      padding: '6px 14px', background: '#07090f',
      borderBottom: '1px solid #1e293b', flexShrink: 0,
    }}>
      <span style={{
        fontFamily: "'Press Start 2P', monospace",
        fontSize: 8, color: '#6366f1', letterSpacing: '0.05em', flexShrink: 0,
      }}>POKE<br />TOWER</span>
      {/* Progress mini-bar */}
      <div style={{ flex: 1, display: 'flex', alignItems: 'center', flexDirection: 'column', gap: 2 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 9, color: '#475569' }}>
          <span>Floor</span><span>{run.currentFloor}/{run.maxFloor}</span>
        </div>
        <div style={{ height: 4, background: '#1e293b', borderRadius: 2, overflow: 'hidden' }}>
          <div style={{
            height: '100%', width: `${progress * 100}%`,
            background: 'linear-gradient(90deg, #6366f1, #a855f7)',
            borderRadius: 2, transition: 'width 0.5s',
          }} />
        </div>
      </div>
      {/* Gold */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 4,
        background: '#1a1400', border: '1px solid #3d2e00',
        borderRadius: 6, padding: '4px 8px', flexShrink: 0,
      }}>
        <span style={{ fontSize: 12 }}>💰</span>
        <span style={{ fontWeight: 700, color: '#fbbf24', fontSize: 12 }}>{run.gold}</span>
      </div>
    </div>
  );
}

// ── Main App ───────────────────────────────────────────────────────────────
export default function App() {
  const [phase, setPhase]         = useState<Phase>('loading');
  const [loadMsg, setLoadMsg]     = useState('初始化中…');
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
        setLoadMsg('連接 Discord…');
        const { user: u, channelId: cId } = await initDiscord();
        setUser(u);
        setChannelId(cId);

        setLoadMsg('載入遊戲資料…');
        const runRes = await api.getRun(cId);
        if (runRes.ok && runRes.data) {
          setRun(runRes.data);
          setPhase('game');
          startPolling(cId);
        } else {
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
    const res = await api.startRun({
      channelId,
      userId: user.id,
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

  async function handleAction(customId: string) {
    if (!channelId || busy) return;
    setBusy(true);
    const res = await api.action({ channelId, customId });
    if (res.ok && res.data) setRun(res.data);
    else { setError(res.error ?? '操作失敗'); }
    setBusy(false);
  }

  // ── Render ──────────────────────────────────────────────────────────────
  if (phase === 'loading') return <LoadingScreen message={loadMsg} />;

  if (phase === 'error') return (
    <div style={{ ...fullCenter, gap: 16, padding: 24 }}>
      <div style={{ fontSize: 40 }}>❌</div>
      <div style={{ color: '#ef4444', fontSize: 12, textAlign: 'center', lineHeight: 1.6, maxWidth: 320 }}>
        {error}
      </div>
      <button
        className="btn-hover"
        onClick={() => window.location.reload()}
        style={{
          background: '#1e293b', color: '#94a3b8', border: '1px solid #334155',
          borderRadius: 8, padding: '10px 20px', fontSize: 13, cursor: 'pointer',
        }}
      >
        🔄 重試
      </button>
    </div>
  );

  if (phase === 'select-pokemon') return (
    <div style={shell}>
      <PokemonSelector pokemons={pokemons} onSelect={handleSelectPokemon} busy={busy} />
    </div>
  );

  if (!run) return null;

  if (run.state === 'Victory' || run.state === 'Defeated') return (
    <div style={{ ...shell, overflow: 'auto' }}>
      <GameOver run={run} onRestart={() => { setRun(null); setPhase('select-pokemon'); }} />
    </div>
  );

  return (
    <div style={shell}>
      <GameHeader run={run} />
      <div style={{ flex: 1, overflow: 'auto', padding: '10px 12px' }}>
        {run.state === 'InBattle' && (
          <BattleScene run={run} onAction={handleAction} busy={busy} />
        )}
        {run.state === 'SelectingPath' && (
          <PathSelector run={run} onAction={handleAction} busy={busy} />
        )}
        {!['InBattle', 'SelectingPath', 'Victory', 'Defeated'].includes(run.state) && (
          <GenericChoices run={run} onAction={handleAction} busy={busy} />
        )}
      </div>

      {/* Busy overlay */}
      {busy && (
        <div style={{
          position: 'absolute', inset: 0,
          background: 'rgba(0,0,0,0.5)',
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          backdropFilter: 'blur(2px)',
        }}>
          <div style={{
            background: '#0f172a', borderRadius: 16, padding: '20px 32px',
            border: '1px solid #334155',
            display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12,
          }}>
            <div style={{ fontSize: 28, animation: 'spin 1s linear infinite' }}>⚙️</div>
            <div style={{ color: '#94a3b8', fontSize: 13 }}>處理中…</div>
          </div>
        </div>
      )}
    </div>
  );
}

const fullCenter: React.CSSProperties = {
  display: 'flex', flexDirection: 'column', alignItems: 'center',
  justifyContent: 'center', height: '100vh',
  background: '#0a0e1a', color: '#e2e8f0',
};
const shell: React.CSSProperties = {
  display: 'flex', flexDirection: 'column', height: '100vh',
  background: '#0a0e1a', color: '#e2e8f0',
  position: 'relative', overflow: 'hidden',
};
