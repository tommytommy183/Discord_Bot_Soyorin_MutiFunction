import { useState } from 'react';
import type { TowerRun } from '../types';
import { HpBar } from './HpBar';
import { TypeBadge } from './TypeBadge';
import { spriteUrl } from '../utils';

const BALL_LABELS: Record<string, { label: string; emoji: string }> = {
  normal: { label: '普通球', emoji: '⚪' },
  super:  { label: '超級球', emoji: '🔵' },
  ultra:  { label: '高級球', emoji: '🟡' },
  master: { label: '大師球', emoji: '🟣' },
};

const RELIC_NAMES: Record<string, { name: string; emoji: string; desc: string }> = {
  relic_shield:     { name: '守護之盾', emoji: '🛡️', desc: '每場戰鬥首次受到的攻擊無效化' },
  relic_hourglass:  { name: '時光沙漏', emoji: '⏳', desc: '每層進入時回復 5% HP' },
  relic_time_warp:  { name: '時空扭曲', emoji: '🌀', desc: '每場戰鬥開始時回復 3 PP' },
};

type Tab = 'team' | 'items';

interface Props {
  run: TowerRun;
  isOpen: boolean;
  onClose: () => void;
  onAction?: (customId: string) => void;
}

export function Inventory({ run, isOpen, onClose, onAction }: Props) {
  const [tab, setTab] = useState<Tab>('team');

  if (!isOpen) return null;

  const balls = run.balls ?? {};
  const hasBalls = Object.values(balls).some(v => v > 0);

  return (
    <div style={{
      position: 'absolute', inset: 0,
      background: 'rgba(0,0,0,0.7)',
      backdropFilter: 'blur(3px)',
      zIndex: 100,
      display: 'flex', alignItems: 'flex-end',
    }} onClick={onClose}>
      <div
        className="anim-fade-in"
        style={{
          width: '100%',
          maxHeight: '80vh',
          background: '#0f172a',
          borderRadius: '16px 16px 0 0',
          border: '1px solid #1e293b',
          borderBottom: 'none',
          display: 'flex', flexDirection: 'column',
          overflow: 'hidden',
        }}
        onClick={e => e.stopPropagation()}
      >
        {/* Header */}
        <div style={{
          padding: '14px 16px 0',
          borderBottom: '1px solid #1e293b',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
            <div style={{ fontWeight: 900, fontSize: 15, color: '#fff' }}>🎒 背包</div>
            <button
              onClick={onClose}
              style={{
                background: 'none', border: 'none', color: '#475569',
                fontSize: 18, cursor: 'pointer', lineHeight: 1,
              }}
            >✕</button>
          </div>
          {/* Tabs */}
          <div style={{ display: 'flex', gap: 2 }}>
            {(['team', 'items'] as Tab[]).map(t => (
              <button
                key={t}
                onClick={() => setTab(t)}
                style={{
                  background: tab === t ? '#1e293b' : 'none',
                  border: 'none',
                  borderRadius: '8px 8px 0 0',
                  padding: '8px 16px',
                  color: tab === t ? '#fff' : '#475569',
                  fontWeight: tab === t ? 700 : 400,
                  fontSize: 13,
                  cursor: 'pointer',
                }}
              >
                {t === 'team' ? '👥 隊伍' : '🎯 道具'}
              </button>
            ))}
          </div>
        </div>

        {/* Content */}
        <div style={{ flex: 1, overflowY: 'auto', padding: '12px 14px' }}>
          {tab === 'team' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              {run.team.map((p, i) => (
                <div key={i} style={{
                  background: '#07090f',
                  borderRadius: 10,
                  padding: '10px 12px',
                  border: `1px solid ${i === run.activeIndex ? '#6366f1' : '#1e293b'}`,
                  display: 'flex', gap: 10, alignItems: 'flex-start',
                }}>
                  <img
                    src={p.isShiny ? spriteUrl(p.pokeId, 'shiny') : spriteUrl(p.pokeId)}
                    alt={p.name}
                    style={{ width: 52, height: 52, imageRendering: 'pixelated', flexShrink: 0 }}
                  />
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 4 }}>
                      <span style={{ fontWeight: 900, fontSize: 13, color: '#fff' }}>
                        {p.displayName}
                      </span>
                      {p.isShiny && <span>✨</span>}
                      {i === run.activeIndex && (
                        <span style={{ fontSize: 9, color: '#6366f1', fontWeight: 700 }}>出戰中</span>
                      )}
                    </div>
                    <div style={{ display: 'flex', gap: 3, marginBottom: 5 }}>
                      {p.types.map(t => <TypeBadge key={t} type={t} />)}
                    </div>
                    <HpBar current={p.currentHP} max={p.maxHP} label="HP" />
                    {/* Moves */}
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4, marginTop: 6 }}>
                      {p.moves.map((m, mi) => (
                        <span key={mi} style={{
                          fontSize: 10, background: '#1e293b',
                          borderRadius: 4, padding: '2px 6px',
                          color: '#94a3b8',
                        }}>
                          {m.emoji} {m.name} <span style={{ color: '#475569' }}>({m.currentPP}/{m.maxPP})</span>
                        </span>
                      ))}
                    </div>
                    {/* Set as lead — only outside battle and not already first */}
                    {onAction && run.state === 'SelectingPath' && i !== 0 && p.currentHP > 0 && (
                      <button
                        className="btn-hover"
                        onClick={() => { onAction(`tower_setlead_${run.channelId}_${i}`); onClose(); }}
                        style={{
                          marginTop: 8, background: 'linear-gradient(135deg, #1e1b4b, #312e81)',
                          border: '1px solid #6366f155', borderRadius: 7,
                          padding: '5px 12px', color: '#a5b4fc',
                          fontSize: 11, fontWeight: 700, cursor: 'pointer',
                          display: 'flex', alignItems: 'center', gap: 5,
                        }}
                      >
                        ⭐ 設為首發
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}

          {tab === 'items' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
              {/* Balls */}
              <div>
                <div style={{ fontSize: 11, color: '#475569', fontWeight: 700, marginBottom: 6, letterSpacing: '0.05em' }}>精靈球</div>
                {!hasBalls ? (
                  <div style={{ color: '#334155', fontSize: 12 }}>無</div>
                ) : (
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
                    {Object.entries(balls).filter(([, v]) => v > 0).map(([k, v]) => {
                      const info = BALL_LABELS[k] ?? { label: k, emoji: '⚪' };
                      return (
                        <div key={k} style={{
                          background: '#07090f', borderRadius: 8,
                          border: '1px solid #1e293b',
                          padding: '8px 12px',
                          display: 'flex', alignItems: 'center', gap: 6,
                        }}>
                          <span style={{ fontSize: 18 }}>{info.emoji}</span>
                          <div>
                            <div style={{ fontSize: 11, color: '#94a3b8' }}>{info.label}</div>
                            <div style={{ fontSize: 14, fontWeight: 900, color: '#fff' }}>×{v}</div>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>

              {/* Relics */}
              <div>
                <div style={{ fontSize: 11, color: '#475569', fontWeight: 700, marginBottom: 6, letterSpacing: '0.05em' }}>遺物</div>
                {run.relicIds.length === 0 ? (
                  <div style={{ color: '#334155', fontSize: 12 }}>無遺物</div>
                ) : (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                    {run.relicIds.map(id => {
                      const info = RELIC_NAMES[id];
                      return (
                        <div key={id} style={{
                          background: '#07090f', borderRadius: 8,
                          border: '1px solid #2d1b4e',
                          padding: '8px 12px',
                          display: 'flex', alignItems: 'center', gap: 8,
                        }}>
                          <span style={{ fontSize: 22 }}>{info?.emoji ?? '🔮'}</span>
                          <div>
                            <div style={{ fontSize: 12, fontWeight: 700, color: '#c084fc' }}>
                              {info?.name ?? id}
                            </div>
                            <div style={{ fontSize: 11, color: '#64748b' }}>
                              {info?.desc ?? id}
                            </div>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>

              {/* Cursed relics */}
              {run.cursedRelicIds.length > 0 && (
                <div>
                  <div style={{ fontSize: 11, color: '#ef4444', fontWeight: 700, marginBottom: 6, letterSpacing: '0.05em' }}>詛咒遺物</div>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                    {run.cursedRelicIds.map(id => (
                      <div key={id} style={{
                        background: '#1a0000', borderRadius: 8,
                        border: '1px solid #7f1d1d',
                        padding: '8px 12px',
                        display: 'flex', alignItems: 'center', gap: 8,
                      }}>
                        <span style={{ fontSize: 22 }}>💀</span>
                        <div style={{ fontSize: 12, color: '#fca5a5' }}>{id}</div>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
