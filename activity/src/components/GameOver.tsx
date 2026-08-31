import type { TowerRun } from '../types';

interface Props {
  run: TowerRun;
  onRestart: () => void;
}

export function GameOver({ run, onRestart }: Props) {
  const isVictory = run.state === 'Victory';

  return (
    <div style={{ textAlign: 'center', display: 'flex', flexDirection: 'column', gap: 20, alignItems: 'center', padding: 32 }}>
      <div style={{ fontSize: 64 }}>{isVictory ? '🏆' : '💀'}</div>
      <div style={{ fontSize: 28, fontWeight: 900, color: isVictory ? '#fbbf24' : '#ef4444' }}>
        {isVictory ? '攻略成功！' : '全滅...'}
      </div>
      <div style={{ color: '#94a3b8', fontSize: 14 }}>
        {isVictory
          ? `恭喜 ${run.playerName} 完成了 ${run.maxFloor} 層的挑戰！`
          : `${run.playerName} 在第 ${run.currentFloor} 層倒下了。`}
      </div>

      {/* 最後日誌 */}
      <div style={{
        background: '#0f172a', borderRadius: 8, padding: 12,
        width: '100%', maxWidth: 360,
        fontSize: 12, color: '#94a3b8', textAlign: 'left',
        maxHeight: 150, overflowY: 'auto',
      }}>
        {run.runLog.slice(-10).reverse().map((l, i) => (
          <div key={i} style={{ marginBottom: 3 }}>{l}</div>
        ))}
      </div>

      <button
        onClick={onRestart}
        style={{
          background: isVictory ? '#6366f1' : '#ef4444',
          color: '#fff', border: 'none', borderRadius: 10,
          padding: '12px 32px', fontSize: 16, fontWeight: 700,
          cursor: 'pointer',
        }}
      >
        {isVictory ? '再挑戰！' : '再來一次'}
      </button>
    </div>
  );
}
