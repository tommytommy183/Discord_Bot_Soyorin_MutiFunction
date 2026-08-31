import { HpBar } from './HpBar';
import { StatusBadge } from './StatusBadge';
import { TypeBadge } from './TypeBadge';
import type { TowerRun, TowerMove } from '../types';

interface Props {
  run: TowerRun;
  onAction: (customId: string) => void;
  busy: boolean;
}

function StageArrow({ stage }: { stage: number }) {
  if (stage === 0) return null;
  return (
    <span style={{ color: stage > 0 ? '#4ade80' : '#f87171', fontSize: 12, marginLeft: 4 }}>
      {stage > 0 ? `▲${stage}` : `▼${Math.abs(stage)}`}
    </span>
  );
}

function PokemonCard({ poke, isBack }: { poke: NonNullable<TowerRun['team'][0]>; isBack?: boolean }) {
  const spriteUrl = isBack
    ? (poke.backImageUrl || `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/back/${poke.pokeId}.png`)
    : (poke.imageUrl || `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/${poke.pokeId}.png`);

  return (
    <div style={{ textAlign: isBack ? 'left' : 'right', flex: 1 }}>
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: isBack ? 'flex-start' : 'flex-end', gap: 4 }}>
        <div style={{ display: 'flex', gap: 4, alignItems: 'center', flexDirection: isBack ? 'row' : 'row-reverse' }}>
          <span style={{ fontWeight: 700, fontSize: 16, color: '#fff' }}>
            {isBack ? poke.displayName : poke.name}
          </span>
          {poke.battleStatus && <StatusBadge status={poke.battleStatus} />}
          {poke.isShiny && <span title="Shiny">✨</span>}
        </div>
        <div style={{ display: 'flex', gap: 4, flexDirection: isBack ? 'row' : 'row-reverse' }}>
          {poke.types.map(t => <TypeBadge key={t} type={t} />)}
        </div>
        <div style={{ width: '100%', maxWidth: 200 }}>
          <HpBar current={poke.currentHP} max={poke.maxHP} label="HP" />
        </div>
        <div style={{ fontSize: 12, color: '#aaa' }}>
          ATK<StageArrow stage={poke.atkStage} /> DEF<StageArrow stage={poke.defStage} />
          {' '}SPD<StageArrow stage={poke.spdStage} />
        </div>
      </div>
      <img
        src={spriteUrl}
        alt={poke.name}
        style={{
          imageRendering: 'pixelated',
          width: isBack ? 96 : 80,
          height: isBack ? 96 : 80,
          filter: poke.currentHP === 0 ? 'grayscale(1) opacity(0.4)' : 'drop-shadow(0 4px 8px rgba(0,0,0,.5))',
          transform: isBack ? 'scaleX(-1)' : 'none',
        }}
      />
    </div>
  );
}

const TYPE_COLORS: Record<string, string> = {
  normal: '#9ca3af', fire: '#f97316', water: '#3b82f6', electric: '#eab308',
  grass: '#22c55e', ice: '#38bdf8', fighting: '#dc2626', poison: '#a855f7',
  ground: '#a16207', flying: '#818cf8', psychic: '#ec4899', bug: '#84cc16',
  rock: '#78716c', ghost: '#7c3aed', dragon: '#6366f1', dark: '#374151',
  steel: '#94a3b8', fairy: '#f472b6',
};

function MoveBtn({ move, onAction, busy }: {
  move: TowerMove; onAction: (id: string) => void; busy: boolean;
}) {
  const color = TYPE_COLORS[move.type?.toLowerCase()] ?? '#6b7280';
  const empty = move.currentPP === 0;
  return (
    <button
      disabled={busy || empty}
      onClick={() => onAction(`tower_move_${move.name}`)}
      style={{
        background: empty ? '#374151' : color,
        color: '#fff',
        border: 'none',
        borderRadius: 8,
        padding: '10px 14px',
        cursor: busy || empty ? 'not-allowed' : 'pointer',
        opacity: busy || empty ? 0.5 : 1,
        transition: 'all 0.15s',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 2,
        minWidth: 110,
      }}
    >
      <span style={{ fontSize: 18 }}>{move.emoji}</span>
      <span style={{ fontWeight: 700, fontSize: 13 }}>{move.name}</span>
      <span style={{ fontSize: 11, opacity: 0.85 }}>
        {move.category} · {move.power > 0 ? `威力 ${move.power}` : '—'}
      </span>
      <span style={{ fontSize: 11 }}>PP {move.currentPP}/{move.maxPP}</span>
    </button>
  );
}

export function BattleScene({ run, onAction, busy }: Props) {
  const activePoke = run.team[run.activeIndex];
  const enemy = run.currentEnemy;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12, height: '100%' }}>
      {/* 樓層標題 */}
      <div style={{ textAlign: 'center', color: '#fbbf24', fontWeight: 700, fontSize: 15 }}>
        {enemy?.isBoss ? '⚠️ BOSS戰！' : ''} 第 {run.currentFloor} 層
      </div>

      {/* 對戰場景 */}
      <div style={{
        background: 'linear-gradient(180deg,#1e293b 0%,#0f172a 100%)',
        borderRadius: 12, padding: '12px 16px',
        display: 'flex', alignItems: 'flex-end', gap: 8, minHeight: 140,
        border: enemy?.isBoss ? '2px solid #ef4444' : '1px solid #334155',
      }}>
        {/* 敵方 */}
        {enemy && (
          <div style={{ flex: 1, textAlign: 'right' }}>
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 4, marginBottom: 4, alignItems: 'center' }}>
              <span style={{ fontWeight: 700, color: '#fff', fontSize: 15 }}>{enemy.name}</span>
              {enemy.battleStatus && <StatusBadge status={enemy.battleStatus} />}
              {enemy.isBoss && <span style={{ color: '#ef4444', fontWeight: 700 }}>BOSS</span>}
            </div>
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 4, marginBottom: 6 }}>
              {enemy.types.map(t => <TypeBadge key={t} type={t} />)}
            </div>
            <div style={{ maxWidth: 200, marginLeft: 'auto' }}>
              <HpBar current={enemy.currentHP} max={enemy.maxHP} label="HP" />
            </div>
            <img
              src={enemy.imageUrl || `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/${enemy.pokeId}.png`}
              alt={enemy.name}
              style={{ imageRendering: 'pixelated', width: 80, height: 80, marginTop: 4 }}
            />
          </div>
        )}

        <div style={{ width: 1, background: '#334155', alignSelf: 'stretch' }} />

        {/* 我方 */}
        {activePoke && <PokemonCard poke={activePoke} isBack />}
      </div>

      {/* 技能按鈕 */}
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, justifyContent: 'center' }}>
        {activePoke?.moves.map(m => (
          <MoveBtn key={m.name} move={m} onAction={onAction} busy={busy} />
        ))}
      </div>

      {/* 戰鬥日誌 */}
      <div style={{
        background: '#0f172a', borderRadius: 8, padding: 10,
        fontSize: 13, color: '#94a3b8', maxHeight: 90, overflowY: 'auto',
        border: '1px solid #1e293b',
      }}>
        {run.battleLog.slice(-5).reverse().map((log, i) => (
          <div key={i} style={{ marginBottom: 2, opacity: 1 - i * 0.15 }}>{log}</div>
        ))}
      </div>
    </div>
  );
}
