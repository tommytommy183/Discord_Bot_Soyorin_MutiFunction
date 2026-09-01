import type { TowerRun } from '../types';
import { spriteUrl } from '../utils';

interface Props {
  run: TowerRun;
  onAction: (customId: string) => void;
  busy: boolean;
}

// States where we show the compact team panel + swap lead option
const SHOW_TEAM_PANEL_STATES = new Set([
  'Shopping', 'Resting', 'SelectingPowerUpgrade', 'SelectingRelic', 'SelectingCursedRelic',
]);

function CompactTeamPanel({ run, onAction, busy }: Props) {
  return (
    <div style={{
      background: '#07090f',
      borderRadius: 10,
      border: '1px solid #1e293b',
      padding: '8px 10px',
    }}>
      <div style={{ fontSize: 10, color: '#475569', fontWeight: 700, marginBottom: 6, letterSpacing: '0.05em' }}>
        👥 當前隊伍
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
        {run.team.map((p, i) => {
          const fainted = p.currentHP === 0;
          const isLead = i === 0;
          const hpPct = Math.max(0, Math.round(p.currentHP / p.maxHP * 100));
          const hpColor = hpPct > 50 ? '#22c55e' : hpPct > 25 ? '#f59e0b' : '#ef4444';
          return (
            <div key={i} style={{
              display: 'flex', alignItems: 'center', gap: 7,
              opacity: fainted ? 0.45 : 1,
            }}>
              <img
                src={spriteUrl(p.pokeId)}
                alt={p.name}
                style={{ width: 30, height: 30, imageRendering: 'pixelated', flexShrink: 0,
                  filter: fainted ? 'grayscale(1)' : isLead ? 'drop-shadow(0 0 4px #6366f1)' : undefined }}
              />
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 5, marginBottom: 2 }}>
                  <span style={{ fontSize: 11, fontWeight: 700, color: isLead ? '#a5b4fc' : '#94a3b8', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {isLead && '⭐ '}{p.displayName}
                  </span>
                  <span style={{ fontSize: 9, color: hpColor, flexShrink: 0 }}>
                    {fainted ? 'FNT' : `${p.currentHP}/${p.maxHP}`}
                  </span>
                </div>
                <div style={{ height: 4, borderRadius: 2, background: '#1e293b', overflow: 'hidden' }}>
                  <div style={{ height: '100%', width: `${hpPct}%`, background: hpColor, transition: 'width 0.3s' }} />
                </div>
              </div>
              {/* Swap lead button — only for alive non-lead */}
              {!isLead && !fainted && (
                <button
                  disabled={busy}
                  onClick={() => onAction(`tower_setlead_${run.channelId}_${i}`)}
                  title="設為首發"
                  style={{
                    background: 'none', border: '1px solid #6366f155', borderRadius: 5,
                    color: '#6366f1', fontSize: 10, padding: '2px 6px',
                    cursor: busy ? 'not-allowed' : 'pointer', flexShrink: 0, fontWeight: 700,
                  }}
                >
                  首發
                </button>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

const STATE_CONFIG: Record<string, { title: string; color: string; bg: string; hint: string }> = {
  Shopping:              { title: '🛍️ 商店', color: '#3b82f6', bg: '#1e3a5f', hint: '選擇購買的道具' },
  SelectingEvent:        { title: '🎉 隨機事件', color: '#a855f7', bg: '#2d1b69', hint: '選擇事件選項' },
  SelectingMoveReward:   { title: '⬆️ 學習技能', color: '#10b981', bg: '#064e3b', hint: '選擇要學的新技能' },
  SelectingMoveSlot:     { title: '🔄 替換技能槽', color: '#f59e0b', bg: '#451a03', hint: '選擇要替換的技能' },
  SelectingCatch:        { title: '⚾ 捕捉寶可夢', color: '#ec4899', bg: '#500724', hint: '要嘗試捕捉嗎？' },
  SelectingCatchSwap:    { title: '🔄 替換隊員', color: '#f97316', bg: '#431407', hint: '選擇要換下場的隊員' },
  Resting:               { title: '🏕️ 休息地點', color: '#22c55e', bg: '#052e16', hint: '選擇休息方式' },
  SelectingPowerUpgrade: { title: '💪 強化技能', color: '#ef4444', bg: '#450a0a', hint: '選擇要強化的技能' },
  SelectingRelic:        { title: '🔮 選擇遺物', color: '#8b5cf6', bg: '#2e1065', hint: '遺物會提供持續效果' },
  InCasino:              { title: '🎰 賭場', color: '#f59e0b', bg: '#451a03', hint: '用金幣試試手氣！' },
  SelectingPassive:      { title: '✨ 被動技能', color: '#fbbf24', bg: '#451a03', hint: '選擇永久被動效果' },
  SelectingCursedRelic:  { title: '💀 詛咒遺物', color: '#dc2626', bg: '#450a0a', hint: '危險！伴隨詛咒的強力道具' },
  InMiniGame2048:        { title: '🎮 2048 迷你遊戲', color: '#6366f1', bg: '#1e1b4b', hint: '滑動合併數字！' },
  InMiniGameMine:        { title: '💣 踩地雷', color: '#f97316', bg: '#431407', hint: '小心！' },
  InMiniGameQuiz:        { title: '❓ Pokemon 問答', color: '#38bdf8', bg: '#0c2233', hint: '考驗你的知識！' },
  ShowingEventResult:    { title: '📋 事件結果', color: '#a855f7', bg: '#2d1b69', hint: '確認後繼續前進' },
};

export function GenericChoices({ run, onAction, busy }: Props) {
  const cfg = STATE_CONFIG[run.state] ?? { title: run.state, color: '#6366f1', bg: '#1e1b4b', hint: '' };
  const opts = run.pathOptions;

  return (
    <div className="anim-fade-in" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>

      {/* Header */}
      <div style={{
        background: `linear-gradient(135deg, ${cfg.bg} 0%, #0a0e1a 100%)`,
        borderRadius: 12, padding: '14px 18px',
        border: `1px solid ${cfg.color}33`,
        textAlign: 'center',
      }}>
        <div style={{
          fontSize: 22, fontWeight: 900, color: cfg.color,
          marginBottom: 4,
        }}>
          {/* Event: show actual event title with emoji */}
          {run.state === 'SelectingEvent' && run.eventTitle
            ? `${run.eventEmoji ?? '🎉'} ${run.eventTitle}`
            : cfg.title}
        </div>
        {/* Event narrative description */}
        {run.state === 'SelectingEvent' && run.eventDesc && (
          <div style={{
            fontSize: 12, color: '#cbd5e1', lineHeight: 1.7,
            background: 'rgba(0,0,0,0.3)', borderRadius: 8,
            padding: '10px 12px', margin: '8px 0', textAlign: 'left',
            border: '1px solid #1e293b',
          }}>
            {run.eventDesc}
          </div>
        )}
        {/* Event result text (ShowingEventResult state) */}
        {run.state === 'ShowingEventResult' && run.eventResultText && (
          <div style={{
            fontSize: 12, color: '#cbd5e1', lineHeight: 1.8,
            background: 'rgba(0,0,0,0.35)', borderRadius: 8,
            padding: '12px 14px', margin: '8px 0', textAlign: 'left',
            border: '1px solid #3d2069', whiteSpace: 'pre-line',
          }}>
            {run.eventResultText}
          </div>
        )}
        {cfg.hint && run.state !== 'SelectingEvent' && <div style={{ fontSize: 12, color: '#64748b' }}>{cfg.hint}</div>}
        <div style={{
          display: 'inline-flex', alignItems: 'center', gap: 6, marginTop: 8,
          background: '#1a1400', borderRadius: 6, padding: '4px 12px',
          border: '1px solid #3d2e00',
        }}>
          <span style={{ fontSize: 14 }}>💰</span>
          <span style={{ fontWeight: 700, color: '#fbbf24', fontSize: 14 }}>{run.gold}</span>
        </div>
      </div>

      {/* Compact team panel for non-battle screens */}
      {SHOW_TEAM_PANEL_STATES.has(run.state) && (
        <CompactTeamPanel run={run} onAction={onAction} busy={busy} />
      )}

      {/* Options */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        {opts.map((opt, i) => (
          <button
            key={opt.customId}
            className="btn-hover"
            disabled={busy || !!opt.disabled}
            onClick={() => !opt.disabled && onAction(opt.customId)}
            style={{
              background: '#0f172a',
              border: `1px solid ${cfg.color}33`,
              borderRadius: 10,
              padding: '12px 16px',
              cursor: busy ? 'not-allowed' : 'pointer',
              color: '#fff',
              display: 'flex', alignItems: 'center', gap: 12,
              textAlign: 'left',
              animation: `fadeIn 0.2s ease ${i * 0.05}s both`,
            }}
          >
            <div style={{
              width: 44, height: 44,
              borderRadius: 10,
              background: `${cfg.color}22`,
              border: `1px solid ${cfg.color}44`,
              display: 'flex', alignItems: 'center', justifyContent: 'center',
              fontSize: 22, flexShrink: 0,
            }}>
              {opt.emoji}
            </div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ fontWeight: 700, fontSize: 14, color: '#e2e8f0' }}>{opt.label}</div>
              {opt.description && (
                <div style={{ fontSize: 12, color: '#64748b', marginTop: 3, lineHeight: 1.4 }}>
                  {opt.description}
                </div>
              )}
            </div>
            <div style={{ color: cfg.color, opacity: 0.5, fontSize: 16, flexShrink: 0 }}>›</div>
          </button>
        ))}
      </div>
    </div>
  );
}
