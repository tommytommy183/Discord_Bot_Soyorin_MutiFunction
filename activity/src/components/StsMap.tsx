import { useRef, useEffect, useMemo, useState } from 'react';
import type { TowerRun, MapNode } from '../types';
import { spriteUrl } from '../utils';

interface Props {
  run: TowerRun;
  onSelectNode?: (customId: string) => void;  // undefined = view-only (battle screen)
  busy?: boolean;
}

// ── Node type config ──────────────────────────────────────────────────────
const NODE_CFG: Record<string, { emoji: string; color: string; label: string }> = {
  battle:       { emoji: '⚔️', color: '#ef4444', label: '戰鬥' },
  miniboss:     { emoji: '👹', color: '#f97316', label: '小頭目' },
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
const FLOOR_H = 62;     // height per floor (taller to fit Pokemon sprites)
const NODE_R = 16;      // node circle radius
const PAD_X = 28;       // left/right padding

export function StsMap({ run, onSelectNode, busy }: Props) {
  const nodes = run.mapNodes ?? [];
  const maxFloor = run.maxFloor;
  const currentId = run.currentNodeId ?? '';
  const svgH = (maxFloor + 0.5) * FLOOR_H;
  const [hoveredId, setHoveredId] = useState<string | null>(null);

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

  // Build edges with bezier curves + availability + hover flag
  const edges = useMemo(() => {
    const result: {
      x1: number; y1: number; x2: number; y2: number;
      isVisited: boolean; isAvailable: boolean; targetType: string; targetId: string;
    }[] = [];
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
        const isAvailable = (n.id === currentId || n.visited) && availableIds.has(nid);
        result.push({ x1, y1, x2, y2, isVisited, isAvailable, targetType: target.type, targetId: target.id });
      });
    });
    return result;
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nodes, byFloor, svgH, currentId, availableIds]);

  // Bezier path: S-curve between two floors (control points at mid-Y)
  function bezierPath(x1: number, y1: number, x2: number, y2: number): string {
    const midY = (y1 + y2) / 2;
    return `M ${x1} ${y1} C ${x1} ${midY} ${x2} ${midY} ${x2} ${y2}`;
  }


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
    <div style={{ position: 'relative' }}>
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

          {/* Edges — bezier curves, drawn in layers: future → visited → available (top) */}
          {/* Layer 1: far-future (very faint) */}
          {edges.filter(e => !e.isVisited && !e.isAvailable).map((e, i) => (
            <path key={`bg_${i}`}
              d={bezierPath(e.x1, e.y1, e.x2, e.y2)}
              stroke="#1e293b" strokeWidth={1} fill="none"
              strokeLinecap="round" opacity={0.35}
            />
          ))}
          {/* Layer 2: visited path (purple trail) */}
          {edges.filter(e => e.isVisited).map((e, i) => (
            <path key={`vis_${i}`}
              d={bezierPath(e.x1, e.y1, e.x2, e.y2)}
              stroke="#6366f1" strokeWidth={2.5} fill="none"
              strokeLinecap="round" opacity={0.7}
            />
          ))}
          {/* Layer 3: available routes — bright, thick, color-coded by destination */}
          {edges.filter(e => e.isAvailable).map((e, i) => {
            const c = cfg(e.targetType).color;
            const isHov = hoveredId === e.targetId;
            return (
              <g key={`av_${i}`}>
                {/* Wide glow halo — extra bright on hover */}
                <path
                  d={bezierPath(e.x1, e.y1, e.x2, e.y2)}
                  stroke={c} strokeWidth={isHov ? 18 : 7} fill="none"
                  strokeLinecap="round" opacity={isHov ? 0.35 : 0.18}
                />
                {/* Main path — solid on hover, dashed normally */}
                <path
                  d={bezierPath(e.x1, e.y1, e.x2, e.y2)}
                  stroke={c} strokeWidth={isHov ? 4 : 2.5} fill="none"
                  strokeLinecap="round" opacity={isHov ? 1 : 0.85}
                  strokeDasharray={isHov ? undefined : '6 3'}
                  style={isHov ? undefined : { animation: 'pulse 1.8s ease-in-out infinite' }}
                />
                {/* Arrow dot at destination on hover */}
                {isHov && (
                  <circle cx={e.x2} cy={e.y2} r={5}
                    fill={c} opacity={0.9}
                    style={{ animation: 'pulse 0.8s ease-in-out infinite' }}
                  />
                )}
              </g>
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
                onMouseEnter={() => setHoveredId(n.id)}
                onMouseLeave={() => setHoveredId(null)}
                onClick={() => {
                  if (!isAvail || !onSelectNode || busy) return;
                  const opt = run.pathOptions.find(o => o.customId.endsWith(`_${n.id}`));
                  if (opt) onSelectNode(opt.customId);
                }}
              >
                {/* Preview Pokemon sprite above battle/boss/miniboss nodes */}
                {(n.type === 'battle' || n.type === 'boss' || n.type === 'miniboss') && n.previewPokeId && n.previewPokeId > 0 && (
                  <image
                    href={spriteUrl(n.previewPokeId, 'front')}
                    x={x - 16} y={y - NODE_R - 34}
                    width={32} height={32}
                    style={{ imageRendering: 'pixelated' }}
                    opacity={isFuture ? 0.25 : isAvail ? 1 : 0.5}
                  />
                )}

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

    {/* Hover preview panel — absolute overlay in top-left corner of outer wrapper */}
    {(() => {
      const hn = hoveredId ? nodes.find(n => n.id === hoveredId) : null;
      if (!hn) return null;
      const c = cfg(hn.type);
      const isBattle = hn.type === 'battle' || hn.type === 'boss' || hn.type === 'miniboss';
      return (
        <div style={{
          position: 'absolute', top: 8, left: 8,
          zIndex: 30, pointerEvents: 'none',
          display: 'inline-flex', flexDirection: 'column', alignItems: 'center', gap: 4,
          background: 'rgba(7,9,15,0.97)',
          border: `1px solid ${c.color}66`,
          borderRadius: 12, padding: '10px 12px', minWidth: 110,
          boxShadow: `0 0 20px ${c.color}44`,
          backdropFilter: 'blur(8px)',
        }}>
          {isBattle && hn.previewPokeId && hn.previewPokeId > 0 ? (
            <>
              <img src={spriteUrl(hn.previewPokeId, 'front')} alt={hn.previewPokeName}
                style={{ width: 72, height: 72, imageRendering: 'pixelated', animation: 'bounce 2s ease-in-out infinite' }} />
              <div style={{ fontWeight: 900, fontSize: 12, color: '#fff', textAlign: 'center', lineHeight: 1.3 }}>
                {hn.previewPokeName || '???'}
              </div>
              <div style={{ fontSize: 11, fontWeight: 700, color: c.color, background: `${c.color}22`, borderRadius: 6, padding: '2px 8px' }}>
                {c.emoji} {c.label}
              </div>
              {hn.type === 'boss' && (
                <div style={{ fontSize: 9, color: '#ef4444', fontFamily: "'Press Start 2P', monospace", animation: 'pulse 1s ease-in-out infinite' }}>
                  BOSS
                </div>
              )}
            </>
          ) : (
            <>
              <div style={{ fontSize: 36 }}>{c.emoji}</div>
              <div style={{ fontWeight: 700, fontSize: 12, color: c.color }}>{c.label}</div>
            </>
          )}
          <div style={{ fontSize: 9, color: '#475569' }}>第 {hn.floor} 層</div>
        </div>
      );
    })()}
    </div>
  );
}
