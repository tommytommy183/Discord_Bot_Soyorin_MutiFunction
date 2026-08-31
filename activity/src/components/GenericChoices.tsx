import type { TowerRun } from '../types';

interface Props {
  run: TowerRun;
  onAction: (customId: string) => void;
  busy: boolean;
}

const STATE_TITLES: Record<string, string> = {
  Shopping:              '🛍️ 商店',
  SelectingEvent:        '🎉 事件',
  SelectingMoveReward:   '⬆️ 學習新技能',
  SelectingMoveSlot:     '🔄 選擇替換的技能槽',
  SelectingCatch:        '⚾ 捕捉寶可夢',
  SelectingCatchSwap:    '🔄 替換隊伍成員',
  Resting:               '🏕️ 休息',
  SelectingPowerUpgrade: '💪 強化技能',
  SelectingRelic:        '🔮 選擇遺物',
  InCasino:              '🎰 賭場',
  SelectingPassive:      '✨ 被動技能',
  SelectingCursedRelic:  '💀 詛咒遺物',
};

export function GenericChoices({ run, onAction, busy }: Props) {
  const title = STATE_TITLES[run.state] ?? run.state;
  const opts = run.pathOptions;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16, alignItems: 'center', padding: '8px 0' }}>
      <div style={{ fontSize: 22, fontWeight: 700, color: '#fff' }}>{title}</div>
      <div style={{ color: '#94a3b8', fontSize: 13 }}>💰 {run.gold} 金幣</div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 8, width: '100%' }}>
        {opts.map((opt) => (
          <button
            key={opt.customId}
            disabled={busy}
            onClick={() => onAction(opt.customId)}
            style={{
              background: '#1e293b',
              border: '1px solid #334155',
              borderRadius: 10,
              padding: '12px 16px',
              cursor: busy ? 'not-allowed' : 'pointer',
              color: '#fff',
              display: 'flex', alignItems: 'center', gap: 10,
              transition: 'all 0.15s',
              textAlign: 'left',
            }}
            onMouseEnter={e => { if (!busy) (e.currentTarget as HTMLElement).style.background = '#334155'; }}
            onMouseLeave={e => { (e.currentTarget as HTMLElement).style.background = '#1e293b'; }}
          >
            <span style={{ fontSize: 22 }}>{opt.emoji}</span>
            <div>
              <div style={{ fontWeight: 700, fontSize: 14 }}>{opt.label}</div>
              {opt.description && <div style={{ fontSize: 12, color: '#94a3b8', marginTop: 2 }}>{opt.description}</div>}
            </div>
          </button>
        ))}
      </div>
    </div>
  );
}
