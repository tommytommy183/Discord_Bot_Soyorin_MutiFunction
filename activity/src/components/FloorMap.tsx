import { useState } from 'react';
import type { TowerRun } from '../types';

// 固定樓層類型（來自 GenPaths 邏輯）
function getFixedFloorType(floor: number): string | null {
  if (floor % 10 === 0) return 'battle';  // BOSS
  if (floor === 9 || floor === 19) return 'shop_rest';  // pre-boss
  if (floor === 7 || floor === 17) return 'cursed_relic';
  return null;
}

interface FloorInfo {
  floor: number;
  type: 'done' | 'current' | 'upcoming';
  choice?: string;       // 已走過的選擇
  fixedType?: string;    // 固定樓層類型
  isBoss: boolean;
}

const CHOICE_CONFIG: Record<string, { emoji: string; color: string; label: string }> = {
  battle:         { emoji: '⚔️', color: '#ef4444', label: '戰鬥' },
  boss:           { emoji: '💀', color: '#7c3aed', label: 'BOSS' },
  shop:           { emoji: '🛍️', color: '#3b82f6', label: '商店' },
  rest:           { emoji: '🏕️', color: '#22c55e', label: '休息' },
  event:          { emoji: '🎉', color: '#a855f7', label: '事件' },
  casino:         { emoji: '🎰', color: '#f59e0b', label: '賭場' },
  relic:          { emoji: '🔮', color: '#8b5cf6', label: '遺物' },
  cursed_relic:   { emoji: '💀', color: '#dc2626', label: '詛咒' },
  shop_rest:      { emoji: '🏕️🛍️', color: '#0ea5e9', label: '補給' },
  '?':            { emoji: '❓', color: '#475569', label: '未知' },
};

function getConfig(key: string | undefined) {
  if (!key) return CHOICE_CONFIG['?'];
  return CHOICE_CONFIG[key] ?? CHOICE_CONFIG['?'];
}

interface Props {
  run: TowerRun;
}

