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
    <span style={{ color: stage > 0 ? '#4ade80' : '#f87171', fontSize: 10, fontWeight: 700 }}>
      {stage > 0 ? `▲${stage}` : `▼${Math.abs(stage)}`}
    </span>
  );
}

function EnemyCard({ enemy, isBoss }: { enemy: TowerEnemy; isBoss: boolean }) {
  const imgSrc = spriteUrl(enemy.pokeId, 'front');
  return (
    <div className="anim-slide-right" style={{
      display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 6,
      padding: '10px 14px 8px',
      background: isBoss
        ? 'linear-gradient(135deg, #2d0a0a 0%, #1e0505 100%)'
        : 'linear-gradient(135deg, #1a1f35 0%, #0f172a 100%)',
      borderRadius: 12,
      border: isBoss ? '2px solid #ef4444' : '1px solid #2d3748',
      boxShadow: isBoss ? '0 0 20px #ef444422' : '0 4px 12px rgba(0,0,0,0.4)',
      flex: 1,
      animation: isBoss ? 'bossGlow 1.5s ease-in-out infinite' : undefined,
    }}
    >
      {/* Name row */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
        {isBoss && <span style={{ color: '#ef4444', fontSize: 10, fontWeight: 700, fontFamily: "'Press Start 2P', monospace", letterSpacing: '0.08em' }}>BOSS</span>}
        <span style={{ fontWeight: 900, fontSize: 15, color: '#fff' }}>{enemy.name}</span>
        {enemy.battleStatus && <StatusBadge status={enemy.battleStatus} />}
      </div>
      {/* Types */}
      <div style={{ display: 'flex', gap: 4 }}>
        {enemy.types.map(t => <TypeBadge key={t} type={t} />)}
      </div>
      {/* HP */}
      <div style={{ width: '100%', maxWidth: 180 }}>
        <HpBar current={enemy.currentHP} max={enemy.maxHP} label="HP" />
      </div>
      {/* Stats */}
      <div style={{ fontSize: 10, color: '#64748b', display: 'flex', gap: 6 }}>
        <span>ATK <StageLabel stage={enemy.atkStage} /></span>
        <span>DEF <StageLabel stage={enemy.defStage} /></span>
        <span>SPD <StageLabel stage={enemy.spdStage} /></span>
        {enemy.goldReward > 0 && <span style={{ color: '#fbbf24' }}>💰{enemy.goldReward}</span>}
      </div>
      {/* Sprite */}
      <img
        src={imgSrc}
        alt={enemy.name}
        style={{
          imageRendering: 'pixelated',
          width: 96, height: 96,
          filter: enemy.currentHP === 0
            ? 'grayscale(1) opacity(0.3)'
            : isBoss ? 'drop-shadow(0 0 10px #ef4444)' : 'drop-shadow(0 4px 12px rgba(0,0,0,0.7))',
          animation: enemy.currentHP > 0 ? 'bounce 2s ease-in-out infinite' : undefined,
        }}
      />
    </div>
  );
}

function PlayerCard({ poke }: { poke: TowerRun['team'][0] }) {
  const imgSrc = poke.isShiny ? spriteUrl(poke.pokeId, 'shiny') : spriteUrl(poke.pokeId, 'back');
  return (
    <div className="anim-slide-left" style={{
      display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: 6,
      padding: '10px 14px 8px',
      background: 'linear-gradient(135deg, #0f2030 0%, #0a1525 100%)',
      borderRadius: 12, border: '1px solid #1e3a5f',
      boxShadow: '0 4px 12px rgba(0,0,0,0.4)',
      flex: 1,
    }}>
      {/* Name row */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
        <span style={{ fontWeight: 900, fontSize: 15, color: '#fff' }}>{poke.displayName || poke.name}</span>
        {poke.isShiny && <span title="Shiny!">✨</span>}
        {poke.battleStatus && <StatusBadge status={poke.battleStatus} />}
      </div>
      {/* Types */}
      <div style={{ display: 'flex', gap: 4 }}>
        {poke.types.map(t => <TypeBadge key={t} type={t} />)}
      </div>
      {/* HP */}
      <div style={{ width: '100%', maxWidth: 200 }}>
        <HpBar current={poke.currentHP} max={poke.maxHP} label="HP" />
      </div>
      {/* Stats */}
      <div style={{ fontSize: 10, color: '#64748b', display: 'flex', gap: 6 }}>
        <span>ATK <StageLabel stage={poke.atkStage} /></span>
        <span>DEF <StageLabel stage={poke.defStage} /></span>
        <span>SPD <StageLabel stage={poke.spdStage} /></span>
      </div>
      {/* Sprite */}
      <img
        src={imgSrc}
        alt={poke.name}
        style={{
          imageRendering: 'pixelated',
          width: 112, height: 112,
          filter: poke.currentHP === 0
            ? 'grayscale(1) opacity(0.3)'
            : 'drop-shadow(0 4px 12px rgba(99,102,241,0.5))',
          transform: 'scaleX(-1)',
          animation: poke.currentHP > 0 ? 'bounce 2.2s ease-in-out infinite' : undefined,
        }}
      />
    </div>
  );
}

function MoveBtn({ move, idx, channelId, onAction, busy }: {
  move: TowerMove; idx: number; channelId: string;
  onAction: (id: string) => void; busy: boolean;
}) {
  const color = typeColor(move.type);
  const empty = move.currentPP === 0;
  const customId = `tower_move_${channelId}_${idx}`;

  return (
    <button
      className="btn-hover"
      disabled={busy || empty}
      onClick={() => onAction(customId)}
      style={{
        background: empty
          ? '#1a1f2e'
          : `linear-gradient(135deg, ${color}44 0%, ${color}22 100%)`,
        color: empty ? '#475569' : '#fff',
        border: `1px solid ${empty ? '#334155' : color + '66'}`,
        borderRadius: 10,
        padding: '10px 12px',
        cursor: busy || empty ? 'not-allowed' : 'pointer',
        opacity: busy || empty ? 0.55 : 1,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 3,
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
      <span style={{ fontSize: 20, lineHeight: 1 }}>{move.emoji}</span>
      <span style={{ fontWeight: 700, fontSize: 12 }}>{move.name}</span>
      <span style={{ fontSize: 10, opacity: 0.75 }}>
        {move.category === 'physical' ? '物攻' : move.category === 'special' ? '特攻' : '變化'}
        {move.power > 0 && ` · ${move.power}`}
        {move.effectAilment && move.effectChance > 0 && ` · ${move.effectAilment} ${move.effectChance}%`}
      </span>
      <div style={{
        display: 'flex', alignItems: 'center', gap: 4, fontSize: 10,
        color: move.currentPP === 0 ? '#ef4444' : move.currentPP <= move.maxPP / 4 ? '#facc15' : '#94a3b8',
      }}>
        <span>PP</span>
        <span style={{ fontWeight: 700 }}>{move.currentPP}</span>
        <span style={{ opacity: 0.5 }}>/ {move.maxPP}</span>
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
      background: '#07090f',
      borderRadius: 8,
      padding: '8px 12px',
      fontSize: 12, color: '#94a3b8',
      height: 80, overflowY: 'auto',
      border: '1px solid #1e293b',
      lineHeight: 1.6,
    }}>
      {logs.slice(-8).map((log, i, arr) => (
        <div key={i} style={{
          opacity: 0.5 + (i / arr.length) * 0.5,
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

  return (
    <div className="anim-fade-in" style={{ display: 'flex', flexDirection: 'column', gap: 10, height: '100%' }}>
      {/* Floor title */}
      <div style={{ textAlign: 'center' }}>
        {isBoss && (
          <div style={{
            fontFamily: "'Press Start 2P', monospace",
            fontSize: 11, color: '#ef4444',
            letterSpacing: '0.12em',
            marginBottom: 4,
            animation: 'pulse 1s ease-in-out infinite',
          }}>
            ⚠ BOSS BATTLE ⚠
          </div>
        )}
        <div style={{ fontSize: 13, color: '#64748b', fontWeight: 600 }}>
          第 <span style={{ color: '#e2e8f0', fontWeight: 900 }}>{run.currentFloor}</span> 層
        </div>
      </div>

      {/* Battle arena */}
      <div style={{
        display: 'flex', gap: 10, alignItems: 'stretch',
        background: isBoss
          ? 'radial-gradient(ellipse at 70% 30%, #2d0a0a 0%, #0a0e1a 100%)'
          : 'radial-gradient(ellipse at 70% 30%, #0f1e35 0%, #0a0e1a 100%)',
        borderRadius: 14, padding: 10, minHeight: 160,
        border: isBoss ? '1px solid #7f1d1d' : '1px solid #1e2d45',
      }}>
        {activePoke && <PlayerCard poke={activePoke} />}
        {enemy && <EnemyCard enemy={enemy} isBoss={isBoss} />}
      </div>

      {/* Move buttons */}
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
        {activePoke?.moves.map((m, i) => (
          <MoveBtn
            key={m.name}
            move={m}
            idx={i}
            channelId={run.channelId}
            onAction={onAction}
            busy={busy}
          />
        ))}
      </div>

      {/* Battle log */}
      <BattleLog logs={run.battleLog} />
    </div>
  );
}
