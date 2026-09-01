import { useState, useEffect, useRef } from 'react';
import { HpBar } from './HpBar';
import { StatusBadge } from './StatusBadge';
import { TypeBadge } from './TypeBadge';
import { spriteUrl, typeColor } from '../utils';
import type { TowerRun, TowerMove, TowerEnemy } from '../types';

interface Props {
  run: TowerRun;
  onAction: (customId: string) => void;
  busy: boolean;
}

function StageLabel({ stage }: { stage: number }) {
  if (stage === 0) return null;
  return (
    <span style={{ color: stage > 0 ? '#4ade80' : '#f87171', fontSize: 10, fontWeight: 700, marginLeft: 2 }}>
      {stage > 0 ? `▲${stage}` : `▼${Math.abs(stage)}`}
    </span>
  );
}

function MoveBtn({ move, idx, channelId, onAction, busy, onAttack }: {
  move: TowerMove; idx: number; channelId: string;
  onAction: (id: string) => void; busy: boolean;
  onAttack: () => void;
}) {
  const color = typeColor(move.type);
  const empty = move.currentPP === 0;
  const customId = `tower_move_${channelId}_${idx}`;
  const ppRatio = move.maxPP > 0 ? move.currentPP / move.maxPP : 0;
  const ppColor = ppRatio === 0 ? '#ef4444' : ppRatio <= 0.25 ? '#facc15' : '#94a3b8';

  return (
    <button
      className="btn-hover"
      disabled={busy || empty}
      onClick={() => { if (!busy && !empty) { onAttack(); onAction(customId); } }}
      style={{
        background: empty
          ? '#1a1f2e'
          : `linear-gradient(135deg, ${color}44 0%, ${color}22 100%)`,
        color: empty ? '#475569' : '#fff',
        border: `1px solid ${empty ? '#334155' : color + '66'}`,
        borderRadius: 10,
        padding: '8px 10px',
        cursor: busy || empty ? 'not-allowed' : 'pointer',
        opacity: busy || empty ? 0.55 : 1,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 2,
        flex: '1 1 calc(50% - 4px)',
        minWidth: 0,
        position: 'relative',
        overflow: 'hidden',
      }}
    >
      {/* Type color bar */}
      {!empty && (
        <div style={{
          position: 'absolute', top: 0, left: 0, right: 0, height: 3,
          background: color, borderRadius: '10px 10px 0 0',
        }} />
      )}
      <span style={{ fontSize: 18, lineHeight: 1, marginTop: 2 }}>{move.emoji}</span>
      <span style={{ fontWeight: 700, fontSize: 11, textAlign: 'center', lineHeight: 1.2 }}>{move.name}</span>
      <div style={{ display: 'flex', alignItems: 'center', gap: 3, fontSize: 9 }}>
        <span style={{ color: '#64748b' }}>
          {move.category === 'physical' ? '物攻' : move.category === 'special' ? '特攻' : '變化'}
          {move.power > 0 && ` ${move.power}`}
        </span>
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 3, fontSize: 9, color: ppColor }}>
        <span>PP</span>
        <span style={{ fontWeight: 700 }}>{move.currentPP}</span>
        <span style={{ opacity: 0.5 }}>/{move.maxPP}</span>
      </div>
    </button>
  );
}

function BattleLog({ logs }: { logs: string[] }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (ref.current) ref.current.scrollTop = ref.current.scrollHeight;
  }, [logs]);

  return (
    <div ref={ref} style={{
      background: '#07090f', borderRadius: 8,
      padding: '8px 12px', fontSize: 11, color: '#94a3b8',
      height: 72, overflowY: 'auto',
      border: '1px solid #1e293b', lineHeight: 1.5,
    }}>
      {logs.slice(-10).map((log, i, arr) => (
        <div key={i} style={{
          opacity: 0.4 + (i / arr.length) * 0.6,
          color: i === arr.length - 1 ? '#e2e8f0' : '#94a3b8',
          fontWeight: i === arr.length - 1 ? 600 : 400,
        }}>
          {log}
        </div>
      ))}
    </div>
  );
}