export function FloorMap({ run }: Props) {
  const [expanded, setExpanded] = useState(false);

  // Build floor info array
  const floors: FloorInfo[] = Array.from({ length: run.maxFloor }, (_, i) => {
    const floor = i + 1;
    const historyIdx = floor - 1;
    const isBoss = floor % 10 === 0;

    if (floor < run.currentFloor) {
      return {
        floor, type: 'done',
        choice: run.floorHistory?.[historyIdx] ?? (isBoss ? 'battle' : undefined),
        fixedType: getFixedFloorType(floor) ?? undefined,
        isBoss,
      };
    } else if (floor === run.currentFloor) {
      return {
        floor, type: 'current',
        choice: run.floorHistory?.[historyIdx],
        fixedType: getFixedFloorType(floor) ?? undefined,
        isBoss,
      };
    } else {
      return {
        floor, type: 'upcoming',
        fixedType: getFixedFloorType(floor) ?? undefined,
        isBoss,
      };
    }
  });

  // Mini view: dots
  const miniView = (
    <div style={{ display: 'flex', gap: 3, alignItems: 'center', flexWrap: 'wrap' }}>
      {floors.map(f => {
        const cfg = getConfig(f.type === 'done' ? (f.isBoss ? 'boss' : f.choice ?? f.fixedType) : f.type === 'current' ? 'current' : f.isBoss ? 'boss' : f.fixedType);
        const isCurrent = f.type === 'current';
        const isDone = f.type === 'done';
        return (
          <div
            key={f.floor}
            title={`第${f.floor}層${f.choice ? ` · ${getConfig(f.choice).label}` : ''}`}
            style={{
              width: isCurrent ? 14 : f.isBoss ? 10 : 7,
              height: isCurrent ? 14 : f.isBoss ? 10 : 7,
              borderRadius: '50%',
              background: isCurrent
                ? '#6366f1'
                : isDone
                  ? (f.isBoss ? '#7c3aed' : '#334155')
                  : f.isBoss
                    ? '#7c3aed44'
                    : '#1e293b',
              border: isCurrent ? '2px solid #a5b4fc' : f.isBoss ? '1px solid #7c3aed' : 'none',
              boxShadow: isCurrent ? '0 0 6px #6366f1' : undefined,
              flexShrink: 0,
            }}
          />
        );
      })}
    </div>
  );

  if (!expanded) {
    return (
      <div
        onClick={() => setExpanded(true)}
        style={{
          background: '#07090f', border: '1px solid #1e293b', borderRadius: 8,
          padding: '6px 12px', cursor: 'pointer',
          display: 'flex', alignItems: 'center', gap: 10,
        }}
      >
        <span style={{ fontSize: 12, color: '#475569', flexShrink: 0 }}>🗺️ 地圖</span>
        {miniView}
        <span style={{ fontSize: 10, color: '#334155', marginLeft: 'auto', flexShrink: 0 }}>▼</span>
      </div>
    );
  }

  // Expanded: show full floor list in groups of 5
  const groups: FloorInfo[][] = [];
  for (let i = 0; i < floors.length; i += 5) {
    groups.push(floors.slice(i, i + 5));
  }

  return (
    <div style={{
      background: '#07090f', border: '1px solid #1e293b', borderRadius: 8,
      padding: '10px 12px',
    }}>
      {/* Header */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
        <span style={{ fontSize: 12, fontWeight: 700, color: '#94a3b8' }}>🗺️ 塔樓地圖</span>
        <button
          onClick={() => setExpanded(false)}
          style={{ background: 'none', border: 'none', color: '#475569', cursor: 'pointer', fontSize: 14, padding: '0 4px' }}
        >▲</button>
      </div>

      {/* Groups */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        {[...groups].reverse().map((grp, gi) => (
          <div key={gi} style={{ display: 'flex', gap: 6, alignItems: 'center', flexDirection: 'row-reverse' }}>
            {grp.map(f => {
              const choiceKey = f.type === 'done' ? (f.isBoss ? 'boss' : f.choice ?? f.fixedType) : f.type === 'upcoming' ? (f.isBoss ? 'boss' : f.fixedType) : 'current';
              const cfg = getConfig(choiceKey);
              const isCurrent = f.type === 'current';
              const isDone = f.type === 'done';
              const isUpcoming = f.type === 'upcoming';

              return (
                <div
                  key={f.floor}
                  title={`第${f.floor}層`}
                  style={{
                    flex: 1,
                    display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 3,
                    background: isCurrent
                      ? '#1e1b4b'
                      : isDone
                        ? '#0f172a'
                        : '#060810',
                    border: `1px solid ${isCurrent ? '#6366f1' : isDone ? '#1e293b' : '#0f172a'}`,
                    borderRadius: 8, padding: '6px 4px',
                    opacity: isUpcoming && !f.isBoss && !f.fixedType ? 0.5 : 1,
                    boxShadow: isCurrent ? '0 0 8px #6366f144' : undefined,
                  }}
                >
                  <span style={{ fontSize: isUpcoming ? 12 : 14 }}>
                    {isCurrent
                      ? '📍'
                      : isDone
                        ? cfg.emoji
                        : f.isBoss
                          ? '💀'
                          : f.fixedType
                            ? getConfig(f.fixedType).emoji
                            : '❓'}
                  </span>
                  <span style={{
                    fontSize: 9, fontWeight: 700,
                    color: isCurrent ? '#818cf8' : isDone ? '#475569' : '#334155',
                  }}>
                    {f.floor}
                  </span>
                  {isCurrent && (
                    <span style={{ fontSize: 8, color: '#6366f1' }}>NOW</span>
                  )}
                  {isDone && f.choice && (
                    <span style={{ fontSize: 8, color: cfg.color, textAlign: 'center' }}>{cfg.label}</span>
                  )}
                </div>
              );
            })}
          </div>
        ))}
      </div>

      {/* Legend */}
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 10, paddingTop: 8, borderTop: '1px solid #1e293b' }}>
        {Object.entries(CHOICE_CONFIG).filter(([k]) => k !== '?').slice(0, 6).map(([k, v]) => (
          <div key={k} style={{ display: 'flex', alignItems: 'center', gap: 3 }}>
            <span style={{ fontSize: 10 }}>{v.emoji}</span>
            <span style={{ fontSize: 9, color: v.color }}>{v.label}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
