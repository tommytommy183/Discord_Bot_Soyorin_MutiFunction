import type { TowerRun } from '../types';

const PATH_COLORS: Record<string, string> = {
  '⚔️': '#ef4444', '🛍️': '#3b82f6', '🏕️': '#22c55e',
  '🎉': '#a855f7', '🎰': '#f59e0b', '💀': '#7c3aed',
  '🌟': '#fbbf24', '📦': '#0ea5e9', '🔮': '#8b5cf6',
};

interface Props {
  run: TowerRun;
  onAction: (customId: string) => void;
  busy: boolean;
}

export function PathSelector({ run, onAction, busy }: Props) {
  const floor = run.currentFloor + 1;
  const isBoss = floor % 10 === 0;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16, alignItems: 'center' }}>
      {/* 進度條 */}
      <div style={{ width: '100%' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, color: '#94a3b8', marginBottom: 4 }}>
          <span>進度</span>
          <span>{run.currentFloor} / {run.maxFloor}</span>
        </div>
        <div style={{ height: 8, background: '#1e293b', borderRadius: 4, overflow: 'hidden' }}>
          <div style={{
            height: '100%',
            width: `${(run.currentFloor / run.maxFloor) * 100}%`,
            background: 'linear-gradient(90deg, #6366f1, #a855f7)',
            borderRadius: 4, transition: 'width 0.5s ease',
          }} />
        </div>
      </div>

      <div style={{ textAlign: 'center' }}>
        <div style={{ fontSize: 22, fontWeight: 700, color: isBoss ? '#ef4444' : '#fff' }}>
          {isBoss ? '⚠️ 準備進入 BOSS 層！' : `第 ${floor} 層 — 選擇路線`}
        </div>
        <div style={{ color: '#94a3b8', fontSize: 13, marginTop: 4 }}>
          💰 {run.gold} 金幣
        </div>
      </div>

      {/* 路線卡片 */}
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', justifyContent: 'center' }}>
        {run.pathOptions.map((opt) => {
          const btnColor = Object.entries(PATH_COLORS).find(([k]) => opt.emoji?.includes(k))?.[1] ?? '#4b5563';
          return (
            <button
              key={opt.customId}
              disabled={busy}
              onClick={() => onAction(opt.customId)}
              style={{
                background: `linear-gradient(135deg, ${btnColor}33, ${btnColor}11)`,
                border: `1px solid ${btnColor}88`,
                borderRadius: 12,
                padding: '16px 20px',
                cursor: busy ? 'not-allowed' : 'pointer',
                color: '#fff',
                minWidth: 130,
                display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6,
                transition: 'all 0.15s',
              }}
              onMouseEnter={e => { if (!busy) (e.currentTarget as HTMLElement).style.transform = 'translateY(-2px)'; }}
              onMouseLeave={e => { (e.currentTarget as HTMLElement).style.transform = 'none'; }}
            >
              <span style={{ fontSize: 28 }}>{opt.emoji}</span>
              <span style={{ fontWeight: 700, fontSize: 14 }}>{opt.label}</span>
              {opt.description && <span style={{ fontSize: 11, color: '#94a3b8', textAlign: 'center' }}>{opt.description}</span>}
            </button>
          );
        })}
      </div>

      {/* 隊伍預覽 */}
      <div style={{ width: '100%', background: '#0f172a', borderRadius: 10, padding: 10 }}>
        <div style={{ color: '#94a3b8', fontSize: 12, marginBottom: 6 }}>🎒 目前隊伍</div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          {run.team.map((p, i) => (
            <div key={i} style={{
              display: 'flex', alignItems: 'center', gap: 6,
              background: p.currentHP === 0 ? '#1a1a1a' : '#1e293b',
              borderRadius: 8, padding: '4px 10px',
              opacity: p.currentHP === 0 ? 0.4 : 1,
            }}>
              <img
                src={`https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/${p.pokeId}.png`}
                alt={p.name}
                style={{ width: 32, height: 32, imageRendering: 'pixelated' }}
              />
              <div>
                <div style={{ fontSize: 12, color: '#fff', fontWeight: 600 }}>{p.displayName}</div>
                <div style={{ fontSize: 11, color: p.currentHP === 0 ? '#ef4444' : '#4ade80' }}>
                  {p.currentHP === 0 ? 'FNT' : `${p.currentHP}/${p.maxHP}`}
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
