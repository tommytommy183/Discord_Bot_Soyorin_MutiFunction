import { spriteUrl } from '../utils';
import type { TowerRun } from '../types';

interface Props {
  run: TowerRun;
  onRestart: () => void;
}

export function GameOver({ run, onRestart }: Props) {
  const isVictory = run.state === 'Victory';

  return (
    <div className="anim-fade-in" style={{
      textAlign: 'center', display: 'flex', flexDirection: 'column',
      gap: 16, alignItems: 'center', padding: '24px 20px',
    }}>

      {/* Big icon */}
      <div style={{ fontSize: 72, animation: isVictory ? 'bounce 1.5s ease-in-out infinite' : undefined }}>
        {isVictory ? '🏆' : '💀'}
      </div>

      {/* Title */}
      <div style={{
        fontFamily: "'Press Start 2P', monospace",
        fontSize: isVictory ? 14 : 12,
        color: isVictory ? '#fbbf24' : '#ef4444',
        letterSpacing: '0.06em',
        lineHeight: 1.5,
        animation: isVictory ? 'glow 2s ease-in-out infinite' : undefined,
      }}>
        {isVictory ? 'CONGRATULATIONS!' : 'GAME OVER'}
      </div>

      {/* Subtitle */}
      <div style={{ color: '#94a3b8', fontSize: 13, lineHeight: 1.6 }}>
        {isVictory
          ? <>🎉 <strong>{run.playerName}</strong> 完成了 {run.maxFloor} 層的挑戰！</>
          : <><strong>{run.playerName}</strong> 在第 <strong style={{ color: '#ef4444' }}>{run.currentFloor}</strong> 層倒下了。</>}
      </div>

      {/* Surviving team */}
      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', justifyContent: 'center' }}>
        {run.team.map((p, i) => {
          const fainted = p.currentHP === 0;
          return (
            <div key={i} style={{
              display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4,
              background: fainted ? '#0a0a0a' : '#0f172a',
              borderRadius: 10, padding: '8px 12px',
              border: `1px solid ${fainted ? '#1e1e1e' : '#22c55e33'}`,
              opacity: fainted ? 0.4 : 1,
              minWidth: 70,
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

      {/* Run log */}
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
          background: isVictory
            ? 'linear-gradient(135deg, #6366f1, #a855f7)'
            : 'linear-gradient(135deg, #dc2626, #ef4444)',
          color: '#fff', border: 'none', borderRadius: 12,
          padding: '14px 36px', fontSize: 15, fontWeight: 700,
          cursor: 'pointer',
          boxShadow: isVictory ? '0 4px 20px #6366f155' : '0 4px 20px #ef444455',
        }}
      >
        {isVictory ? '🏆 再挑一次！' : '🔄 再來一次'}
      </button>
    </div>
  );
}
