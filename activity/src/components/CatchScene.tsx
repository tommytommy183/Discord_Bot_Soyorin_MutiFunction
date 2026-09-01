import { useState } from 'react';
import { spriteUrl } from '../utils';
import type { TowerRun } from '../types';

interface Props {
  run: TowerRun;
  onAction: (customId: string) => void;
  busy: boolean;
}

type CatchPhase = 'ready' | 'throwing' | 'wobbling' | 'success' | 'fail';

const BALL_EMOJI: Record<string, string> = {
  normal: '⚽',
  super: '🔵',
  ultra: '🟡',
  master: '🟣',
};
const BALL_NAME: Record<string, string> = {
  normal: '普通球',
  super: '超級球',
  ultra: '高級球',
  master: '大師球',
};
const BALL_RATE: Record<string, number> = {
  normal: 30,
  super: 55,
  ultra: 75,
  master: 100,
};

export function CatchScene({ run, onAction, busy }: Props) {
  const enemy = run.currentEnemy;
  const balls = run.balls ?? {};
  const [phase, setPhase] = useState<CatchPhase>('ready');
  const [selectedBall, setSelectedBall] = useState<string | null>(null);

  const availableBalls = Object.entries(balls).filter(([, cnt]) => cnt > 0);
  const isAnimating = phase !== 'ready';

  function handleThrow(ballKey: string) {
    if (isAnimating || busy) return;
    setSelectedBall(ballKey);
    setPhase('throwing');

    // Simulate ball animation phases then call API
    setTimeout(() => setPhase('wobbling'), 600);
    setTimeout(() => {
      // Actually send the action — result determines success/fail visually
      // We'll just transition to calling the server; the busy overlay will take over
      onAction(`tower_catch_${run.channelId}_${ballKey}`);
      setPhase('ready');
    }, 1800);
  }

  function handlePass() {
    if (isAnimating || busy) return;
    onAction(`tower_catch_${run.channelId}_pass`);
  }

  if (!enemy) return null;

  return (
    <div className="anim-fade-in" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      {/* Title */}
      <div style={{ textAlign: 'center', padding: '4px 0' }}>
        <div style={{
          fontSize: 18, fontWeight: 900, color: '#ec4899',
          textShadow: '0 0 12px #ec489966',
        }}>⚾ 野生 {enemy.name} 出現了！</div>
        <div style={{ fontSize: 11, color: '#64748b', marginTop: 3 }}>
          HP: {enemy.currentHP}/{enemy.maxHP}
        </div>
      </div>

      {/* Arena */}
      <div style={{
        position: 'relative',
        background: 'radial-gradient(ellipse at 50% 40%, #1a0a2e 0%, #0a0e1a 100%)',
        borderRadius: 14,
        border: '1px solid #ec489933',
        height: 200,
        overflow: 'hidden',
      }}>
        {/* Ground line */}
        <div style={{
          position: 'absolute', bottom: 60, left: 0, right: 0,
          height: 2, background: 'linear-gradient(90deg, transparent, #1e293b, transparent)',
        }} />

        {/* Enemy Pokemon */}
        <img
          src={spriteUrl(enemy.pokeId, 'front')}
          alt={enemy.name}
          style={{
            position: 'absolute', top: 24, right: 36,
            imageRendering: 'pixelated',
            width: 112, height: 112,
            filter: phase === 'wobbling'
              ? 'drop-shadow(0 0 18px #ec4899) brightness(1.3)'
              : 'drop-shadow(0 4px 12px rgba(0,0,0,0.6))',
            animation: phase === 'wobbling'
              ? 'shake 0.4s ease-in-out 3'
              : phase === 'success'
              ? 'flash 0.3s ease-in-out 4'
              : 'bounce 2s ease-in-out infinite',
            transition: 'filter 0.2s',
            opacity: phase === 'success' ? 0.2 : 1,
          }}
        />

        {/* Pokéball — only visible during throw */}
        {(phase === 'throwing' || phase === 'wobbling') && selectedBall && (
          <div style={{
            position: 'absolute',
            bottom: 68,
            left: phase === 'throwing' ? 24 : 'calc(60% - 16px)',
            fontSize: 28,
            transition: phase === 'throwing' ? 'left 0.5s ease-in, bottom 0.5s ease-in' : undefined,
            animation: phase === 'wobbling' ? 'shake 0.35s ease-in-out 2' : 'none',
            zIndex: 10,
          }}>
            {BALL_EMOJI[selectedBall] ?? '⚾'}
          </div>
        )}

        {/* HP bar overlay */}
        <div style={{
          position: 'absolute', bottom: 8, left: 12, right: 12,
        }}>
          <div style={{ height: 6, borderRadius: 3, background: '#1e293b', overflow: 'hidden' }}>
            <div style={{
              height: '100%', borderRadius: 3,
              width: `${Math.round(enemy.currentHP / enemy.maxHP * 100)}%`,
              background: enemy.currentHP / enemy.maxHP > 0.5 ? '#22c55e'
                : enemy.currentHP / enemy.maxHP > 0.2 ? '#facc15' : '#ef4444',
              transition: 'width 0.5s ease',
            }} />
          </div>
        </div>
      </div>

      {/* Ball buttons */}
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
        {availableBalls.map(([key, cnt]) => (
          <button
            key={key}
            className="btn-hover"
            disabled={isAnimating || busy}
            onClick={() => handleThrow(key)}
            style={{
              flex: '1 1 calc(50% - 3px)',
              background: 'linear-gradient(135deg, #500724 0%, #2d0a20 100%)',
              border: '1px solid #ec489955',
              borderRadius: 10, padding: '10px 8px',
              color: '#fff', cursor: isAnimating || busy ? 'not-allowed' : 'pointer',
              display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2,
              opacity: isAnimating || busy ? 0.55 : 1,
            }}
          >
            <span style={{ fontSize: 22 }}>{BALL_EMOJI[key]}</span>
            <span style={{ fontWeight: 700, fontSize: 12 }}>{BALL_NAME[key]}</span>
            <span style={{ fontSize: 10, color: '#94a3b8' }}>
              剩餘×{cnt}・捕獲率 {BALL_RATE[key]}%
            </span>
          </button>
        ))}
        <button
          className="btn-hover"
          disabled={isAnimating || busy}
          onClick={handlePass}
          style={{
            flex: '1 1 100%',
            background: '#0f172a', border: '1px solid #33415555',
            borderRadius: 10, padding: '10px 8px',
            color: '#64748b', cursor: isAnimating || busy ? 'not-allowed' : 'pointer',
            fontWeight: 700, fontSize: 13,
            opacity: isAnimating || busy ? 0.55 : 1,
          }}
        >
          🚫 放過，繼續前進
        </button>
      </div>

      {/* Animation hint */}
      {phase === 'throwing' && (
        <div style={{ textAlign: 'center', color: '#ec4899', fontSize: 12, fontWeight: 700, animation: 'pulse 0.6s ease-in-out infinite' }}>
          球飛出去了！
        </div>
      )}
      {phase === 'wobbling' && (
        <div style={{ textAlign: 'center', color: '#fbbf24', fontSize: 12, fontWeight: 700, animation: 'pulse 0.4s ease-in-out infinite' }}>
          搖晃中…
        </div>
      )}
    </div>
  );
}
