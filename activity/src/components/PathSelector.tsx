import { spriteUrl } from '../utils';
import { HpBar } from './HpBar';
import { StsMap } from './StsMap';
import type { TowerRun } from '../types';

interface Props {
  run: TowerRun;
  onAction: (customId: string) => void;
  busy: boolean;
}

export function PathSelector({ run, onAction, busy }: Props) {
  const nextFloor = run.currentFloor + 1;
  const isBoss = nextFloor % 10 === 0;
  const hasStsMap = (run.mapNodes?.length ?? 0) > 0;

  return (
    <div className="anim-fade-in" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>

      {/* Floor header */}
      <div style={{ textAlign: 'center' }}>
        {isBoss && (
          <div style={{
            fontFamily: "'Press Start 2P', monospace",
            fontSize: 10, color: '#ef4444', letterSpacing: '0.1em',
            marginBottom: 4, animation: 'pulse 1s ease-in-out infinite',
          }}>⚠ BOSS FLOOR AHEAD ⚠</div>
        )}
        <div style={{ fontSize: 18, fontWeight: 900, color: isBoss ? '#ef4444' : '#fff' }}>
          第 {nextFloor} 層{isBoss ? ' ⚔️' : ''}
        </div>
        <div style={{ color: '#64748b', fontSize: 11, marginTop: 2 }}>
          選擇路線前進
        </div>
      </div>

      {/* Status bar: progress + gold */}
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        {/* Progress */}
        <div style={{ flex: 1, background: '#0f172a', borderRadius: 6, padding: '4px 8px', border: '1px solid #1e293b' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 9, color: '#475569', marginBottom: 2 }}>
            <span>進度</span><span>{run.currentFloor}/{run.maxFloor}</span>
          </div>
          <div style={{ height: 5, background: '#1e293b', borderRadius: 3, overflow: 'hidden' }}>
            <div style={{
              height: '100%',
              width: `${(run.currentFloor / run.maxFloor) * 100}%`,
              background: isBoss ? 'linear-gradient(90deg,#ef4444,#f97316)' : 'linear-gradient(90deg,#6366f1,#a855f7)',
              borderRadius: 3, transition: 'width 0.6s ease',
            }} />
          </div>
        </div>
        {/* Gold */}
        <div style={{
          background: '#1a1400', border: '1px solid #3d2e00', borderRadius: 6,
          padding: '4px 10px', display: 'flex', alignItems: 'center', gap: 4,
          flexShrink: 0,
        }}>
          <span style={{ fontSize: 14 }}>💰</span>
          <span style={{ fontWeight: 900, color: '#fbbf24', fontSize: 14 }}>{run.gold}</span>
        </div>
      </div>

      {/* STS Map (主要選擇介面) */}
      {hasStsMap ? (
        <StsMap run={run} onSelectNode={onAction} busy={busy} />
      ) : (
        /* Fallback: 舊風格卡片（無地圖資料時） */
        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', justifyContent: 'center' }}>
          {run.pathOptions.map(opt => (
            <button
              key={opt.customId}
              className="btn-hover"
              disabled={busy}
              onClick={() => onAction(opt.customId)}
              style={{
                background: '#0f172a', border: '1px solid #334155', borderRadius: 12,
                padding: '16px 20px', cursor: busy ? 'not-allowed' : 'pointer',
                color: '#fff', minWidth: 120,
                display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8,
              }}
            >
              <span style={{ fontSize: 28 }}>{opt.emoji}</span>
              <span style={{ fontWeight: 700, fontSize: 14 }}>{opt.label}</span>
              {opt.description && <span style={{ fontSize: 11, color: '#94a3b8' }}>{opt.description}</span>}
            </button>
          ))}
        </div>
      )}

      {/* Team preview */}
      <div style={{ background: '#0a0e1a', borderRadius: 10, padding: '8px 10px', border: '1px solid #1e293b' }}>
        <div style={{ color: '#334155', fontSize: 10, marginBottom: 6, fontWeight: 600 }}>🎒 目前隊伍</div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          {run.team.map((p, i) => {
            const fainted = p.currentHP === 0;
            return (
              <div key={i} style={{
                display: 'flex', alignItems: 'center', gap: 6,
                background: fainted ? '#0a0a0a' : i === run.activeIndex ? '#1a2040' : '#0f172a',
                borderRadius: 8, padding: '4px 8px',
                border: `1px solid ${fainted ? '#1e1e1e' : i === run.activeIndex ? '#6366f155' : '#1e293b'}`,
                opacity: fainted ? 0.4 : 1, flex: '1 1 110px',
              }}>
                <img src={spriteUrl(p.pokeId)} alt={p.name}
                  style={{ width: 32, height: 32, imageRendering: 'pixelated' }} />
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontSize: 10, fontWeight: 700, color: '#e2e8f0', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {p.displayName}{p.isShiny ? ' ✨' : ''}
                    {i === run.activeIndex && <span style={{ color: '#6366f1' }}> ▶</span>}
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
