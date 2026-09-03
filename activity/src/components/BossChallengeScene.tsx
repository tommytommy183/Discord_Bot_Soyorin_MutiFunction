import { spriteUrl } from '../utils';
import type { TowerRun } from '../types';

interface Props {
  run: TowerRun;
  onAction: (customId: string) => void;
  busy: boolean;
}

// Ultimate boss: Arceus (493)
const ARCEUS_ID = 493;

export function BossChallengeScene({ run, onAction, busy }: Props) {
  const lead = run.team[run.activeIndex];
  const elapsed = 0; // not tracked in frontend

  return (
    <div className="anim-fade-in" style={{
      display: 'flex', flexDirection: 'column', gap: 16,
      background: 'linear-gradient(180deg, #0a0e1a 0%, #1a0a2e 60%, #0a0e1a 100%)',
      borderRadius: 14, padding: 16,
    }}>

      {/* Trophy header */}
      <div style={{ textAlign: 'center' }}>
        <div style={{ fontSize: 72, animation: 'bounce 1.2s ease-in-out infinite', filter: 'drop-shadow(0 0 30px #fbbf24)' }}>
          🏆
        </div>
        <div style={{
          fontFamily: "'Press Start 2P', monospace", fontSize: 13,
          color: '#fbbf24', marginTop: 10, lineHeight: 1.8,
          textShadow: '0 0 20px #fbbf24, 0 0 40px #f59e0b',
        }}>
          TOWER CLEARED!
        </div>
        <div style={{ fontSize: 13, color: '#e2e8f0', marginTop: 6, fontWeight: 700 }}>
          {run.playerName} 征服了全 {run.maxFloor} 層！
        </div>
      </div>

      {/* Stats row */}
      <div style={{ display: 'flex', gap: 8, justifyContent: 'center', flexWrap: 'wrap' }}>
        {[
          { label: '層數', value: `${run.maxFloor}/${run.maxFloor}`, color: '#fbbf24' },
          { label: '剩餘金幣', value: `${run.gold}💰`, color: '#4ade80' },
          { label: '首發HP', value: lead ? `${lead.currentHP}/${lead.maxHP}` : '?', color: lead && lead.currentHP / lead.maxHP > 0.5 ? '#4ade80' : lead && lead.currentHP / lead.maxHP > 0.25 ? '#f59e0b' : '#ef4444' },
        ].map(s => (
          <div key={s.label} style={{
            background: '#0f172a', border: `1px solid ${s.color}33`,
            borderRadius: 10, padding: '8px 16px', textAlign: 'center', flex: '1 0 80px',
          }}>
            <div style={{ fontSize: 10, color: '#475569', fontWeight: 700, marginBottom: 3 }}>{s.label}</div>
            <div style={{ fontSize: 15, fontWeight: 900, color: s.color }}>{s.value}</div>
          </div>
        ))}
      </div>

      {/* Surviving team */}
      <div style={{
        background: 'linear-gradient(135deg, #0f2a18, #0f172a)',
        border: '1px solid #22c55e66', borderRadius: 12, padding: '10px 12px',
        boxShadow: '0 0 12px #22c55e22',
      }}>
        <div style={{ fontSize: 10, color: '#22c55e', fontWeight: 700, marginBottom: 8 }}>👥 出戰隊伍</div>
        <div style={{ display: 'flex', gap: 10, justifyContent: 'center', flexWrap: 'wrap' }}>
          {run.team.map((p, i) => {
            const fainted = p.currentHP === 0;
            const hpPct = Math.max(0, Math.round(p.currentHP / p.maxHP * 100));
            const hpColor = hpPct > 50 ? '#22c55e' : hpPct > 25 ? '#f59e0b' : '#ef4444';
            return (
              <div key={i} style={{ textAlign: 'center', opacity: fainted ? 0.4 : 1 }}>
                <img src={spriteUrl(p.pokeId)} alt={p.name} style={{
                  width: 48, height: 48, imageRendering: 'pixelated',
                  filter: fainted ? 'grayscale(1)' : i === run.activeIndex ? 'drop-shadow(0 0 6px #fbbf24)' : undefined,
                }} />
                <div style={{ fontSize: 10, color: fainted ? '#475569' : '#e2e8f0', fontWeight: 700 }}>{p.displayName}</div>
                <div style={{ fontSize: 9, color: hpColor }}>{fainted ? 'FNT' : `${p.currentHP}/${p.maxHP}`}</div>
              </div>
            );
          })}
        </div>
      </div>

      {/* Reward note */}
      <div style={{
        background: '#1a1400', border: '1px solid #fbbf2444', borderRadius: 10,
        padding: '10px 14px', textAlign: 'center',
      }}>
        <div style={{ fontSize: 12, color: '#fbbf24', fontWeight: 700 }}>✨ 塔頂限定獎勵</div>
        <div style={{ fontSize: 11, color: '#94a3b8', marginTop: 4, lineHeight: 1.6 }}>
          下一次使用 /抓pokemon 保證閃光！（已記錄）
        </div>
      </div>

      {/* Divider with boss reveal */}
      <div style={{
        background: 'linear-gradient(135deg, #1c0a3a, #0a0e1a)',
        border: '1px solid #7c3aed55', borderRadius: 12, padding: '14px 16px',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 10 }}>
          <img src={spriteUrl(ARCEUS_ID)} alt="Arceus" style={{
            width: 64, height: 64, imageRendering: 'pixelated',
            filter: 'drop-shadow(0 0 12px #a855f7) brightness(0.7) sepia(1) hue-rotate(250deg)',
            animation: 'bounce 2s ease-in-out infinite',
          }} />
          <div>
            <div style={{ fontSize: 13, fontWeight: 900, color: '#c084fc' }}>★ 始祖神獸 ARCEUS ★</div>
            <div style={{ fontSize: 11, lineHeight: 1.9, marginTop: 3 }}>
              <span style={{ color: '#ef4444', fontWeight: 700 }}>HP 3200</span>
              {'  '}
              <span style={{ color: '#f97316', fontWeight: 700 }}>ATK 620</span>
              {'  '}
              <span style={{ color: '#60a5fa', fontWeight: 700 }}>DEF 480</span>
              {'  '}
              <span style={{ color: '#facc15', fontWeight: 700 }}>SPD 520</span>
            </div>
            <div style={{ fontSize: 10, color: '#a855f7', marginTop: 2, fontWeight: 700 }}>
              ⚠️ 從未被任何人擊敗過
            </div>
          </div>
        </div>
        <div style={{ fontSize: 11, color: '#94a3b8', lineHeight: 1.8 }}>
          🌌 塔頂的空間開始扭曲，時間本身在顫抖……<br />
          <span style={{ color: '#c084fc' }}>祂能操控所有屬性，技能威力無視防禦，每回合恢復 80HP。</span><br />
          <span style={{ color: '#64748b', fontSize: 10 }}>你的隊伍疲憊不堪，但祂卻如同剛剛甦醒。</span><br />
          <span style={{ color: '#f59e0b', fontWeight: 700 }}>⚠️ 擊敗或失敗都不會有任何額外獎勵。這是一場純粹的試煉。</span>
        </div>
      </div>

      {/* Choice buttons */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <button
          className="btn-hover"
          disabled={busy}
          onClick={() => onAction(`tower_bossChallenge_${run.channelId}_accept`)}
          style={{
            background: 'linear-gradient(135deg, #450a0a, #1a0000)',
            border: '2px solid #ef444488', borderRadius: 12,
            padding: '16px', cursor: busy ? 'not-allowed' : 'pointer',
            color: '#fff', fontWeight: 900, fontSize: 15,
            boxShadow: '0 0 20px #ef444422',
            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 10,
          }}
        >
          <span style={{ fontSize: 24 }}>⚔️</span>
          <div style={{ textAlign: 'left' }}>
            <div>接受挑戰</div>
            <div style={{ fontSize: 11, color: '#94a3b8', fontWeight: 400 }}>我知道沒有額外獎勵</div>
          </div>
        </button>

        <button
          className="btn-hover"
          disabled={busy}
          onClick={() => onAction(`tower_bossChallenge_${run.channelId}_decline`)}
          style={{
            background: 'linear-gradient(135deg, #052e16, #0a0e1a)',
            border: '2px solid #22c55e88', borderRadius: 12,
            padding: '16px', cursor: busy ? 'not-allowed' : 'pointer',
            color: '#fff', fontWeight: 900, fontSize: 15,
            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 10,
          }}
        >
          <span style={{ fontSize: 24 }}>🏠</span>
          <div style={{ textAlign: 'left' }}>
            <div>功成身退</div>
            <div style={{ fontSize: 11, color: '#94a3b8', fontWeight: 400 }}>帶著榮耀回家，正式通關</div>
          </div>
        </button>
      </div>
    </div>
  );
}
