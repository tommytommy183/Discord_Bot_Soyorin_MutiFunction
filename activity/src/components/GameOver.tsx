import { spriteUrl } from '../utils';
import type { TowerRun } from '../types';

interface Props {
  run: TowerRun;
  onRestart: () => void;
}

// Confetti-like decoration for victory
function VictoryStars() {
  const stars = ['⭐','🌟','✨','💫','🎊','🎉','🏅','🎆'];
  return (
    <div style={{
      display: 'flex', gap: 6, flexWrap: 'wrap', justifyContent: 'center',
      fontSize: 18, padding: '4px 0',
    }}>
      {stars.map((s, i) => (
        <span key={i} style={{
          animation: `bounce ${0.8 + i * 0.12}s ease-in-out ${i * 0.08}s infinite`,
          display: 'inline-block',
        }}>{s}</span>
      ))}
    </div>
  );
}

export function GameOver({ run, onRestart }: Props) {
  const isVictory = run.state === 'Victory';
  const survivingCount = run.team.filter(p => p.currentHP > 0).length;
  const teamSize = run.team.length;

  if (isVictory) {
    return (
      <div className="anim-fade-in" style={{
        textAlign: 'center', display: 'flex', flexDirection: 'column',
        gap: 14, alignItems: 'center', padding: '20px 18px',
        background: 'linear-gradient(180deg, #0a0e1a 0%, #1a0a2e 60%, #0a0e1a 100%)',
        minHeight: '100%',
      }}>

        {/* Confetti top */}
        <VictoryStars />

        {/* Trophy */}
        <div style={{ position: 'relative' }}>
          <div style={{
            fontSize: 80,
            animation: 'bounce 1.2s ease-in-out infinite',
            filter: 'drop-shadow(0 0 30px #fbbf24)',
            lineHeight: 1,
          }}>🏆</div>
          <div style={{
            position: 'absolute', top: -6, left: -6, right: -6, bottom: -6,
            borderRadius: '50%',
            background: 'radial-gradient(circle, #fbbf2422 0%, transparent 70%)',
            animation: 'pulse 1.5s ease-in-out infinite',
          }} />
        </div>

        {/* Title */}
        <div>
          <div style={{
            fontFamily: "'Press Start 2P', monospace",
            fontSize: 13, color: '#fbbf24',
            letterSpacing: '0.08em', lineHeight: 1.6,
            textShadow: '0 0 20px #fbbf24, 0 0 40px #f59e0b',
            animation: 'pulse 2s ease-in-out infinite',
          }}>
            TOWER CLEARED!
          </div>
          <div style={{ color: '#e2e8f0', fontSize: 14, fontWeight: 900, marginTop: 6 }}>
            🎉 {run.playerName} 征服了爬塔！
          </div>
        </div>

        {/* Stats row */}
        <div style={{
          display: 'flex', gap: 8, width: '100%', maxWidth: 360,
        }}>
          {[
            { emoji: '🏔️', label: '通關層數', value: `${run.maxFloor} 層` },
            { emoji: '💰', label: '金幣', value: `${run.gold}` },
            { emoji: '💚', label: '倖存隊員', value: `${survivingCount}/${teamSize}` },
          ].map(stat => (
            <div key={stat.label} style={{
              flex: 1, background: '#0f172a',
              border: '1px solid #fbbf2433', borderRadius: 10,
              padding: '10px 6px', textAlign: 'center',
            }}>
              <div style={{ fontSize: 20, marginBottom: 4 }}>{stat.emoji}</div>
              <div style={{ fontSize: 14, fontWeight: 900, color: '#fbbf24' }}>{stat.value}</div>
              <div style={{ fontSize: 9, color: '#64748b', marginTop: 2 }}>{stat.label}</div>
            </div>
          ))}
        </div>

        {/* Surviving team */}
        <div style={{ width: '100%', maxWidth: 360 }}>
          <div style={{ fontSize: 11, color: '#64748b', fontWeight: 700, marginBottom: 8, letterSpacing: '0.05em' }}>
            ── 最終隊伍 ──
          </div>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', justifyContent: 'center' }}>
            {run.team.map((p, i) => {
              const fainted = p.currentHP === 0;
              return (
                <div key={i} style={{
                  display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4,
                  background: fainted ? '#0a0a0a' : 'linear-gradient(135deg, #0f2a18, #0f172a)',
                  borderRadius: 10, padding: '10px 12px',
                  border: `1px solid ${fainted ? '#1e1e1e' : '#22c55e66'}`,
                  opacity: fainted ? 0.4 : 1,
                  minWidth: 72,
                  boxShadow: fainted ? 'none' : '0 0 12px #22c55e22',
                }}>
                  <img src={spriteUrl(p.pokeId)} alt={p.name}
                    style={{ width: 44, height: 44, imageRendering: 'pixelated',
                      filter: fainted ? 'grayscale(1)' : 'drop-shadow(0 0 6px #22c55e)',
                      animation: fainted ? undefined : 'bounce 2s ease-in-out infinite',
                    }} />
                  <div style={{ fontSize: 10, color: '#94a3b8' }}>{p.displayName}</div>
                  <div style={{ fontSize: 10, color: fainted ? '#ef4444' : '#4ade80', fontWeight: 700 }}>
                    {fainted ? 'FNT' : `${p.currentHP}/${p.maxHP}`}
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        {/* Run log */}
        <div style={{
          background: '#07090f', borderRadius: 8, padding: 10,
          width: '100%', maxWidth: 360,
          fontSize: 11, color: '#475569', textAlign: 'left',
          maxHeight: 100, overflowY: 'auto', lineHeight: 1.7,
          border: '1px solid #1e293b',
        }}>
          {run.runLog.slice(-10).reverse().map((l, i) => (
            <div key={i} style={{ opacity: 1 - i * 0.08 }}>{l}</div>
          ))}
        </div>

        <VictoryStars />

        <button
          className="btn-hover"
          onClick={onRestart}
          style={{
            background: 'linear-gradient(135deg, #fbbf24, #f59e0b, #6366f1)',
            color: '#000', border: 'none', borderRadius: 14,
            padding: '16px 40px', fontSize: 16, fontWeight: 900,
            cursor: 'pointer',
            boxShadow: '0 4px 24px #fbbf2455, 0 0 40px #6366f133',
            letterSpacing: '0.03em',
          }}
        >
          🏆 再挑一次！
        </button>
      </div>
    );
  }

  // ── Defeat screen ─────────────────────────────────────────────────────────
  return (
    <div className="anim-fade-in" style={{
      textAlign: 'center', display: 'flex', flexDirection: 'column',
      gap: 16, alignItems: 'center', padding: '24px 20px',
    }}>
      <div style={{ fontSize: 72 }}>💀</div>

      <div style={{
        fontFamily: "'Press Start 2P', monospace",
        fontSize: 12, color: '#ef4444', letterSpacing: '0.06em', lineHeight: 1.5,
      }}>
        GAME OVER
      </div>

      <div style={{ color: '#94a3b8', fontSize: 13, lineHeight: 1.6 }}>
        <strong>{run.playerName}</strong> 在第{' '}
        <strong style={{ color: '#ef4444' }}>{run.currentFloor}</strong> 層倒下了。
      </div>

      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', justifyContent: 'center' }}>
        {run.team.map((p, i) => {
          const fainted = p.currentHP === 0;
          return (
            <div key={i} style={{
              display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4,
              background: fainted ? '#0a0a0a' : '#0f172a',
              borderRadius: 10, padding: '8px 12px',
              border: `1px solid ${fainted ? '#1e1e1e' : '#22c55e33'}`,
              opacity: fainted ? 0.4 : 1, minWidth: 70,
            }}>
              <img src={spriteUrl(p.pokeId)} alt={p.name}
                style={{ width: 40, height: 40, imageRendering: 'pixelated', filter: fainted ? 'grayscale(1)' : undefined }} />
              <div style={{ fontSize: 10, color: '#94a3b8' }}>{p.displayName}</div>
              <div style={{ fontSize: 10, color: fainted ? '#ef4444' : '#4ade80', fontWeight: 700 }}>
                {fainted ? 'FNT' : `${p.currentHP}/${p.maxHP}`}
              </div>
            </div>
          );
        })}
      </div>

      <div style={{
        background: '#07090f', borderRadius: 8, padding: 12,
        width: '100%', maxWidth: 400,
        fontSize: 11, color: '#475569', textAlign: 'left',
        maxHeight: 130, overflowY: 'auto', lineHeight: 1.7,
        border: '1px solid #1e293b',
      }}>
        {run.runLog.slice(-12).reverse().map((l, i) => (
          <div key={i} style={{ opacity: 1 - i * 0.07 }}>{l}</div>
        ))}
      </div>

      <button
        className="btn-hover"
        onClick={onRestart}
        style={{
          background: 'linear-gradient(135deg, #dc2626, #ef4444)',
          color: '#fff', border: 'none', borderRadius: 12,
          padding: '14px 36px', fontSize: 15, fontWeight: 700,
          cursor: 'pointer',
          boxShadow: '0 4px 20px #ef444455',
        }}
      >
        🔄 再來一次
      </button>
    </div>
  );
}
