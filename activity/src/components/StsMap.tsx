import { useRef, useEffect, useMemo } from 'react';
import type { TowerRun, MapNode } from '../types';

interface Props {
  run: TowerRun;
  onSelectNode?: (customId: string) => void;  // undefined = view-only (battle screen)
  busy?: boolean;
}

// ── Node type config ──────────────────────────────────────────────────────
const NODE_CFG: Record<string, { emoji: string; color: string; label: string }> = {
  battle:       { emoji: '⚔️', color: '#ef4444', label: '戰鬥' },
  boss:         { emoji: '💀', color: '#7c3aed', label: 'BOSS' },
  shop:         { emoji: '🛍️', color: '#3b82f6', label: '商店' },
  rest:         { emoji: '🏕️', color: '#22c55e', label: '休息' },
  event:        { emoji: '🎉', color: '#a855f7', label: '事件' },
  casino:       { emoji: '🎰', color: '#f59e0b', label: '賭場' },
  relic:        { emoji: '🔮', color: '#8b5cf6', label: '遺物' },
  cursed_relic: { emoji: '💀', color: '#dc2626', label: '詛咒' },
};
function cfg(type: string) { return NODE_CFG[type] ?? { emoji: '❓', color: '#475569', label: '?' }; }

// ── Layout constants ───────────────────────────────────────────────────────
const W = 300;          // SVG width
const FLOOR_H = 54;     // height per floor
const NODE_R = 16;      // node circle radius
const PAD_X = 28;       // left/right padding

