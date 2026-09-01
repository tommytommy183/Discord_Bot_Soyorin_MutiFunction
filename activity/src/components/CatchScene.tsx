import { useState, useRef, useEffect } from 'react';
import { spriteUrl } from '../utils';
import type { TowerRun } from '../types';

interface Props {
  run: TowerRun;
  onAction: (customId: string) => void;
  busy: boolean;
  catchFailed?: boolean;
}

type CatchPhase = 'ready' | 'throwing' | 'wobbling' | 'escape' | 'done';

const BALL_EMOJI: Record<string, string> = { normal:'⚽', super:'🔵', ultra:'🟡', master:'🟣' };
const BALL_NAME: Record<string, string>  = { normal:'普通球', super:'超級球', ultra:'高級球', master:'大師球' };
const BALL_RATE: Record<string, number>  = { normal:30, super:55, ultra:75, master:100 };

export function CatchScene({ run, onAction, busy, catchFailed }: Props) {
  const enemy = run.currentEnemy;
  const balls = run.balls ?? {};
  const [phase, setPhase] = useState<CatchPhase>('ready');
  const [ballEmoji, setBallEmoji] = useState('⚽');
  const [escapeText, setEscapeText] = useState('');
  const throwing = useRef(false);

  // When parent signals catch failed, show escape text
  useEffect(() => {
    if (catchFailed && enemy) {
      setEscapeText(`${enemy.name}掙扎著逃了出來，真是囂張的傢伙！`);
      setPhase('escape');
      throwing.current = false;
      setTimeout(() => { setEscapeText(''); setPhase('ready'); }, 2500);
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [catchFailed]);

  const availableBalls = Object.entries(balls).filter(([, cnt]) => cnt > 0);
  const isAnimating = phase !== 'ready' && phase !== 'done';

  function handleThrow(ballKey: string) {
    if (isAnimating || busy || throwing.current) return;
    throwing.current = true;
    setBallEmoji(BALL_EMOJI[ballKey] ?? '⚾');
    setPhase('throwing');

    // throwing → wobbling after ball arrives
    setTimeout(() => setPhase('wobbling'), 800);

    // wobbling → API call
    setTimeout(() => {
      onAction(`tower_catch_${run.channelId}_${ballKey}`);
      throwing.current = false;
      setPhase('done');
    }, 2000);
  }

  function handlePass() {
    if (isAnimating || busy) return;
    onAction(`tower_catch_${run.channelId}_pass`);
  }

  if (!enemy) return null;

  // Determine if throwing phase ball x position (CSS animation handles y arc)
  const ballStyle: React.CSSProperties = {
    position: 'absolute',
    fontSize: 26,
    zIndex: 20,
    pointerEvents: 'none',
    ...(phase === 'throwing' ? {
      animation: 'ballArc 0.8s cubic-bezier(.2,.9,.7,.9) forwards',
    } : phase === 'wobbling' ? {
      left: 'calc(58% + 10px)',
      bottom: 100,
      animation: 'shake 0.35s ease-in-out 4',
    } : { display: 'none' }),
  };

  return (
    <div className="anim-fade-in" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      {/* Title */}
      <div style={{ textAlign: 'center', padding: '4px 0' }}>
        <div style={{ fontSize: 18, fontWeight: 900, color: '#ec4899', textShadow: '0 0 12px #ec489966' }}>
          ⚾ 野生 {enemy.name} 出現了！
        </div>
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
        height: 210,
        overflow: 'hidden',
      }}>
        {/* Ground */}
        <div style={{
          position: 'absolute', bottom: 58, left: 0, right: 0,
          height: 2, background: 'linear-gradient(90deg, transparent, #1e293b 30%, #1e293b 70%, transparent)',
        }} />

        {/* Enemy Pokemon */}
        <img
          src={spriteUrl(enemy.pokeId, 'front')}
          alt={enemy.name}
          style={{
            position: 'absolute', top: 20, right: 28,
            imageRendering: 'pixelated',
            width: 120, height: 120,
            filter: phase === 'wobbling'
              ? 'drop-shadow(0 0 20px #ec4899) brightness(1.4)'
              : phase === 'escape'
              ? 'drop-shadow(0 0 8px #fff) brightness(1.2)'
              : 'drop-shadow(0 4px 12px rgba(0,0,0,0.7))',
            animation: phase === 'wobbling'
              ? 'shake 0.3s ease-in-out 5'
              : phase === 'escape'
              ? 'bounce 0.3s ease-in-out 3'
              : 'bounce 2.5s ease-in-out infinite',
            transition: 'filter 0.3s',
          }}
        />

        {/* Pokéball — arc animation from left to enemy */}
        {(phase === 'throwing' || phase === 'wobbling') && (
          <div style={ballStyle}>
            {ballEmoji}
          </div>
        )}

        {/* Escape floating text */}
        {phase === 'escape' && escapeText && (
          <div style={{
            position: 'absolute',
            top: 20, left: 8, right: 8,
            textAlign: 'center',
            fontSize: 12, fontWeight: 900, color: '#fbbf24',
            textShadow: '0 0 8px #f59e0b',
            animation: 'floatText 2.2s ease-out forwards',
            zIndex: 20,
            lineHeight: 1.5,
          }}>
            {escapeText}
          </div>
        )}

        {/* HP bar */}
        <div style={{ position: 'absolute', bottom: 8, left: 12, right: 12 }}>
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
            <span style={{ fontSize: 24 }}>{BALL_EMOJI[key]}</span>
            <span style={{ fontWeight: 700, fontSize: 12 }}>{BALL_NAME[key]}</span>
            <span style={{ fontSize: 10, color: '#94a3b8' }}>剩餘×{cnt}・捕獲率 {BALL_RATE[key]}%</span>
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

      {/* Status hint */}
      {phase === 'throwing' && (
        <div style={{ textAlign: 'center', color: '#ec4899', fontSize: 12, fontWeight: 700, animation: 'pulse 0.5s ease-in-out infinite' }}>
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
