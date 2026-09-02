import { useState, useEffect, useRef } from 'react';
import { HpBar } from './HpBar';
import { StatusBadge } from './StatusBadge';
import { TypeBadge } from './TypeBadge';
import { spriteUrl, typeColor } from '../utils';
import type { TowerRun, TowerMove } from '../types';

interface Props {
  run: TowerRun;
  onAction: (customId: string) => void;
  busy: boolean;
}

type AttackAnim =
  | 'idle' | 'lunge' | 'beam' | 'projectile' | 'status'
  | 'tail' | 'claw' | 'thunder' | 'flame' | 'ice' | 'shadow'
  | 'cross' | 'leaf' | 'wave' | 'slam' | 'poison' | 'wind' | 'rock';

/** Map move name keywords → animation type */
function getMoveAnimType(name: string, category: string): AttackAnim {
  const n = name ?? '';
  // Ordered from most-specific to most-general
  if (/尾/.test(n)) return 'tail';
  if (/十字|剪刀X|剪刀十/.test(n)) return 'cross';
  if (/葉|草|木|花|種|藤|豆|植|芽|棉|孢子|楓/.test(n)) return 'leaf';
  if (/光束|射線|雷射|能量波|氣功|幅射/.test(n)) return 'beam';
  if (/球|彈|珠/.test(n)) return 'projectile';
  if (/爪|刀|斬|切|利刃|利爪|裂/.test(n)) return 'claw';
  if (/電|雷|靜電|十萬伏特|落雷|閃電|放電/.test(n)) return 'thunder';
  if (/火|炎|焰|岩漿|爆炎|大字|噴射(?!水)/.test(n)) return 'flame';
  if (/冰|雪|霜|凍|冷|冰凍/.test(n)) return 'ice';
  if (/影|暗|鬼|幽|夜|詛|闇|奪|魅/.test(n)) return 'shadow';
  if (/毒|酸|腐|汙/.test(n)) return 'poison';
  if (/波|水|海|潮|漩渦|泡|湧|衝浪|瀑|液|噴水/.test(n)) return 'wave';
  if (/岩石|石塊|土石|礫|隕石|土壤|岩崩/.test(n)) return 'rock';
  if (/砸|落|降|踩|震|崩|地震|重力/.test(n)) return 'slam';
  if (/風|颱|龍捲|旋|吹/.test(n)) return 'wind';
  // Fallback by category
  return category === 'Physical' ? 'lunge'
    : category === 'Special' ? 'projectile'
    : 'status';
}

function StageLabel({ stage }: { stage: number }) {
  if (stage === 0) return null;
  return (
    <span style={{ color: stage > 0 ? '#4ade80' : '#f87171', fontSize: 10, fontWeight: 700, marginLeft: 2 }}>
      {stage > 0 ? `▲${stage}` : `▼${Math.abs(stage)}`}
    </span>
  );
}

function MoveBtn({ move, idx, channelId, busy, locked, onAttack }: {
  move: TowerMove; idx: number; channelId: string;
  busy: boolean; locked: boolean;
  onAttack: (category: string, moveType: string, customId: string, moveName: string) => void;
}) {
  const color = typeColor(move.type);
  const empty = move.currentPP === 0;
  const customId = `tower_move_${channelId}_${idx}`;
  const ppRatio = move.maxPP > 0 ? move.currentPP / move.maxPP : 0;
  const ppColor = ppRatio === 0 ? '#ef4444' : ppRatio <= 0.25 ? '#facc15' : '#94a3b8';
  const isDisabled = busy || empty || locked;

  return (
    <button
      className="btn-hover"
      disabled={isDisabled}
      onClick={() => { if (!isDisabled) { onAttack(move.category, move.type, customId, move.name); } }}
      style={{
        background: empty ? '#1a1f2e' : `linear-gradient(135deg, ${color}44 0%, ${color}22 100%)`,
        color: empty ? '#475569' : '#fff',
        border: `1px solid ${empty ? '#334155' : color + '88'}`,
        boxShadow: (!empty && !isDisabled) ? `0 0 6px ${color}33` : 'none',
        borderRadius: 8,
        padding: '6px 8px',
        cursor: isDisabled ? 'not-allowed' : 'pointer',
        opacity: isDisabled ? 0.55 : 1,
        display: 'flex', flexDirection: 'row', alignItems: 'center', gap: 6,
        flex: '1 1 calc(50% - 4px)',
        minWidth: 0, position: 'relative', overflow: 'hidden',
      }}
    >
      {!empty && (
        <div style={{
          position: 'absolute', top: 0, left: 0, bottom: 0, width: 3,
          background: color, borderRadius: '8px 0 0 8px',
        }} />
      )}
      <span style={{ fontSize: 18, lineHeight: 1, flexShrink: 0, marginLeft: 4 }}>{move.emoji}</span>
      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 1 }}>
        <div style={{ fontWeight: 700, fontSize: 11, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{move.name}</div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 9, color: '#64748b' }}>
          <span>{['physical','Physical'].includes(move.category) ? '物' : ['special','Special'].includes(move.category) ? '特' : '變'}{move.power > 0 ? ` ${move.power}` : ''}</span>
          <span style={{ color: ppColor }}>PP {move.currentPP}/{move.maxPP}</span>
        </div>
      </div>
    </button>
  );
}