export function StsMap({ run, onSelectNode, busy }: Props) {
  const nodes = run.mapNodes ?? [];
  const maxFloor = run.maxFloor;
  const currentId = run.currentNodeId ?? '';
  const svgH = (maxFloor + 0.5) * FLOOR_H;

  // Group by floor
  const byFloor = useMemo(() => {
    const map = new Map<number, MapNode[]>();
    nodes.forEach(n => {
      if (!map.has(n.floor)) map.set(n.floor, []);
      map.get(n.floor)!.push(n);
    });
    return map;
  }, [nodes]);

  // Which nodes can be selected right now
  const availableIds = useMemo(() => {
    return new Set(run.pathOptions.map(o => {
      // customId format: "tower_path_{channelId}_{nodeId}"
      const parts = o.customId.split('_');
      return parts.slice(3).join('_');  // nodeId may contain underscores (but IDs are like f01n0)
    }));
  }, [run.pathOptions]);

  // Find visited path chain (for drawing the trail)
  const visitedSet = useMemo(() => new Set(nodes.filter(n => n.visited).map(n => n.id)), [nodes]);

  // Layout: x position of node
  function nodeX(n: MapNode): number {
    const floorNodes = byFloor.get(n.floor) ?? [];
    const idx = floorNodes.indexOf(n);
    const count = floorNodes.length;
    if (count === 1) return W / 2;
    return PAD_X + (idx * (W - 2 * PAD_X)) / (count - 1);
  }

  // y from bottom (floor 1 = bottom)
  function nodeY(floor: number): number {
    return svgH - floor * FLOOR_H - FLOOR_H * 0.2;
  }

  // Build edges
  const edges = useMemo(() => {
    const result: { x1: number; y1: number; x2: number; y2: number; isVisited: boolean }[] = [];
    nodes.forEach(n => {
      const flN = byFloor.get(n.floor) ?? [];
      const ni = flN.indexOf(n);
      const nCount = flN.length;
      const x1 = nCount === 1 ? W / 2 : PAD_X + (ni * (W - 2 * PAD_X)) / (nCount - 1);
      const y1 = svgH - n.floor * FLOOR_H - FLOOR_H * 0.2;
      n.nextIds.forEach(nid => {
        const target = nodes.find(x => x.id === nid);
        if (!target) return;
        const flT = byFloor.get(target.floor) ?? [];
        const ti = flT.indexOf(target);
        const tCount = flT.length;
        const x2 = tCount === 1 ? W / 2 : PAD_X + (ti * (W - 2 * PAD_X)) / (tCount - 1);
        const y2 = svgH - target.floor * FLOOR_H - FLOOR_H * 0.2;
        const isVisited = n.visited && target.visited;
        result.push({ x1, y1, x2, y2, isVisited });
      });
    });
    return result;
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nodes, byFloor, svgH]);

  // Auto-scroll to current floor (SVG origin = top-left, floor 1 is near bottom of SVG)
  const scrollRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!scrollRef.current) return;
    const currentNode = currentId ? nodes.find(n => n.id === currentId) : null;
    const containerH = scrollRef.current.clientHeight;
    if (currentNode) {
      // y in SVG coords → scroll so the current floor is vertically centered
      const y = nodeY(currentNode.floor);
      scrollRef.current.scrollTop = y - containerH / 2;
    } else {
      // No current node yet → show bottom of map (floor 1)
      scrollRef.current.scrollTop = svgH;
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentId]);

  if (nodes.length === 0) return null;

  return (
    <div ref={scrollRef} style={{
      background: '#07090f',
      border: '1px solid #1e293b',
      borderRadius: 10,
      overflow: 'auto',
      maxHeight: 360,
      position: 'relative',
    }}>
      {/* Floor labels overlay */}
      <div style={{ position: 'relative' }}>
        <svg width="100%" viewBox={`0 0 ${W} ${svgH}`} height={svgH} style={{ display: 'block', minWidth: W }}>
          {/* Background grid lines (subtle) */}
          {Array.from({ length: maxFloor }, (_, i) => i + 1).map(f => (
            <line key={f}
              x1={PAD_X / 2} y1={nodeY(f)} x2={W - PAD_X / 2} y2={nodeY(f)}
              stroke="#ffffff08" strokeWidth={1}
            />
          ))}

          {/* Edges */}
          {edges.map((e, i) => {
            const cy = (e.y1 + e.y2) / 2;
            const d = `M ${e.x1} ${e.y1} C ${e.x1} ${cy}, ${e.x2} ${cy}, ${e.x2} ${e.y2}`;
            return (
              <path key={i} d={d} fill="none"
                stroke={e.isVisited ? '#6366f1' : '#1e293b'}
                strokeWidth={e.isVisited ? 3 : 1.5}
                strokeLinecap="round"
                opacity={e.isVisited ? 1 : 0.6}
              />
            );
          })}

          {/* Nodes */}
          {nodes.map(n => {
            const x = nodeX(n);
            const y = nodeY(n.floor);
            const c = cfg(n.type);
            const isCurrent = n.id === currentId;
            const isAvail = availableIds.has(n.id);
            const isVisited = n.visited;
            const isFuture = !isVisited && !isCurrent && !isAvail;

            let fillColor = '#0a0e1a';
            let strokeColor = '#334155';
            let opacity = isFuture ? 0.35 : 1;

            if (isCurrent) {
              fillColor = '#1e1b4b';
              strokeColor = '#6366f1';
            } else if (isVisited) {
              fillColor = `${c.color}22`;
              strokeColor = `${c.color}88`;
            } else if (isAvail) {
              fillColor = `${c.color}33`;
              strokeColor = c.color;
            } else {
              strokeColor = '#1e293b';
            }

            return (
              <g key={n.id}
                style={{ cursor: isAvail && onSelectNode && !busy ? 'pointer' : 'default' }}
                onClick={() => {
                  if (!isAvail || !onSelectNode || busy) return;
                  const opt = run.pathOptions.find(o => o.customId.endsWith(`_${n.id}`));
                  if (opt) onSelectNode(opt.customId);
                }}
              >
                {/* Glow ring for available nodes */}
                {isAvail && (
                  <circle cx={x} cy={y} r={NODE_R + 6}
                    fill="none" stroke={c.color} strokeWidth={1.5} opacity={0.4}
                    style={{ animation: 'pulse 1.5s ease-in-out infinite' }}
                  />
                )}
                {/* Current position glow */}
                {isCurrent && (
                  <circle cx={x} cy={y} r={NODE_R + 8}
                    fill="none" stroke="#6366f1" strokeWidth={2} opacity={0.3}
                    style={{ animation: 'pulse 1.5s ease-in-out infinite' }}
                  />
                )}

                {/* Main circle */}
                <circle cx={x} cy={y} r={NODE_R}
                  fill={fillColor} stroke={strokeColor} strokeWidth={isCurrent ? 2.5 : 1.5}
                  opacity={opacity}
                />

                {/* Emoji */}
                <text x={x} y={y + 1} textAnchor="middle" dominantBaseline="middle"
                  fontSize={isCurrent ? 14 : 12}
                  opacity={isFuture ? 0.3 : 1}
                  style={{ userSelect: 'none', pointerEvents: 'none' }}
                >
                  {isCurrent ? '📍' : isVisited ? c.emoji : isAvail ? c.emoji : '❓'}
                </text>

                {/* Floor number (right of node on rightmost or only node) */}
                {(byFloor.get(n.floor)?.indexOf(n) === 0 && (byFloor.get(n.floor)?.length ?? 0) <= 1) && (
                  <text x={x + NODE_R + 4} y={y + 1}
                    fill="#334155" fontSize={9} dominantBaseline="middle"
                  >
                    {n.floor}
                  </text>
                )}
              </g>
            );
          })}

          {/* Floor numbers on left side */}
          {Array.from(byFloor.keys()).map(f => (
            <text key={`fl${f}`}
              x={8} y={nodeY(f) + 1}
              fill={f === (run.currentFloor) ? '#6366f1' : '#1e2940'}
              fontSize={8} dominantBaseline="middle"
              fontWeight={f === run.currentFloor ? 'bold' : 'normal'}
            >
              {f}
            </text>
          ))}
        </svg>
      </div>

      {/* Available nodes tooltip bar at bottom */}
      {onSelectNode && availableIds.size > 0 && (
        <div style={{
          position: 'sticky', bottom: 0,
          background: '#07090f', borderTop: '1px solid #1e293b',
          padding: '6px 12px',
          display: 'flex', gap: 8, alignItems: 'center',
        }}>
          <span style={{ fontSize: 10, color: '#475569', flexShrink: 0 }}>點擊選擇路線 →</span>
          {run.pathOptions.map(opt => {
            const nodeId = opt.customId.split('_').slice(3).join('_');
            const node = nodes.find(n => n.id === nodeId);
            if (!node) return null;
            const c = cfg(node.type);
            return (
              <button
                key={opt.customId}
                disabled={busy}
                onClick={() => onSelectNode(opt.customId)}
                className="btn-hover"
                style={{
                  background: `${c.color}22`,
                  border: `1px solid ${c.color}55`,
                  borderRadius: 6, padding: '4px 10px',
                  color: c.color, fontSize: 11, fontWeight: 700,
                  cursor: busy ? 'not-allowed' : 'pointer',
                  display: 'flex', alignItems: 'center', gap: 4,
                }}
              >
                <span>{c.emoji}</span>
                <span>{c.label}</span>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
