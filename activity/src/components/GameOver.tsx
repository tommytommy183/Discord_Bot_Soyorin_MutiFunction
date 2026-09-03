import { spriteUrl } from '../utils';
import type { TowerRun } from '../types';

interface Props {
  run: TowerRun;
  onRestart: () => void;
  onAction?: (customId: string) => void;
}

function TeamGrid({ run, compact = false }: { run: TowerRun; compact?: boolean }) {
  return (
    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', justifyContent: 'center' }}>
      {run.team.map((p, i) => {
        const fainted = p.currentHP === 0;
        const hpPct = Math.max(0, p.currentHP / p.maxHP);
        const hpColor = hpPct > 0.5 ? '#22c55e' : hpPct > 0.25 ? '#f59e0b' : '#ef4444';
        return (
          <div key={i} style={{
            display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 3,
            background: fainted ? '#0a0a12' : 'linear-gradient(135deg, #0f2a18, #0f172a)',
            borderRadius: 10, padding: compact ? '7px 9px' : '10px 12px',
            border: `1px solid ${fainted ? '#1e1e2e' : '#22c55e55'}`,
            opacity: fainted ? 0.45 : 1,
            minWidth: compact ? 62 : 72,
            boxShadow: fainted ? 'none' : '0 0 10px #22c55e18',
          }}>
            <img src={spriteUrl(p.pokeId)} alt={p.name} style={{
              width: compact ? 38 : 44, height: compact ? 38 : 44,
              imageRendering: 'pixelated',
              filter: fainted ? 'grayscale(1) brightness(0.5)' : 'drop-shadow(0 0 5px #22c55e88)',
              animation: fainted ? undefined : 'bounce 2.2s ease-in-out infinite',
            }} />
            <div style={{ fontSize: 9, color: fainted ? '#475569' : '#94a3b8', textAlign: 'center', maxWidth: 64, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {p.displayName}
            </div>
            <div style={{ fontSize: 9, fontWeight: 700, color: fainted ? '#ef444488' : hpColor }}>
              {fainted ? 'FNT' : `${p.currentHP}/${p.maxHP}`}
            </div>
          </div>
        );
      })}
    </div>
  );
}

function StatChip({ emoji, label, value, color = '#fbbf24' }: { emoji: string; label: string; value: string; color?: string }) {
  return (
    <div style={{
      flex: 1, minWidth: 72,
      background: '#0b0f1a', border: `1px solid ${color}33`,
      borderRadius: 10, padding: '9px 6px', textAlign: 'center',
    }}>
      <div style={{ fontSize: 18, lineHeight: 1, marginBottom: 3 }}>{emoji}</div>
      <div style={{ fontSize: 14, fontWeight: 900, color }}>{value}</div>
      <div style={{ fontSize: 9, color: '#475569', marginTop: 2 }}>{label}</div>
    </div>
  );
}

// ── 20層通關特殊畫面 ──────────────────────────────────────────────────────
function TowerClearedScene({ run, onAction, busy }: { run: TowerRun; onAction: (id: string) => void; busy?: boolean }) {
  const survivingCount = run.team.filter(p => p.currentHP > 0).length;

  return (
    <div className="anim-fade-in" style={{
      display: 'flex', flexDirection: 'column', gap: 14,
      padding: '18px 16px',
      background: 'linear-gradient(180deg, #050810 0%, #130820 50%, #050810 100%)',
      minHeight: '100%',
    }}>

      {/* Header */}
      <div style={{ textAlign: 'center' }}>
        <div style={{
          fontSize: 72, lineHeight: 1,
          animation: 'bounce 1.2s ease-in-out infinite',
          filter: 'drop-shadow(0 0 32px #fbbf24)',
        }}>🏆</div>
        <div style={{
          fontFamily: "'Press Start 2P', monospace",
          fontSize: 11, color: '#fbbf24', marginTop: 12, letterSpacing: '0.1em', lineHeight: 1.8,
          textShadow: '0 0 20px #fbbf24, 0 0 50px #f59e0b88',
          animation: 'pulse 2s ease-in-out infinite',
        }}>TOWER CLEARED!</div>
        <div style={{ fontSize: 14, color: '#e2e8f0', fontWeight: 900, marginTop: 8 }}>
          🎉 {run.playerName} 征服了 {run.maxFloor} 層！
        </div>
      </div>

      {/* Stats */}
      <div style={{ display: 'flex', gap: 8 }}>
        <StatChip emoji="🏔️" label="通關層數" value={`${run.maxFloor} 層`} />
        <StatChip emoji="💰" label="剩餘金幣" value={`${run.gold}`} color="#4ade80" />
        <StatChip emoji="💚" label="倖存" value={`${survivingCount}/${run.team.length}`} color="#60a5fa" />
      </div>

      {/* Team */}
      <div style={{
        background: 'linear-gradient(135deg, #0d1f12, #0f172a)',
        border: '1px solid #22c55e44', borderRadius: 12, padding: '12px',
        boxShadow: '0 0 16px #22c55e18',
      }}>
        <div style={{ fontSize: 10, color: '#22c55e', fontWeight: 700, marginBottom: 8, letterSpacing: '0.06em' }}>👥 最終出戰隊伍</div>
        <TeamGrid run={run} compact />
      </div>

      {/* Shiny reward */}
      <div style={{
        background: 'linear-gradient(135deg, #1a1400, #0f172a)',
        border: '1px solid #fbbf2444', borderRadius: 10, padding: '10px 14px',
        textAlign: 'center',
      }}>
        <div style={{ fontSize: 12, color: '#fbbf24', fontWeight: 700 }}>✨ 塔頂獎勵已解鎖</div>
        <div style={{ fontSize: 11, color: '#94a3b8', marginTop: 4, lineHeight: 1.6 }}>
          下一次 /抓pokemon 保證閃光！
        </div>
      </div>

      {/* Divider */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <div style={{ flex: 1, height: 1, background: 'linear-gradient(90deg, transparent, #4f46e5, transparent)' }} />
        <div style={{ fontSize: 10, color: '#6366f1', fontWeight: 700 }}>接下來呢？</div>
        <div style={{ flex: 1, height: 1, background: 'linear-gradient(90deg, transparent, #4f46e5, transparent)' }} />
      </div>

      {/* Choices */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <button
          className="btn-hover"
          disabled={busy}
          onClick={() => onAction(`tower_preBoss_${run.channelId}_continue`)}
          style={{
            background: 'linear-gradient(135deg, #1a0a2e, #2d1b69)',
            border: '2px solid #7c3aed88', borderRadius: 12,
            padding: '15px 16px', cursor: busy ? 'not-allowed' : 'pointer',
            display: 'flex', alignItems: 'center', gap: 12, textAlign: 'left',
            boxShadow: '0 0 20px #7c3aed22',
          }}
        >
          <span style={{ fontSize: 28 }}>🎁</span>
          <div>
            <div style={{ fontSize: 14, fontWeight: 900, color: '#c4b5fd' }}>最後補給站</div>
            <div style={{ fontSize: 11, color: '#6d28d9', marginTop: 2 }}>整備完畢再挑戰終極神獸</div>
          </div>
          <span style={{ marginLeft: 'auto', color: '#7c3aed', fontSize: 18 }}>›</span>
        </button>

        <button
          className="btn-hover"
          disabled={busy}
          onClick={() => onAction(`tower_preBoss_${run.channelId}_home`)}
          style={{
            background: 'linear-gradient(135deg, #052e16, #0a0e1a)',
            border: '2px solid #22c55e66', borderRadius: 12,
            padding: '15px 16px', cursor: busy ? 'not-allowed' : 'pointer',
            display: 'flex', alignItems: 'center', gap: 12, textAlign: 'left',
          }}
        >
          <span style={{ fontSize: 28 }}>🏠</span>
          <div>
            <div style={{ fontSize: 14, fontWeight: 900, color: '#86efac' }}>帶著榮耀回家</div>
            <div style={{ fontSize: 11, color: '#166534', marginTop: 2 }}>本次爬塔正式通關結束</div>
          </div>
          <span style={{ marginLeft: 'auto', color: '#22c55e', fontSize: 18 }}>›</span>
        </button>
      </div>
    </div>
  );
}

// ── 一般勝利畫面（非20層）────────────────────────────────────────────────
function VictoryScene({ run, onRestart }: { run: TowerRun; onRestart: () => void }) {
  const survivingCount = run.team.filter(p => p.currentHP > 0).length;

  return (
    <div className="anim-fade-in" style={{
      display: 'flex', flexDirection: 'column', gap: 14,
      padding: '18px 16px', alignItems: 'center',
      background: 'linear-gradient(180deg, #050810 0%, #0e1a08 60%, #050810 100%)',
      minHeight: '100%',
    }}>
      <div style={{ fontSize: 64, animation: 'bounce 1.2s ease-in-out infinite', filter: 'drop-shadow(0 0 24px #22c55e)' }}>🌟</div>

      <div style={{ textAlign: 'center' }}>
        <div style={{
          fontFamily: "'Press Start 2P', monospace",
          fontSize: 10, color: '#4ade80', letterSpacing: '0.08em', lineHeight: 1.8,
          textShadow: '0 0 16px #22c55e',
        }}>VICTORY!</div>
        <div style={{ fontSize: 13, color: '#e2e8f0', fontWeight: 700, marginTop: 6 }}>
          {run.playerName} 通關了！
        </div>
      </div>

      <div style={{ display: 'flex', gap: 8, width: '100%' }}>
        <StatChip emoji="🏔️" label="層數" value={`${run.currentFloor}`} color="#4ade80" />
        <StatChip emoji="💰" label="金幣" value={`${run.gold}`} color="#fbbf24" />
        <StatChip emoji="💚" label="倖存" value={`${survivingCount}/${run.team.length}`} color="#60a5fa" />
      </div>

      <div style={{
        background: '#0b1215', border: '1px solid #22c55e33',
        borderRadius: 12, padding: '12px', width: '100%',
      }}>
        <div style={{ fontSize: 10, color: '#22c55e', fontWeight: 700, marginBottom: 8 }}>👥 隊伍狀況</div>
        <TeamGrid run={run} compact />
      </div>

      <button
        className="btn-hover"
        onClick={onRestart}
        style={{
          background: 'linear-gradient(135deg, #16a34a, #22c55e)',
          color: '#fff', border: 'none', borderRadius: 12,
          padding: '14px 40px', fontSize: 15, fontWeight: 900,
          cursor: 'pointer', width: '100%',
          boxShadow: '0 4px 20px #22c55e44',
        }}
      >🔄 再挑一次！</button>
    </div>
  );
}

// ── 敗北畫面 ──────────────────────────────────────────────────────────────
function DefeatScene({ run, onRestart }: { run: TowerRun; onRestart: () => void }) {
  return (
    <div className="anim-fade-in" style={{
      display: 'flex', flexDirection: 'column', gap: 14,
      padding: '20px 16px', alignItems: 'center',
      background: 'linear-gradient(180deg, #050810 0%, #1a0808 60%, #050810 100%)',
      minHeight: '100%',
    }}>
      <div style={{ fontSize: 64, filter: 'drop-shadow(0 0 20px #ef4444)', animation: 'pulse 1.5s ease-in-out infinite' }}>💀</div>

      <div style={{ textAlign: 'center' }}>
        <div style={{
          fontFamily: "'Press Start 2P', monospace",
          fontSize: 11, color: '#ef4444', letterSpacing: '0.06em', lineHeight: 1.6,
          textShadow: '0 0 16px #ef4444',
        }}>GAME OVER</div>
        <div style={{ fontSize: 13, color: '#94a3b8', marginTop: 8, lineHeight: 1.7 }}>
          <strong style={{ color: '#f87171' }}>{run.playerName}</strong> 在第{' '}
          <strong style={{ color: '#ef4444', fontSize: 16 }}>{run.currentFloor}</strong> 層倒下了。
        </div>
      </div>

      {/* Team */}
      <div style={{
        background: '#0b0a12', border: '1px solid #ef444433',
        borderRadius: 12, padding: '12px', width: '100%',
      }}>
        <div style={{ fontSize: 10, color: '#ef4444', fontWeight: 700, marginBottom: 8 }}>最後的隊伍</div>
        <TeamGrid run={run} compact />
      </div>

      {/* Last log */}
      <div style={{
        background: '#07090f', borderRadius: 8, padding: '10px 12px',
        width: '100%', fontSize: 11, color: '#475569',
        maxHeight: 110, overflowY: 'auto', lineHeight: 1.8,
        border: '1px solid #1e293b',
      }}>
        {run.runLog.slice(-8).reverse().map((l, i) => (
          <div key={i} style={{ opacity: 1 - i * 0.1 }}>{l}</div>
        ))}
      </div>

      <button
        className="btn-hover"
        onClick={onRestart}
        style={{
          background: 'linear-gradient(135deg, #7f1d1d, #dc2626)',
          color: '#fff', border: 'none', borderRadius: 12,
          padding: '14px 40px', fontSize: 15, fontWeight: 900,
          cursor: 'pointer', width: '100%',
          boxShadow: '0 4px 20px #ef444433',
        }}
      >🔄 再來一次</button>
    </div>
  );
}

// ── 主 export ────────────────────────────────────────────────────────────
export function GameOver({ run, onRestart, onAction }: Props) {
  // 打完20層的特殊通關畫面
  if (run.state === 'Victory' && run.preBossShopPending && onAction) {
    return <TowerClearedScene run={run} onAction={onAction} />;
  }

  if (run.state === 'Victory') {
    return <VictoryScene run={run} onRestart={onRestart} />;
  }

  return <DefeatScene run={run} onRestart={onRestart} />;
}