function BattleLog({ logs }: { logs: string[] }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (ref.current) ref.current.scrollTop = ref.current.scrollHeight;
  }, [logs]);

  return (
    <div ref={ref} style={{
      background: '#07090f', borderRadius: 8,
      padding: '8px 12px', fontSize: 11, color: '#94a3b8',
      height: 96, overflowY: 'auto',
      border: '1px solid #1e293b', lineHeight: 1.6, flexShrink: 0,
    }}>
      {logs.slice(-10).map((log, i, arr) => (
        <div key={i} style={{
          opacity: 0.4 + (i / arr.length) * 0.6,
          color: i === arr.length - 1 ? '#e2e8f0' : '#94a3b8',
          fontWeight: i === arr.length - 1 ? 600 : 400,
        }}>
          {log}
        </div>
      ))}
    </div>
  );
}

export function BattleScene({ run, onAction, busy }: Props) {
  const activePoke = run.team[run.activeIndex];
  const enemy = run.currentEnemy;
  const isBoss = enemy?.isBoss ?? false;

  // ── Animation states ──────────────────────────────────────────────────
  const [shake, setShake] = useState(false);
  const [playerShake, setPlayerShake] = useState(false);
  const [attackAnim, setAttackAnim] = useState<AttackAnim>('idle');
  const [enemyAnim, setEnemyAnim] = useState<AttackAnim>('idle');
  const [currentMoveType, setCurrentMoveType] = useState<string>('normal');
  const [enemyMoveType, setEnemyMoveType] = useState<string>('normal');
  const [attackLocked, setAttackLocked] = useState(false);
  const [showImpact, setShowImpact] = useState(false);
  const [screenFlash, setScreenFlash] = useState(false);
  const [critAnim, setCritAnim] = useState(false);
  const [floatTexts, setFloatTexts] = useState<{id: number; text: string; color: string; x: number}[]>([]);

  const floatIdRef = useRef(0);
  const prevLogLen = useRef(0);
  const attackLockedRef = useRef(false);
  const fxKey = useRef(0); // incremented on each attack to force re-mount of FX elements

  function addFloat(text: string, color: string, xPct: number) {
    const id = ++floatIdRef.current;
    setFloatTexts(prev => [...prev, { id, text, color, x: xPct }]);
    setTimeout(() => setFloatTexts(prev => prev.filter(f => f.id !== id)), 1000);
  }

  // ── Log watcher: enemy animations + proc floats ───────────────────────
  useEffect(() => {
    const logs = run.battleLog;
    if (logs.length > prevLogLen.current) {
      const newLogs = logs.slice(prevLogLen.current);
      prevLogLen.current = logs.length;
      const currentEnemy = run.currentEnemy;

      // Proc floating texts
      const joined = newLogs.join(' ');
      if (joined.includes('暴擊'))       { addFloat('暴擊!', '#facc15', 68); setCritAnim(true); setTimeout(() => setCritAnim(false), 500); }
      if (joined.includes('連擊'))       addFloat('連擊!', '#f97316', 72);
      if (joined.includes('鏡面反射'))   addFloat('反射!', '#60a5fa', 55);
      if (joined.includes('復仇釋放'))   addFloat('復仇!', '#ef4444', 65);
      if (joined.includes('生命吸取'))   addFloat('+HP', '#4ade80', 28);
      if (joined.includes('吸血鬼'))     addFloat('+HP', '#c084fc', 28);
      if (joined.includes('反噬'))       addFloat('反噬!', '#f87171', 25);
      if (joined.includes('再生'))       addFloat('+HP', '#34d399', 25);
      if (joined.includes('寄生'))       addFloat('+5HP', '#86efac', 24);

      if (attackLockedRef.current) {
        // Enemy turn: animate and detect move type
        const moveLine = newLogs.find(l => currentEnemy && l.includes(currentEnemy.name) && l.includes('使用'));
        const etype = currentEnemy?.moves?.[0]?.type ?? '一般';
        setEnemyMoveType(etype);
        const isPhys = !!(moveLine?.includes('物') || moveLine?.includes('衝') || moveLine?.includes('撞'));
        setScreenFlash(true);
        setTimeout(() => setScreenFlash(false), 320);
        setPlayerShake(true);
        setTimeout(() => setPlayerShake(false), 600);
        setEnemyAnim(isPhys ? 'lunge' : 'projectile');
        setTimeout(() => {
          setEnemyAnim('idle');
          setAttackLocked(false);
          attackLockedRef.current = false;
        }, 750);
      } else {
        setAttackLocked(false);
        attackLockedRef.current = false;
      }
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [run.battleLog.length]);

  // Safety unlock
  useEffect(() => {
    if (!busy) {
      const t = setTimeout(() => { setAttackLocked(false); attackLockedRef.current = false; }, 900);
      return () => clearTimeout(t);
    }
  }, [busy]);

  // ── handleAttack: trigger animation then call API ─────────────────────
  function handleAttack(category: string, moveType?: string, customId?: string, moveName?: string) {
    const animType = getMoveAnimType(moveName ?? '', category);
    setAttackLocked(true);
    attackLockedRef.current = true;
    setCurrentMoveType(moveType ?? '一般');
    fxKey.current++;
    setAttackAnim(animType);

    // Impact timing: physical/tail/rock hit peak earlier
    const impactDelay =
      ['lunge','tail','rock','slam','claw','cross'].includes(animType) ? 360
      : ['beam','wave'].includes(animType) ? 280
      : 420;

    setTimeout(() => { setShake(true); setShowImpact(true); }, impactDelay);
    setTimeout(() => { setShake(false); setShowImpact(false); }, impactDelay + 350);
    setTimeout(() => setAttackAnim('idle'), 780);

    if (customId) setTimeout(() => onAction(customId), 780);
  }

  // ── FX element renderer ───────────────────────────────────────────────
  function renderAttackFX() {
    const c = typeColor(currentMoveType);
    const k = fxKey.current;

    switch (attackAnim) {
      case 'lunge':
        // Speed lines handled separately
        return null;

      case 'beam':
        return (
          <div key={k} style={{
            position: 'absolute', bottom: 95, left: 100,
            height: 13, borderRadius: 7, zIndex: 10, pointerEvents: 'none',
            background: `linear-gradient(90deg, ${c}88, ${c}, #ffffffcc)`,
            boxShadow: `0 0 24px 10px ${c}99, 0 0 50px 4px ${c}44`,
            animation: 'beamExpand 0.6s ease-out forwards',
          }} />
        );

      case 'projectile':
        return (
          <div key={k} style={{
            position: 'absolute', bottom: 100, left: 110,
            width: 26, height: 26, borderRadius: '50%', zIndex: 10, pointerEvents: 'none',
            background: `radial-gradient(circle, white 0%, ${c} 50%, ${c}88 100%)`,
            boxShadow: `0 0 22px 10px ${c}`,
            animation: 'projectileFly 0.62s ease-in forwards',
          }} />
        );

      case 'tail':
        // Arc sweep from player side
        return (
          <div key={k} style={{
            position: 'absolute', bottom: '28%', left: 80,
            width: '48%', height: 28, borderRadius: '0 50% 50% 0',
            border: `3px solid ${c}`, borderLeft: 'none',
            boxShadow: `0 0 14px 5px ${c}66`,
            zIndex: 10, pointerEvents: 'none',
            transformOrigin: 'left center',
            animation: 'tailSwing 0.62s ease forwards',
          }} />
        );

      case 'claw': {
        const rotations = [-30, -15, 0];
        return (
          <>
            {rotations.map((rot, i) => (
              <div key={i} style={{
                position: 'absolute',
                top: `${18 + i * 16}%`, right: `${6 + i}%`,
                width: '32%', height: 3, borderRadius: 2,
                background: `linear-gradient(270deg, ${c}, ${c}44)`,
                boxShadow: `0 0 10px 3px ${c}88`,
                zIndex: 12, pointerEvents: 'none',
                transformOrigin: 'right center',
                ['--r' as string]: `${rot}deg`,
                animation: `clawSlash 0.56s ${i * 0.07}s ease forwards`,
              } as React.CSSProperties} />
            ))}
          </>
        );
      }

      case 'cross':
        return (
          <>
            {/* Horizontal bar */}
            <div key={`${k}h`} style={{
              position: 'absolute', top: '38%', right: '4%',
              width: '40%', height: 4, borderRadius: 2,
              background: `linear-gradient(270deg, ${c}, ${c}44)`,
              boxShadow: `0 0 14px 6px ${c}88`,
              zIndex: 12, pointerEvents: 'none',
              animation: 'crossSlashH 0.56s ease forwards',
            }} />
            {/* Vertical bar */}
            <div key={`${k}v`} style={{
              position: 'absolute', top: '6%', right: '17%',
              width: 4, height: '75%', borderRadius: 2,
              background: `linear-gradient(180deg, ${c}44, ${c})`,
              boxShadow: `0 0 14px 6px ${c}88`,
              zIndex: 12, pointerEvents: 'none',
              animation: 'crossSlashV 0.56s 0.06s ease forwards',
            }} />
          </>
        );

      case 'thunder':
        return (
          <svg key={k} style={{
            position: 'absolute', inset: 0, width: '100%', height: '100%',
            zIndex: 12, pointerEvents: 'none',
            animation: 'thunderZap 0.52s ease forwards',
          }}>
            <polyline
              points="66%,2% 52%,28% 68%,44% 45%,70% 60%,84% 50%,100%"
              fill="none" stroke={c} strokeWidth="5" strokeLinecap="round"
              style={{ filter: `drop-shadow(0 0 5px ${c}) drop-shadow(0 0 14px ${c})` }}
            />
            <polyline
              points="62%,8% 74%,30% 56%,48% 70%,68%"
              fill="none" stroke="white" strokeWidth="2" strokeLinecap="round"
              strokeOpacity="0.5"
            />
          </svg>
        );

      case 'flame':
        return (
          <>
            {[0,1,2,3,4].map(i => (
              <div key={i} style={{
                position: 'absolute',
                bottom: `${26 + (i % 3) * 13}%`,
                right: `${4 + (i % 5) * 6}%`,
                width: 9 + i * 5, height: 14 + i * 7,
                borderRadius: `${40 + i * 4}% ${60 - i * 4}% 50% 50%`,
                background: i % 2 === 0
                  ? 'radial-gradient(ellipse, #fde68a 0%, #f97316 50%, transparent 100%)'
                  : 'radial-gradient(ellipse, #fbbf24 0%, #ef4444 60%, transparent 100%)',
                zIndex: 12, pointerEvents: 'none',
                animation: `flamePuff 0.65s ${i * 0.09}s ease forwards`,
              }} />
            ))}
          </>
        );

      case 'ice': {
        // Six shards positioned around enemy, scatter outward
        const dirs: [number, number][] = [[-40,-60], [5,-72], [45,-52], [62,18], [28,52], [-32,38]];
        return (
          <>
            {dirs.map(([dx, dy], i) => (
              <div key={i} style={{
                position: 'absolute',
                top: `${isBoss ? 28 : 33}%`, right: `${isBoss ? 14 : 16}%`,
                width: 8 + i * 2, height: 20 + i * 2,
                background: 'linear-gradient(180deg, #e0f2fe, #93c5fd)',
                boxShadow: '0 0 6px 2px #60a5fa',
                clipPath: 'polygon(50% 0%, 90% 100%, 10% 100%)',
                zIndex: 12, pointerEvents: 'none',
                ['--dx' as string]: `${dx}px`,
                ['--dy' as string]: `${dy}px`,
                animation: `iceShatter 0.62s ${i * 0.055}s ease forwards`,
              } as React.CSSProperties} />
            ))}
          </>
        );
      }

      case 'shadow':
        return (
          <>
            {/* Central void */}
            <div key={`${k}s`} style={{
              position: 'absolute',
              top: `${isBoss ? 16 : 20}%`, right: `${isBoss ? 3 : 5}%`,
              width: isBoss ? 136 : 104, height: isBoss ? 136 : 104,
              borderRadius: '50%',
              background: 'radial-gradient(circle, #4c1d9588 0%, #1e1b4b66 55%, transparent 75%)',
              boxShadow: '0 0 30px 12px #7c3aed99',
              zIndex: 11, pointerEvents: 'none',
              animation: 'shadowVoid 0.68s ease forwards',
            }} />
            {/* Tendrils */}
            {[0,1,2,3].map(i => (
              <div key={i} style={{
                position: 'absolute',
                top: `${22 + i * 12}%`, right: `${8 + i * 5}%`,
                width: 3, height: `${10 + i * 5}%`,
                background: `linear-gradient(180deg, transparent, #a855f7)`,
                borderRadius: 2, zIndex: 12, pointerEvents: 'none',
                transformOrigin: 'bottom',
                animation: `crossSlashV 0.5s ${i * 0.08}s ease forwards`,
              }} />
            ))}
          </>
        );

      case 'leaf': {
        const leafEmojis = ['🍃','🌿','🍀','🌱','🍃','🌿'];
        const leafDirs: [number, number][] = [[-50,-55], [0,-70], [48,-50], [62,22], [30,54], [-34,40]];
        return (
          <>
            {leafEmojis.map((em, i) => (
              <div key={i} style={{
                position: 'absolute',
                top: `${isBoss ? 28 : 35}%`, right: `${isBoss ? 14 : 17}%`,
                fontSize: 14 + (i % 2) * 4,
                zIndex: 12, pointerEvents: 'none',
                ['--dx' as string]: `${leafDirs[i][0]}px`,
                ['--dy' as string]: `${leafDirs[i][1]}px`,
                animation: `leafWhirl 0.72s ${i * 0.06}s ease forwards`,
              } as React.CSSProperties}>{em}</div>
            ))}
          </>
        );
      }

      case 'wave':
        return (
          <>
            <div key={`${k}w1`} style={{
              position: 'absolute', bottom: '36%', left: 90, right: 20,
              height: 18, borderRadius: 9,
              background: `linear-gradient(90deg, transparent, ${c}bb, ${c}, #ffffffcc, ${c}99, transparent)`,
              boxShadow: `0 0 20px 8px ${c}77`,
              zIndex: 10, pointerEvents: 'none',
              animation: 'waveSweep 0.62s ease forwards',
            }} />
            <div key={`${k}w2`} style={{
              position: 'absolute', bottom: '43%', left: 90, right: 20,
              height: 10, borderRadius: 5, opacity: 0.55,
              background: `linear-gradient(90deg, transparent, ${c}88, transparent)`,
              zIndex: 10, pointerEvents: 'none',
              animation: 'waveSweep 0.62s 0.08s ease forwards',
            }} />
          </>
        );

      case 'slam': {
        const ex = isBoss ? '56%' : '63%';
        return (
          <>
            <div key={`${k}orb`} style={{
              position: 'absolute', left: ex, top: '2%',
              width: 38, height: 38, borderRadius: '50%',
              background: `radial-gradient(circle, white 0%, ${c} 40%, ${c}44 80%)`,
              boxShadow: `0 0 22px 8px ${c}99`,
              transform: 'translateX(-50%)',
              zIndex: 13, pointerEvents: 'none',
              animation: 'slamDrop 0.58s ease-in forwards',
            }} />
            <div key={`${k}shock`} style={{
              position: 'absolute', bottom: '22%', left: ex,
              width: 14, height: 14, borderRadius: '50%',
              border: `3px solid ${c}`,
              boxShadow: `0 0 8px 3px ${c}`,
              transform: 'translate(-50%, 50%)',
              zIndex: 12, pointerEvents: 'none',
              animation: 'shockwave 0.5s 0.46s ease forwards',
            }} />
          </>
        );
      }

      case 'poison':
        return (
          <>
            {[0,1,2,3].map(i => (
              <div key={i} style={{
                position: 'absolute',
                top: `${10 + i * 18}%`, right: `${8 + (i % 3) * 8}%`,
                width: 10 + (i % 2) * 6, height: 10 + (i % 2) * 6,
                borderRadius: '50%',
                background: `radial-gradient(circle, #e879f9 0%, #a855f7 60%, transparent 100%)`,
                boxShadow: '0 0 8px 3px #c026d3',
                zIndex: 12, pointerEvents: 'none',
                animation: `poisonDrip 0.65s ${i * 0.09}s ease forwards`,
              }} />
            ))}
          </>
        );

      case 'wind':
        return (
          <>
            {[0,1,2].map(i => (
              <div key={i} style={{
                position: 'absolute',
                top: `${20 + i * 20}%`, right: `${8 + i * 5}%`,
                width: 55 - i * 10, height: 55 - i * 10,
                borderRadius: '50%',
                border: `2px solid ${c}88`,
                boxShadow: `0 0 10px 3px ${c}44`,
                zIndex: 11, pointerEvents: 'none',
                animation: `windSwirl 0.65s ${i * 0.1}s ease forwards`,
              }} />
            ))}
          </>
        );

      case 'rock': {
        const offsets = [-28, 0, 28];
        return (
          <>
            {offsets.map((rx, i) => (
              <div key={i} style={{
                position: 'absolute',
                left: `${isBoss ? 55 : 60}%`, top: '5%',
                width: 14 + i * 4, height: 14 + i * 4,
                background: `linear-gradient(135deg, ${c}, ${c}88)`,
                borderRadius: '30% 70% 60% 40%',
                boxShadow: `0 0 8px 3px ${c}66`,
                transform: `translateX(${rx}px)`,
                zIndex: 13, pointerEvents: 'none',
                ['--rx' as string]: `${rx}px`,
                animation: `rockFall 0.6s ${i * 0.08}s ease-in forwards`,
              } as React.CSSProperties} />
            ))}
          </>
        );
      }

      case 'status':
        return (
          <>
            {[0,1,2].map(i => (
              <div key={i} style={{
                position: 'absolute',
                bottom: '35%', left: `${10 + i * 5}%`,
                width: 80 + i * 20, height: 80 + i * 20,
                borderRadius: '50%',
                border: `2px solid ${typeColor(currentMoveType)}88`,
                zIndex: 9, pointerEvents: 'none',
                animation: `statusRing 0.7s ${i * 0.12}s ease forwards`,
              }} />
            ))}
          </>
        );

      default:
        return null;
    }
  }

  const bgGradient = isBoss
    ? 'radial-gradient(ellipse at 60% 40%, #2d0a0a 0%, #0a0e1a 100%)'
    : 'radial-gradient(ellipse at 60% 40%, #0d1e30 0%, #0a0e1a 100%)';

  const allMovesEmpty = activePoke ? activePoke.moves.every(m => m.currentPP === 0) : false;

  return (
    <div className="anim-fade-in" style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      {/* Floor / Boss title */}
      <div style={{ textAlign: 'center', paddingTop: 2 }}>
        {isBoss && (
          <div style={{
            fontFamily: "'Press Start 2P', monospace",
            fontSize: 10, color: '#ef4444', letterSpacing: '0.12em',
            animation: 'pulse 1s ease-in-out infinite', marginBottom: 2,
          }}>⚠ BOSS BATTLE ⚠</div>
        )}
        <div style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>
          第 <span style={{ color: '#e2e8f0', fontWeight: 900 }}>{run.currentFloor}</span> 層
        </div>
      </div>

      {/* ── Battle Arena ─────────────────────────────────────────── */}
      <div style={{
        position: 'relative',
        background: bgGradient,
        borderRadius: 14,
        border: isBoss ? '2px solid #7f1d1d' : '1px solid #1e2d45',
        height: isBoss ? 220 : 200, overflow: 'hidden',
      }}>
        {/* ── Flash overlays ────────────────────────────────────── */}
        {screenFlash && (
          <div style={{
            position: 'absolute', inset: 0, borderRadius: 14, zIndex: 22, pointerEvents: 'none',
            background: 'rgba(239,68,68,0.28)',
            animation: 'screenFlash 0.32s ease forwards',
          }} />
        )}
        {critAnim && (
          <div style={{
            position: 'absolute', inset: 0, borderRadius: 14, zIndex: 23, pointerEvents: 'none',
            background: 'rgba(250,204,21,0.2)',
            animation: 'screenFlash 0.45s ease forwards',
          }} />
        )}

        {/* ── Speed lines for lunge ─────────────────────────────── */}
        {(attackAnim === 'lunge' || attackAnim === 'tail') && [0,1,2,3].map(i => (
          <div key={i} style={{
            position: 'absolute',
            top: `${46 + i * 9}%`, left: 80, right: 80,
            height: i === 1 ? 3 : 2, borderRadius: 2,
            zIndex: 8, pointerEvents: 'none',
            background: `linear-gradient(90deg, transparent, ${typeColor(currentMoveType)}cc ${30+i*5}%, transparent)`,
            animation: `speedLine 0.38s ${i * 0.045}s ease forwards`,
          }} />
        ))}

        {/* ── Attack FX (keyword-based) ─────────────────────────── */}
        {attackAnim !== 'idle' && renderAttackFX()}

        {/* ── Impact burst ring at enemy on hit ────────────────── */}
        {showImpact && (
          <div style={{
            position: 'absolute',
            top: isBoss ? '22%' : '25%', right: isBoss ? '8%' : '10%',
            width: 72, height: 72, borderRadius: '50%',
            border: `3px solid ${typeColor(currentMoveType)}`,
            boxShadow: `0 0 20px 5px ${typeColor(currentMoveType)}99`,
            zIndex: 18, pointerEvents: 'none',
            animation: 'impactBurst 0.5s ease forwards',
          }} />
        )}

        {/* ── Enemy beam (when enemy attacks special) ──────────── */}
        {enemyAnim === 'projectile' && (
          <div style={{
            position: 'absolute', bottom: 95, right: 100,
            height: 9, borderRadius: 5, zIndex: 10, pointerEvents: 'none',
            background: `linear-gradient(270deg, ${typeColor(enemyMoveType)}, #ffffff88)`,
            boxShadow: `0 0 18px 7px ${typeColor(enemyMoveType)}99`,
            animation: 'beamExpand 0.65s ease-out forwards',
            transformOrigin: 'right',
          }} />
        )}

        {/* ── Floating proc texts ──────────────────────────────── */}
        {floatTexts.map(ft => (
          <div key={ft.id} style={{
            position: 'absolute', bottom: '38%', left: `${ft.x}%`,
            pointerEvents: 'none', zIndex: 26,
            fontWeight: 900, fontSize: 13, letterSpacing: '0.03em',
            color: ft.color,
            textShadow: `0 0 8px ${ft.color}, 0 2px 4px rgba(0,0,0,0.8)`,
            animation: 'floatText2 0.95s ease forwards',
          }}>{ft.text}</div>
        ))}

        {/* ── Ground line ──────────────────────────────────────── */}
        <div style={{
          position: 'absolute', bottom: 42, left: 0, right: 0,
          height: 2, background: 'linear-gradient(90deg, transparent, #1e293b 25%, #1e293b 75%, transparent)',
        }} />

        {/* ── Enemy sprite ─────────────────────────────────────── */}
        {enemy && (
          <div style={{
            position: 'absolute',
            top: isBoss ? 4 : 8, right: isBoss ? 4 : 8,
            animation: enemyAnim === 'lunge' ? 'enemyLunge 0.7s ease-in-out' : undefined,
          }}>
            <img
              src={spriteUrl(enemy.pokeId, 'front')}
              alt={enemy.name}
              style={{
                imageRendering: 'pixelated',
                width: isBoss ? 130 : 96, height: isBoss ? 130 : 96,
                filter: enemy.currentHP === 0
                  ? 'grayscale(1) opacity(0.3)'
                  : isBoss
                  ? 'drop-shadow(0 0 14px #ef4444) drop-shadow(0 0 6px #f97316) brightness(1.05)'
                  : 'drop-shadow(0 4px 10px rgba(0,0,0,0.7))',
                animation: enemy.currentHP > 0 && enemyAnim === 'idle'
                  ? (shake ? 'shake 0.45s ease-in-out' : 'bounce 2s ease-in-out infinite')
                  : undefined,
              }}
            />
          </div>
        )}

        {/* ── Player sprite ────────────────────────────────────── */}
        {activePoke && (
          <div style={{
            position: 'absolute', bottom: 40, left: 6,
            animation:
              attackAnim === 'lunge' ? 'lungeFull 0.75s ease-in-out'
              : attackAnim === 'tail' ? 'lungeSpinFull 0.7s ease-in-out'
              : undefined,
          }}>
            <img
              src={activePoke.isShiny ? spriteUrl(activePoke.pokeId, 'shiny') : spriteUrl(activePoke.pokeId, 'back')}
              alt={activePoke.name}
              style={{
                imageRendering: 'pixelated', width: 110, height: 110,
                filter: activePoke.currentHP === 0
                  ? 'grayscale(1) opacity(0.3)'
                  : attackAnim === 'status' || attackAnim === 'wind'
                  ? 'brightness(2.5) saturate(3) hue-rotate(30deg)'
                  : attackAnim === 'shadow'
                  ? 'brightness(0.4) saturate(0)'
                  : 'drop-shadow(0 4px 12px rgba(99,102,241,0.5))',
                transform: 'scaleX(-1)',
                animation: activePoke.currentHP > 0 && attackAnim === 'idle'
                  ? (playerShake ? 'shake 0.45s ease-in-out' : 'bounce 2.2s ease-in-out infinite')
                  : undefined,
                transition: 'filter 0.15s',
              }}
            />
          </div>
        )}
      </div>

      {/* ── Info panels: player left, enemy right ──────────────── */}
      <div style={{ display: 'flex', gap: 8, alignItems: 'stretch' }}>
        {activePoke && (
          <div style={{
            flex: 1, background: 'rgba(10,14,30,0.9)', borderRadius: 10, padding: '8px 10px',
            border: '1px solid #1e3a5f',
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 5, marginBottom: 3 }}>
              <span style={{ fontWeight: 900, fontSize: 12, color: '#fff', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{activePoke.displayName}</span>
              {activePoke.isShiny && <span style={{ fontSize: 12 }}>✨</span>}
              {activePoke.battleStatus && <StatusBadge status={activePoke.battleStatus} />}
            </div>
            <div style={{ display: 'flex', gap: 3, marginBottom: 5, flexWrap: 'wrap' }}>
              {activePoke.types.map(t => <TypeBadge key={t} type={t} />)}
            </div>
            <HpBar current={activePoke.currentHP} max={activePoke.maxHP} label="HP" />
            <div style={{ fontSize: 9, color: '#475569', marginTop: 3, display: 'flex', gap: 5 }}>
              <span>ATK<StageLabel stage={activePoke.atkStage} /></span>
              <span>DEF<StageLabel stage={activePoke.defStage} /></span>
              <span>SPD<StageLabel stage={activePoke.spdStage} /></span>
            </div>
          </div>
        )}
        {enemy && (
          <div style={{
            flex: 1, background: 'rgba(10,14,30,0.9)', borderRadius: 10, padding: '8px 10px',
            border: isBoss ? '1px solid #ef444455' : '1px solid #1e293b',
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 5, marginBottom: 3 }}>
              {isBoss && <span style={{ color: '#ef4444', fontSize: 9, fontWeight: 700, fontFamily: "'Press Start 2P', monospace", flexShrink: 0 }}>BOSS</span>}
              <span style={{ fontWeight: 900, fontSize: 12, color: '#fff', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{enemy.name}</span>
              {enemy.battleStatus && <StatusBadge status={enemy.battleStatus} />}
            </div>
            <div style={{ display: 'flex', gap: 3, marginBottom: 5, flexWrap: 'wrap' }}>
              {enemy.types.map(t => <TypeBadge key={t} type={t} />)}
            </div>
            <HpBar current={enemy.currentHP} max={enemy.maxHP} label="HP" />
            <div style={{ fontSize: 9, color: '#475569', marginTop: 3, display: 'flex', gap: 5 }}>
              <span>ATK<StageLabel stage={enemy.atkStage} /></span>
              <span>DEF<StageLabel stage={enemy.defStage} /></span>
              <span>SPD<StageLabel stage={enemy.spdStage} /></span>
              {enemy.goldReward > 0 && <span style={{ color: '#fbbf24' }}>💰{enemy.goldReward}</span>}
            </div>
          </div>
        )}
      </div>

      {/* ── Move buttons ──────────────────────────────────────── */}
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
        {activePoke?.moves.map((m, i) => (
          <MoveBtn
            key={`${m.name}_${i}`}
            move={m} idx={i}
            channelId={run.channelId}
            busy={busy} locked={attackLocked}
            onAttack={handleAttack}
          />
        ))}
        {allMovesEmpty && activePoke && (
          <button
            className="btn-hover"
            disabled={busy || attackLocked}
            onClick={() => { if (!busy && !attackLocked) handleAttack('Physical', 'normal', `tower_move_${run.channelId}_99`, '掙扎'); }}
            style={{
              background: 'linear-gradient(135deg, #ef444433, #ef444411)',
              color: '#fca5a5', border: '1px solid #ef444466',
              borderRadius: 10, padding: '8px 10px',
              cursor: busy ? 'not-allowed' : 'pointer',
              display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2,
              flex: '1 1 100%',
            }}
          >
            <span style={{ fontSize: 18 }}>👊</span>
            <span style={{ fontWeight: 700, fontSize: 11 }}>普通攻擊</span>
            <span style={{ fontSize: 9, color: '#94a3b8' }}>PP歸零時的緊急手段</span>
          </button>
        )}
      </div>

      {/* ── Swap button ───────────────────────────────────────── */}
      {run.team.filter(p => p.currentHP > 0).length > 1 && !run.swapPending && (
        <button
          className="btn-hover"
          disabled={busy}
          onClick={() => onAction(`tower_swap_request_${run.channelId}`)}
          style={{
            background: 'linear-gradient(135deg, #1e293b 0%, #0f172a 100%)',
            border: '1px solid #33415566', borderRadius: 10, padding: '8px 14px',
            color: '#94a3b8', cursor: busy ? 'not-allowed' : 'pointer',
            fontSize: 12, fontWeight: 700,
            display: 'flex', alignItems: 'center', gap: 6,
          }}
        >🔄 換隊員</button>
      )}

      {/* ── Team picker overlay ───────────────────────────────── */}
      {run.swapPending && (
        <div className="anim-fade-in" style={{
          background: '#0a1020', border: '1px solid #334155',
          borderRadius: 12, padding: '12px 14px',
          display: 'flex', flexDirection: 'column', gap: 8,
        }}>
          <div style={{ fontSize: 12, color: '#94a3b8', fontWeight: 700 }}>🔄 選擇換上場的寶可夢：</div>
          {run.team.map((pk, i) => {
            if (i === run.activeIndex || pk.currentHP <= 0) return null;
            return (
              <button key={i} className="btn-hover" disabled={busy}
                onClick={() => onAction(`tower_swap_${run.channelId}_${i}`)}
                style={{
                  background: '#0f172a', border: '1px solid #6366f133',
                  borderRadius: 10, padding: '10px 14px',
                  color: '#fff', cursor: busy ? 'not-allowed' : 'pointer',
                  display: 'flex', alignItems: 'center', gap: 10, textAlign: 'left',
                }}
              >
                <img src={spriteUrl(pk.pokeId, 'front')} alt={pk.displayName}
                  style={{ width: 40, height: 40, imageRendering: 'pixelated', flexShrink: 0 }} />
                <div>
                  <div style={{ fontWeight: 700, fontSize: 13 }}>{pk.displayName}</div>
                  <div style={{ fontSize: 11, color: '#64748b' }}>HP {pk.currentHP}/{pk.maxHP}</div>
                </div>
              </button>
            );
          })}
          <button className="btn-hover" disabled={busy}
            onClick={() => onAction(`tower_swap_cancel_${run.channelId}`)}
            style={{
              background: 'transparent', border: '1px solid #334155',
              borderRadius: 8, padding: '8px',
              color: '#64748b', cursor: busy ? 'not-allowed' : 'pointer', fontSize: 12,
            }}
          >❌ 取消</button>
        </div>
      )}

      {/* ── Battle log ────────────────────────────────────────── */}
      <BattleLog logs={run.battleLog} />
    </div>
  );
}
