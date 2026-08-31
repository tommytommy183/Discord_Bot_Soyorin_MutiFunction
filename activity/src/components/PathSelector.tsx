import { spriteUrl } from '../utils';
import { HpBar } from './HpBar';
import type { TowerRun } from '../types';

interface Props {
  run: TowerRun;
  onAction: (customId: string) => void;
  busy: boolean;
}

const PATH_CONFIG: Record<string, { color: string; bg: string; desc: string }> = {
  '⚔️': { color: '#ef4444', bg: '#ef444415', desc: '挑戰野生 Pokemon！' },
  '🛍️': { color: '#3b82f6', bg: '#3b82f615', desc: '購買道具與強化' },
  '🏕️': { color: '#22c55e', bg: '#22c55e15', desc: '回復 HP 並休息' },
  '🎉': { color: '#a855f7', bg: '#a855f715', desc: '隨機神秘事件' },
  '🎰': { color: '#f59e0b', bg: '#f59e0b15', desc: '賭場：用金幣賭一把' },
  '💀': { color: '#7c3aed', bg: '#7c3aed15', desc: '高風險高報酬挑戰' },
  '🌟': { color: '#fbbf24', bg: '#fbbf2415', desc: '特殊稀有事件' },
  '📦': { color: '#0ea5e9', bg: '#0ea5e915', desc: '神秘寶箱' },
  '🔮': { color: '#8b5cf6', bg: '#8b5cf615', desc: '獲取強力遺物' },
};

function getPathStyle(emoji: string) {
  const found = Object.entries(PATH_CONFIG).find(([k]) => emoji?.includes(k));
  return found?.[1] ?? { color: '#6366f1', bg: '#6366f115', desc: '' };
}

export function PathSelector({ run, onAction, busy }: Props) {
  const nextFloor = run.currentFloor + 1;
  const isBoss = nextFloor % 10 === 0;
  const progress = run.currentFloor / run.maxFloor;

  return (
    <div className="anim-fade-in" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>

      {/* Floor header */}
      <div style={{ textAlign: 'center' }}>
        {isBoss && (
          <div style={{
            fontFamily: "'Press Start 2P', monospace",
            fontSize: 10, color: '#ef4444', letterSpacing: '0.1em',
            marginBottom: 6, animation: 'pulse 1s ease-in-out infinite',
          }}>
            ⚠ BOSS FLOOR AHEAD ⚠
          </div>
        )}
        <div style={{ fontSize: 20, fontWeight: 900, color: isBoss ? '#ef4444' : '#fff' }}>
          第 {nextFloor} 層
          {isBoss && <span style={{ marginLeft: 8, fontSize: 14 }}>⚔️</span>}
        </div>
        <div style={{ color: '#64748b', fontSize: 12, marginTop: 2 }}>選擇前進路線</div>
      </div>

      {/* Progress bar */}
      <div style={{ background: '#0f172a', borderRadius: 6, padding: '6px 10px', border: '1px solid #1e293b' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 10, color: '#475569', marginBottom: 4 }}>
          <span>進度</span>
          <span style={{ color: '#94a3b8' }}>{run.currentFloor} / {run.maxFloor}</span>
        </div>
        <div style={{ height: 8, background: '#1e293b', borderRadius: 4, overflow: 'hidden' }}>
          <div style={{
            height: '100%',
            width: `${progress * 100}%`,
            background: isBoss
              ? 'linear-gradient(90deg, #ef4444, #f97316)'
              : 'linear-gradient(90deg, #6366f1, #a855f7)',
            borderRadius: 4,
            transition: 'width 0.8s ease',
            boxShadow: isBoss ? '0 0 8px #ef4444' : '0 0 8px #6366f1',
          }} />
        </div>
      </div>

      {/* Gold */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 6,
        background: '#1a1400', border: '1px solid #3d2e00',
        borderRadius: 8, padding: '6px 12px', alignSelf: 'center',
      }}>
        <span style={{ fontSize: 18 }}>💰</span>
        <span style={{ fontWeight: 900, fontSize: 16, color: '#fbbf24' }}>{run.gold}</span>
        <span style={{ fontSize: 11, color: '#92400e' }}>金幣</span>
      </div>

      {/* Path cards */}
      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', justifyContent: 'center' }}>
        {run.pathOptions.map((opt) => {
          const cfg = getPathStyle(opt.emoji ?? '');
          return (
            <button
              key={opt.customId}
              className="btn-hover"
              disabled={busy}
              onClick={() => onAction(opt.customId)}
              style={{
                background: cfg.bg,
                border: `1px solid ${cfg.color}44`,
                borderRadius: 14,
                padding: '18px 22px',
                cursor: busy ? 'not-allowed' : 'pointer',
                color: '#fff',
                minWidth: 140, maxWidth: 180,
                display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8,
                position: 'relative', overflow: 'hidden',
                flex: '1 1 140px',
              }}
            >
              {/* Top accent */}
              <div style={{
                position: 'absolute', top: 0, left: 0, right: 0, height: 3,
                background: cfg.color,
              }} />
              <span style={{ fontSize: 32 }}>{opt.emoji}</span>
              <span style={{ fontWeight: 800, fontSize: 15, color: cfg.color }}>{opt.label}</span>
              {opt.description && (
                <span style={{ fontSize: 11, color: '#94a3b8', textAlign: 'center', lineHeight: 1.4 }}>
                  {opt.description}
                </span>
              )}
              {!opt.description && cfg.desc && (
                <span style={{ fontSize: 11, color: '#475569', textAlign: 'center', lineHeight: 1.4 }}>
                  {cfg.desc}
                </span>
              )}
            </button>
          );
        })}
      </div>

      {/* Team preview */}
      <div style={{ background: '#0a0e1a', borderRadius: 10, padding: 10, border: '1px solid #1e293b' }}>
        <div style={{ color: '#475569', fontSize: 11, marginBottom: 8, fontWeight: 600 }}>🎒 目前隊伍</div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          {run.team.map((p, i) => {
            const fainted = p.currentHP === 0;
            return (
              <div key={i} style={{
                display: 'flex', alignItems: 'center', gap: 8,
                background: fainted ? '#0a0a0a' : i === run.activeIndex ? '#1a2040' : '#0f172a',
                borderRadius: 8, padding: '6px 10px',
                border: `1px solid ${fainted ? '#1e1e1e' : i === run.activeIndex ? '#6366f1' : '#1e293b'}`,
                opacity: fainted ? 0.4 : 1,
                flex: '1 1 120px',
              }}>
                <img
                  src={spriteUrl(p.pokeId, 'front')}
                  alt={p.name}
                  style={{ width: 36, height: 36, imageRendering: 'pixelated' }}
                />
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontSize: 11, fontWeight: 700, color: '#e2e8f0', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {p.displayName}{p.isShiny ? ' ✨' : ''}
                    {i === run.activeIndex && <span style={{ color: '#6366f1', marginLeft: 4 }}>▶</span>}
                  </div>
                  <HpBar current={p.currentHP} max={p.maxHP} compact />
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