export function BattleScene({ run, onAction, busy }: Props) {
  const activePoke = run.team[run.activeIndex];
  const enemy = run.currentEnemy;
  const isBoss = enemy?.isBoss ?? false;
  const [shake, setShake] = useState(false);

  function handleAttack() {
    setShake(true);
    setTimeout(() => setShake(false), 500);
  }

  const bgGradient = isBoss
    ? 'radial-gradient(ellipse at 60% 40%, #2d0a0a 0%, #0a0e1a 100%)'
    : 'radial-gradient(ellipse at 60% 40%, #0d1e30 0%, #0a0e1a 100%)';

  return (
    <div className="anim-fade-in" style={{ display: 'flex', flexDirection: 'column', gap: 8, height: '100%' }}>
      {/* Floor / Boss title */}
      <div style={{ textAlign: 'center', paddingTop: 2 }}>
        {isBoss && (
          <div style={{
            fontFamily: "'Press Start 2P', monospace",
            fontSize: 10, color: '#ef4444', letterSpacing: '0.12em',
            animation: 'pulse 1s ease-in-out infinite', marginBottom: 2,
          }}>⚠ BOSS BATTLE ⚠</div>
        )}
        <div style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>
          第 <span style={{ color: '#e2e8f0', fontWeight: 900 }}>{run.currentFloor}</span> 層
        </div>
      </div>

      {/* ── Battle Arena: classic Pokemon layout ─────────────────────── */}
      <div style={{
        position: 'relative',
        background: bgGradient,
        borderRadius: 14, padding: '10px 12px',
        border: isBoss ? '1px solid #7f1d1d' : '1px solid #1e2d45',
        minHeight: 170, overflow: 'hidden',
      }}>
        {/* Enemy: top-right */}
        {enemy && (
          <div style={{ position: 'absolute', top: 8, right: 10, display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 3 }}>
            {/* Enemy info box */}
            <div style={{
              background: 'rgba(0,0,0,0.55)', borderRadius: 8, padding: '5px 10px',
              border: isBoss ? '1px solid #ef444455' : '1px solid #1e293b',
              minWidth: 140, backdropFilter: 'blur(4px)',
            }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 5, marginBottom: 2 }}>
                {isBoss && <span style={{ color: '#ef4444', fontSize: 9, fontWeight: 700, fontFamily: "'Press Start 2P', monospace" }}>BOSS</span>}
                <span style={{ fontWeight: 900, fontSize: 13, color: '#fff' }}>{enemy.name}</span>
                {enemy.battleStatus && <StatusBadge status={enemy.battleStatus} />}
              </div>
              <div style={{ display: 'flex', gap: 3, marginBottom: 4 }}>
                {enemy.types.map(t => <TypeBadge key={t} type={t} />)}
              </div>
              <HpBar current={enemy.currentHP} max={enemy.maxHP} label="HP" />
              <div style={{ fontSize: 9, color: '#475569', marginTop: 3, display: 'flex', gap: 6 }}>
                <span>ATK<StageLabel stage={enemy.atkStage} /></span>
                <span>DEF<StageLabel stage={enemy.defStage} /></span>
                <span>SPD<StageLabel stage={enemy.spdStage} /></span>
                {enemy.goldReward > 0 && <span style={{ color: '#fbbf24' }}>💰{enemy.goldReward}</span>}
              </div>
            </div>
            {/* Enemy sprite */}
            <img
              src={spriteUrl(enemy.pokeId, 'front')}
              alt={enemy.name}
              style={{
                imageRendering: 'pixelated', width: 88, height: 88,
                filter: enemy.currentHP === 0
                  ? 'grayscale(1) opacity(0.3)'
                  : isBoss ? 'drop-shadow(0 0 12px #ef4444)' : 'drop-shadow(0 4px 10px rgba(0,0,0,0.7))',
                animation: enemy.currentHP > 0
                  ? (shake ? 'shake 0.4s ease-in-out' : isBoss ? 'bossGlow 1.5s ease-in-out infinite' : 'bounce 2s ease-in-out infinite')
                  : undefined,
              }}
            />
          </div>
        )}

        {/* Player: bottom-left */}
        {activePoke && (
          <div style={{ position: 'absolute', bottom: 8, left: 10, display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: 3 }}>
            {/* Player sprite */}
            <img
              src={activePoke.isShiny ? spriteUrl(activePoke.pokeId, 'shiny') : spriteUrl(activePoke.pokeId, 'back')}
              alt={activePoke.name}
              style={{
                imageRendering: 'pixelated', width: 100, height: 100,
                filter: activePoke.currentHP === 0
                  ? 'grayscale(1) opacity(0.3)'
                  : 'drop-shadow(0 4px 12px rgba(99,102,241,0.5))',
                transform: 'scaleX(-1)',
                animation: activePoke.currentHP > 0 ? 'bounce 2.2s ease-in-out infinite' : undefined,
              }}
            />
            {/* Player info box */}
            <div style={{
              background: 'rgba(0,0,0,0.55)', borderRadius: 8, padding: '5px 10px',
              border: '1px solid #1e3a5f', minWidth: 140, backdropFilter: 'blur(4px)',
            }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 5, marginBottom: 2 }}>
                <span style={{ fontWeight: 900, fontSize: 13, color: '#fff' }}>{activePoke.displayName}</span>
                {activePoke.isShiny && <span>✨</span>}
                {activePoke.battleStatus && <StatusBadge status={activePoke.battleStatus} />}
              </div>
              <div style={{ display: 'flex', gap: 3, marginBottom: 4 }}>
                {activePoke.types.map(t => <TypeBadge key={t} type={t} />)}
              </div>
              <HpBar current={activePoke.currentHP} max={activePoke.maxHP} label="HP" />
              <div style={{ fontSize: 9, color: '#475569', marginTop: 3, display: 'flex', gap: 6 }}>
                <span>ATK<StageLabel stage={activePoke.atkStage} /></span>
                <span>DEF<StageLabel stage={activePoke.defStage} /></span>
                <span>SPD<StageLabel stage={activePoke.spdStage} /></span>
              </div>
            </div>
          </div>
        )}
        {/* Invisible spacer so the div has height */}
        <div style={{ height: 185 }} />
      </div>

      {/* Move buttons */}
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
        {activePoke?.moves.map((m, i) => (
          <MoveBtn
            key={`${m.name}_${i}`}
            move={m}
            idx={i}
            channelId={run.channelId}
            onAction={onAction}
            busy={busy}
            onAttack={handleAttack}
          />
        ))}
      </div>

      {/* Battle log */}
      <BattleLog logs={run.battleLog} />
    </div>
  );
}
