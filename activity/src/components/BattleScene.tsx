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

type AttackAnim = 'idle' | 'lunge' | 'beam' | 'projectile' | 'status';

function StageLabel({ stage }: { stage: number }) {
  if (stage === 0) return null;
  return (
    <span style={{ color: stage > 0 ? '#4ade80' : '#f87171', fontSize: 10, fontWeight: 700, marginLeft: 2 }}>
      {stage > 0 ? `▲${stage}` : `▼${Math.abs(stage)}`}
    </span>
  );
}

function MoveBtn({ move, idx, channelId, onAction, busy, onAttack }: {
  move: TowerMove; idx: number; channelId: string;
  onAction: (id: string) => void; busy: boolean;
  onAttack: (category: string, moveType: string) => void;
}) {
  const color = typeColor(move.type);
  const empty = move.currentPP === 0;
  const customId = `tower_move_${channelId}_${idx}`;
  const ppRatio = move.maxPP > 0 ? move.currentPP / move.maxPP : 0;
  const ppColor = ppRatio === 0 ? '#ef4444' : ppRatio <= 0.25 ? '#facc15' : '#94a3b8';

  return (
    <button
      className="btn-hover"
      disabled={busy || empty}
      onClick={() => { if (!busy && !empty) { onAttack(move.category, move.type); onAction(customId); } }}
      style={{
        background: empty
          ? '#1a1f2e'
          : `linear-gradient(135deg, ${color}44 0%, ${color}22 100%)`,
        color: empty ? '#475569' : '#fff',
        border: `1px solid ${empty ? '#334155' : color + '66'}`,
        borderRadius: 10,
        padding: '8px 10px',
        cursor: busy || empty ? 'not-allowed' : 'pointer',
        opacity: busy || empty ? 0.55 : 1,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 2,
        flex: '1 1 calc(50% - 4px)',
        minWidth: 0,
        position: 'relative',
        overflow: 'hidden',
      }}
    >
      {!empty && (
        <div style={{
          position: 'absolute', top: 0, left: 0, right: 0, height: 3,
          background: color, borderRadius: '10px 10px 0 0',
        }} />
      )}
      <span style={{ fontSize: 18, lineHeight: 1, marginTop: 2 }}>{move.emoji}</span>
      <span style={{ fontWeight: 700, fontSize: 11, textAlign: 'center', lineHeight: 1.2 }}>{move.name}</span>
      <div style={{ display: 'flex', alignItems: 'center', gap: 3, fontSize: 9 }}>
        <span style={{ color: '#64748b' }}>
          {['physical','Physical'].includes(move.category) ? '物攻' : ['special','Special'].includes(move.category) ? '特攻' : '變化'}
          {move.power > 0 && ` ${move.power}`}
        </span>
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 3, fontSize: 9, color: ppColor }}>
        <span>PP</span>
        <span style={{ fontWeight: 700 }}>{move.currentPP}</span>
        <span style={{ opacity: 0.5 }}>/{move.maxPP}</span>
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
  const [shake, setShake] = useState(false);           // enemy shakes
  const [playerShake, setPlayerShake] = useState(false); // player shakes (enemy attack)
  const [attackAnim, setAttackAnim] = useState<AttackAnim>('idle');
  const [enemyAnim, setEnemyAnim] = useState<AttackAnim>('idle');
  const [currentMoveType, setCurrentMoveType] = useState<string>('normal');
  const [enemyMoveType, setEnemyMoveType] = useState<string>('normal');
  // Track last log length to detect enemy attacks
  const prevLogLen = useRef(0);

  // Detect enemy action from new battle log entries
  useEffect(() => {
    const logs = run.battleLog;
    if (logs.length > prevLogLen.current) {
      const newLogs = logs.slice(prevLogLen.current);
      prevLogLen.current = logs.length;
      const enemyName = enemy?.name ?? '';
      const enemyAttacked = newLogs.some(l => l.includes(enemyName) && (l.includes('使用') || l.includes('造成')));
      if (enemyAttacked) {
        // Trigger enemy attack animation
        setPlayerShake(true);
        setTimeout(() => setPlayerShake(false), 600);
        const moveLine = newLogs.find(l => l.includes(enemyName) && l.includes('使用'));
        // Pick a random enemy move type for visual
        const etype = enemy?.moves?.[0]?.type ?? '一般';
        setEnemyMoveType(etype);
        const isPhys = moveLine?.includes('物攻') ?? Math.random() > 0.5;
        setEnemyAnim(isPhys ? 'lunge' : 'projectile');
        setTimeout(() => setEnemyAnim('idle'), 700);
      }
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [run.battleLog.length]);

  function handleAttack(category: string, moveType?: string) {
    setShake(true);
    setTimeout(() => setShake(false), 650);
    setCurrentMoveType(moveType ?? '一般');
    if (category === 'Physical' || category === 'physical') {
      setAttackAnim('lunge');
      setTimeout(() => setAttackAnim('idle'), 750);
    } else if (category === 'Special' || category === 'special') {
      setAttackAnim('projectile');
      setTimeout(() => setAttackAnim('idle'), 750);
    } else {
      setAttackAnim('status');
      setTimeout(() => setAttackAnim('idle'), 750);
    }
  }

  const bgGradient = isBoss
    ? 'radial-gradient(ellipse at 60% 40%, #2d0a0a 0%, #0a0e1a 100%)'
    : 'radial-gradient(ellipse at 60% 40%, #0d1e30 0%, #0a0e1a 100%)';

  // All moves empty?
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

      {/* ── Battle Arena ─────────────────────── */}
      <div style={{
        position: 'relative',
        background: bgGradient,
        borderRadius: 14,
        border: isBoss ? '1px solid #7f1d1d' : '1px solid #1e2d45',
        height: 220, overflow: 'visible',
      }}>
        {/* Enemy info box: top-right */}
        {enemy && (
          <div style={{
            position: 'absolute', top: 8, right: 8,
            background: 'rgba(0,0,0,0.6)', borderRadius: 8, padding: '5px 10px',
            border: isBoss ? '1px solid #ef444455' : '1px solid #1e293b',
            minWidth: 130, maxWidth: 170, backdropFilter: 'blur(4px)',
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 5, marginBottom: 2 }}>
              {isBoss && <span style={{ color: '#ef4444', fontSize: 9, fontWeight: 700, fontFamily: "'Press Start 2P', monospace" }}>BOSS</span>}
              <span style={{ fontWeight: 900, fontSize: 12, color: '#fff' }}>{enemy.name}</span>
              {enemy.battleStatus && <StatusBadge status={enemy.battleStatus} />}
            </div>
            <div style={{ display: 'flex', gap: 3, marginBottom: 4 }}>
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

        {/* Enemy sprite: middle-right — moves left on lunge */}
        {enemy && (
          <div style={{
            position: 'absolute', top: 78, right: 12,
            animation: enemyAnim === 'lunge' ? 'enemyLunge 0.7s ease-in-out' : undefined,
          }}>
            <img
              src={spriteUrl(enemy.pokeId, 'front')}
              alt={enemy.name}
              style={{
                imageRendering: 'pixelated', width: 98, height: 98,
                filter: enemy.currentHP === 0
                  ? 'grayscale(1) opacity(0.3)'
                  : isBoss ? 'drop-shadow(0 0 14px #ef4444)' : 'drop-shadow(0 4px 10px rgba(0,0,0,0.7))',
                animation: enemy.currentHP > 0 && enemyAnim === 'idle'
                  ? (shake ? 'shake 0.45s ease-in-out' : isBoss ? 'bossGlow 1.5s ease-in-out infinite' : 'bounce 2s ease-in-out infinite')
                  : undefined,
              }}
            />
          </div>
        )}

        {/* Player beam — from player toward enemy */}
        {attackAnim === 'beam' && (
          <div style={{
            position: 'absolute',
            bottom: 135, left: 110,
            height: 8, borderRadius: 4, zIndex: 10, pointerEvents: 'none',
            background: `linear-gradient(90deg, ${typeColor(currentMoveType)}, #ffffff88)`,
            boxShadow: `0 0 16px 6px ${typeColor(currentMoveType)}`,
            animation: 'beamExpand 0.65s ease-out forwards',
          }} />
        )}

        {/* Enemy beam — from enemy toward player */}
        {enemyAnim === 'projectile' && (
          <div style={{
            position: 'absolute',
            bottom: 135, right: 110,
            height: 8, borderRadius: 4, zIndex: 10, pointerEvents: 'none',
            background: `linear-gradient(270deg, ${typeColor(enemyMoveType)}, #ffffff88)`,
            boxShadow: `0 0 16px 6px ${typeColor(enemyMoveType)}`,
            animation: 'beamExpand 0.65s ease-out forwards',
            transformOrigin: 'right',
          }} />
        )}

        {/* Player projectile — flies toward enemy */}
        {attackAnim === 'projectile' && (
          <div key={Date.now() + 'proj'} style={{
            position: 'absolute',
            bottom: 132, left: 112,
            width: 20, height: 20, borderRadius: '50%', zIndex: 10, pointerEvents: 'none',
            background: typeColor(currentMoveType),
            boxShadow: `0 0 18px 8px ${typeColor(currentMoveType)}`,
            animation: 'projectileFly 0.65s ease-in forwards',
          }} />
        )}

        {/* Player sprite: middle-left (with lunge wrapper — moves right toward enemy) */}
        {activePoke && (
          <div style={{
            position: 'absolute', bottom: 86, left: 6,
            animation: attackAnim === 'lunge' ? 'lungeFull 0.7s ease-in-out' : undefined,
          }}>
            <img
              src={activePoke.isShiny ? spriteUrl(activePoke.pokeId, 'shiny') : spriteUrl(activePoke.pokeId, 'back')}
              alt={activePoke.name}
              style={{
                imageRendering: 'pixelated', width: 110, height: 110,
                filter: activePoke.currentHP === 0
                  ? 'grayscale(1) opacity(0.3)'
                  : attackAnim === 'status'
                  ? 'brightness(2.5) saturate(3) hue-rotate(30deg)'
                  : 'drop-shadow(0 4px 12px rgba(99,102,241,0.5))',
                transform: 'scaleX(-1)',
                animation: activePoke.currentHP > 0 && attackAnim === 'idle'
                  ? (playerShake ? 'shake 0.45s ease-in-out' : 'bounce 2.2s ease-in-out infinite')
                  : undefined,
                transition: 'filter 0.2s',
              }}
            />
          </div>
        )}

        {/* Player info box: bottom-left */}
        {activePoke && (
          <div style={{
            position: 'absolute', bottom: 8, left: 8,
            background: 'rgba(0,0,0,0.6)', borderRadius: 8, padding: '5px 10px',
            border: '1px solid #1e3a5f', minWidth: 130, maxWidth: 170, backdropFilter: 'blur(4px)',
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 5, marginBottom: 2 }}>
              <span style={{ fontWeight: 900, fontSize: 12, color: '#fff' }}>{activePoke.displayName}</span>
              {activePoke.isShiny && <span>✨</span>}
              {activePoke.battleStatus && <StatusBadge status={activePoke.battleStatus} />}
            </div>
            <div style={{ display: 'flex', gap: 3, marginBottom: 4 }}>
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
      </div>

      {/* Move buttons */}
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
        {activePoke?.moves.map((m, i) => (
          <MoveBtn
            key={`${m.name}_${i}`}
            move={m}
            idx={i}
            channelId={run.channelId}
            onAction={onAction}
            busy={busy}
            onAttack={handleAttack}
          />
        ))}
        {/* 普通攻擊 fallback when all PP = 0 */}
        {allMovesEmpty && activePoke && (
          <button
            className="btn-hover"
            disabled={busy}
            onClick={() => { if (!busy) { handleAttack('Physical', 'normal'); onAction(`tower_move_${run.channelId}_99`); } }}
            style={{
              background: 'linear-gradient(135deg, #ef444433, #ef444411)',
              color: '#fca5a5',
              border: '1px solid #ef444466',
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

      {/* Swap button row */}
      {run.team.filter(p => p.currentHP > 0).length > 1 && !run.swapPending && (
        <button
          className="btn-hover"
          disabled={busy}
          onClick={() => onAction(`tower_swap_request_${run.channelId}`)}
          style={{
            background: 'linear-gradient(135deg, #1e293b 0%, #0f172a 100%)',
            border: '1px solid #33415566',
            borderRadius: 10, padding: '8px 14px',
            color: '#94a3b8', cursor: busy ? 'not-allowed' : 'pointer',
            fontSize: 12, fontWeight: 700,
            display: 'flex', alignItems: 'center', gap: 6,
          }}
        >
          🔄 換隊員
        </button>
      )}

      {/* Team picker overlay (when swapPending) */}
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
              <button key={i}
                className="btn-hover"
                disabled={busy}
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
                  <div style={{ fontSize: 11, color: '#64748b' }}>
                    HP {pk.currentHP}/{pk.maxHP}
                  </div>
                </div>
              </button>
            );
          })}
          <button
            className="btn-hover"
            disabled={busy}
            onClick={() => onAction(`tower_swap_cancel_${run.channelId}`)}
            style={{
              background: 'transparent', border: '1px solid #334155',
              borderRadius: 8, padding: '8px',
              color: '#64748b', cursor: busy ? 'not-allowed' : 'pointer',
              fontSize: 12,
            }}
          >
            ❌ 取消
          </button>
        </div>
      )}

      {/* Battle log */}
      <BattleLog logs={run.battleLog} />
    </div>
  );
}
