import { useState, useEffect } from 'react';
import type { TowerRun } from '../types';

interface Props {
  run: TowerRun;
  onAction: (customId: string) => void;
  busy: boolean;
}

const DICE_FACES = ['⚀', '⚁', '⚂', '⚃', '⚄', '⚅'];

// Extract last dice roll from pathOptions description or run log
function parseLastResult(run: TowerRun): { dice?: number; won?: boolean; jackpot?: boolean; profit?: number } {
  // pathOptions can carry description with result info via the last runLog entry
  const last = run.runLog?.[run.runLog.length - 1] ?? '';
  const diceMatch = last.match(/骰出.*?(\d)/);
  const jackpot = last.includes('JACKPOT');
  const won = last.includes('猜中') || jackpot;
  const profitMatch = last.match(/[+](\d+)💰/);
  return {
    dice: diceMatch ? parseInt(diceMatch[1]) : undefined,
    won,
    jackpot,
    profit: profitMatch ? parseInt(profitMatch[1]) : undefined,
  };
}

function MultiplierBadge({ streak }: { streak: number }) {
  if (streak === 0) return null;
  const mult = streak >= 3 ? '×4' : streak >= 2 ? '×3' : '×2.5';
  const color = streak >= 3 ? '#ef4444' : streak >= 2 ? '#f97316' : '#f59e0b';
  return (
    <div style={{
      display: 'inline-flex', alignItems: 'center', gap: 4,
      background: `${color}22`, border: `1px solid ${color}66`,
      borderRadius: 20, padding: '3px 10px',
      color, fontWeight: 900, fontSize: 13,
      animation: 'pulse 1s ease-in-out infinite',
    }}>
      🔥 連勝 {streak} 次 → 下局 {mult}
    </div>
  );
}

export function CasinoScene({ run, onAction, busy }: Props) {
  const bet = run.casinoBet ?? 0;
  const profit = run.casinoProfit ?? 0;
  const round = run.casinoRound ?? 0;
  const streak = run.casinoWinStreak ?? 0;
  const phase = bet > 0 ? 2 : 1;

  // Rolling dice animation on mount
  const [displayDice, setDisplayDice] = useState<number | null>(null);
  const [rolling, setRolling] = useState(false);
  const [resultFlash, setResultFlash] = useState<'win' | 'jackpot' | 'lose' | null>(null);

  // Trigger dice animation when runLog changes (new result)
  useEffect(() => {
    const last = run.runLog?.[run.runLog.length - 1] ?? '';
    const diceMatch = last.match(/骰出.*?(\d)/);
    if (!diceMatch) return;
    const finalDice = parseInt(diceMatch[1]);
    const jackpot = last.includes('JACKPOT');
    const won = last.includes('猜中') || jackpot;
    // Rolling animation
    setRolling(true);
    setResultFlash(null);
    let count = 0;
    const interval = setInterval(() => {
      setDisplayDice(Math.floor(Math.random() * 6) + 1);
      count++;
      if (count > 10) {
        clearInterval(interval);
        setDisplayDice(finalDice);
        setRolling(false);
        setResultFlash(jackpot ? 'jackpot' : won ? 'win' : 'lose');
        setTimeout(() => setResultFlash(null), 1200);
      }
    }, 80);
    return () => clearInterval(interval);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [run.runLog?.length]);

  // Next multiplier preview
  const nextMult = streak >= 3 ? 4 : streak >= 2 ? 3 : streak >= 1 ? 2.5 : 2;

  // Bet button options (deduplicated)
  const betOptions: { amt: number; label: string; style: string }[] = [];
  const seen = new Set<number>();
  const tryAdd = (amt: number, label: string, style: string) => {
    if (amt > 0 && amt <= run.gold && seen.add(amt)) betOptions.push({ amt, label, style });
  };
  tryAdd(10, '🟡 小注 10💰', 'primary');
  tryAdd(Math.floor(run.gold / 2), `🟠 半押 ${Math.floor(run.gold / 2)}💰`, 'primary');
  tryAdd(run.gold, `🔴 全押 ${run.gold}💰`, 'danger');

  const profitColor = profit > 0 ? '#4ade80' : profit < 0 ? '#f87171' : '#94a3b8';

  return (
    <div className="anim-fade-in" style={{
      display: 'flex', flexDirection: 'column', gap: 12,
      background: 'linear-gradient(180deg, #1c0a00 0%, #0a0e1a 100%)',
      borderRadius: 14, border: '1px solid #7c2d1266', padding: 14,
    }}>

      {/* Header */}
      <div style={{ textAlign: 'center', paddingBottom: 4 }}>
        <div style={{ fontSize: 26, fontWeight: 900, color: '#f59e0b', letterSpacing: '0.05em', textShadow: '0 0 20px #f59e0b88' }}>
          🎰 老虎機賭場
        </div>
        <div style={{ fontSize: 11, color: '#78350f', marginTop: 2 }}>高風險・高報酬・連勝越賺</div>
      </div>

      {/* Stats row */}
      <div style={{ display: 'flex', gap: 8, justifyContent: 'center' }}>
        <div style={{ background: '#1a1400', border: '1px solid #3d2e00', borderRadius: 8, padding: '6px 12px', textAlign: 'center' }}>
          <div style={{ fontSize: 9, color: '#78350f', fontWeight: 700 }}>持有金幣</div>
          <div style={{ fontSize: 16, fontWeight: 900, color: '#fbbf24' }}>💰 {run.gold}</div>
        </div>
        <div style={{ background: '#0a1a0a', border: `1px solid ${profitColor}44`, borderRadius: 8, padding: '6px 12px', textAlign: 'center' }}>
          <div style={{ fontSize: 9, color: '#166534', fontWeight: 700 }}>本場損益</div>
          <div style={{ fontSize: 16, fontWeight: 900, color: profitColor }}>
            {profit >= 0 ? '+' : ''}{profit}💰
          </div>
        </div>
        <div style={{ background: '#1a0a1a', border: '1px solid #3b0764', borderRadius: 8, padding: '6px 12px', textAlign: 'center' }}>
          <div style={{ fontSize: 9, color: '#581c87', fontWeight: 700 }}>局數</div>
          <div style={{ fontSize: 16, fontWeight: 900, color: '#a855f7' }}>{round}</div>
        </div>
      </div>

      {/* Win streak badge */}
      <div style={{ textAlign: 'center' }}>
        <MultiplierBadge streak={streak} />
      </div>

      {/* Dice display */}
      {(displayDice !== null || phase === 2) && (
        <div style={{ textAlign: 'center' }}>
          <div style={{
            fontSize: 72, lineHeight: 1,
            display: 'inline-block',
            animation: rolling ? 'spin 0.4s linear infinite' : resultFlash === 'jackpot' ? 'pulse 0.3s ease-in-out 3' : undefined,
            filter: resultFlash === 'jackpot' ? 'drop-shadow(0 0 20px #fbbf24)' : resultFlash === 'win' ? 'drop-shadow(0 0 12px #4ade80)' : resultFlash === 'lose' ? 'drop-shadow(0 0 12px #ef4444)' : undefined,
            transition: 'filter 0.3s',
          }}>
            {displayDice ? DICE_FACES[displayDice - 1] : '🎲'}
          </div>
          {resultFlash === 'jackpot' && (
            <div style={{ fontSize: 20, fontWeight: 900, color: '#fbbf24', animation: 'pulse 0.5s ease-in-out 3', marginTop: 4 }}>
              🎰 JACKPOT！ 🎰
            </div>
          )}
          {resultFlash === 'win' && !rolling && (
            <div style={{ fontSize: 16, fontWeight: 900, color: '#4ade80', marginTop: 4 }}>猜中！</div>
          )}
          {resultFlash === 'lose' && !rolling && (
            <div style={{ fontSize: 16, fontWeight: 900, color: '#ef4444', marginTop: 4 }}>猜錯！💀</div>
          )}
        </div>
      )}

      {/* Multiplier info box */}
      <div style={{
        background: '#0f0900', border: '1px solid #3d2e0088', borderRadius: 10,
        padding: '10px 14px',
      }}>
        <div style={{ fontSize: 11, color: '#78350f', fontWeight: 700, marginBottom: 6 }}>📊 倍率表</div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          {[
            { label: '首勝', mult: '×2', color: '#94a3b8' },
            { label: '連勝2', mult: '×2.5', color: '#f59e0b' },
            { label: '連勝3', mult: '×3', color: '#f97316' },
            { label: '連勝4+', mult: '×4', color: '#ef4444' },
            { label: '🎰 JACKPOT', mult: '×5', color: '#fbbf24' },
          ].map(({ label, mult, color }) => (
            <div key={label} style={{
              background: `${color}11`, border: `1px solid ${color}44`,
              borderRadius: 6, padding: '3px 8px', display: 'flex', gap: 5, alignItems: 'center',
            }}>
              <span style={{ fontSize: 10, color: '#64748b' }}>{label}</span>
              <span style={{ fontSize: 11, fontWeight: 900, color }}>{mult}</span>
            </div>
          ))}
        </div>
        <div style={{ fontSize: 10, color: '#475569', marginTop: 6 }}>
          JACKPOT = 猜大時骰到 ⚅6，或猜小時骰到 ⚀1
        </div>
      </div>

      {/* Phase 1: bet selection */}
      {phase === 1 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <div style={{ fontSize: 12, color: '#f59e0b', fontWeight: 700 }}>
            🎯 選擇籌碼（下局倍率：{nextMult}×）
          </div>
          {run.gold === 0 ? (
            <div style={{ textAlign: 'center', color: '#ef4444', fontWeight: 700, padding: 16 }}>
              💸 沒有金幣了，快逃吧！
            </div>
          ) : betOptions.map(({ amt, label, style }) => {
            const winPreview = Math.floor(amt * nextMult) - amt;
            const isAllIn = amt === run.gold;
            return (
              <button
                key={amt}
                className="btn-hover"
                disabled={busy}
                onClick={() => onAction(`tower_casino_${run.channelId}_bet_${amt}`)}
                style={{
                  background: isAllIn ? 'linear-gradient(135deg, #450a0a, #1a0000)' : 'linear-gradient(135deg, #1c1400, #0a0e1a)',
                  border: `2px solid ${isAllIn ? '#ef4444' : '#f59e0b'}55`,
                  borderRadius: 10, padding: '12px 16px',
                  cursor: busy ? 'not-allowed' : 'pointer',
                  color: '#fff', display: 'flex', alignItems: 'center', gap: 12,
                  boxShadow: isAllIn ? '0 0 16px #ef444422' : undefined,
                }}
              >
                <div style={{
                  width: 48, height: 48, borderRadius: '50%',
                  background: isAllIn ? '#ef444422' : '#f59e0b22',
                  border: `2px solid ${isAllIn ? '#ef4444' : '#f59e0b'}88`,
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  fontSize: 22, flexShrink: 0,
                }}>
                  {isAllIn ? '🔴' : amt <= 10 ? '🟡' : '🟠'}
                </div>
                <div style={{ flex: 1, textAlign: 'left' }}>
                  <div style={{ fontWeight: 800, fontSize: 14 }}>{label}</div>
                  <div style={{ fontSize: 11, color: '#64748b', marginTop: 2 }}>
                    猜中可得：<span style={{ color: '#4ade80', fontWeight: 700 }}>+{winPreview}💰</span>
                    {isAllIn && <span style={{ color: '#ef4444', marginLeft: 6 }}>全押高危！</span>}
                  </div>
                </div>
              </button>
            );
          })}
          <button
            className="btn-hover"
            disabled={busy}
            onClick={() => onAction(`tower_casino_${run.channelId}_leave`)}
            style={{
              background: '#0f172a', border: '1px solid #1e293b', borderRadius: 10,
              padding: '10px 16px', cursor: busy ? 'not-allowed' : 'pointer',
              color: '#64748b', fontSize: 13, fontWeight: 700,
            }}
          >
            🚪 拍拍屁股離開（{profit >= 0 ? `帶走 +${profit}💰` : `虧了 ${profit}💰`}）
          </button>
        </div>
      )}

      {/* Phase 2: high or low guess */}
      {phase === 2 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          <div style={{
            background: '#1c1400', borderRadius: 10,
            border: '1px solid #f59e0b44', padding: '10px 14px', textAlign: 'center',
          }}>
            <div style={{ fontSize: 13, color: '#f59e0b', fontWeight: 700 }}>
              下注籌碼：{bet}💰 　猜中可得：
              <span style={{ color: '#4ade80', fontWeight: 900 }}> +{Math.floor(bet * nextMult) - bet}💰</span>
              <span style={{ color: '#94a3b8', fontSize: 11 }}> （×{nextMult}倍）</span>
            </div>
            {streak >= 1 && (
              <div style={{ fontSize: 11, color: '#f97316', marginTop: 4 }}>
                🔥 已連勝 {streak} 次，保持下去！
              </div>
            )}
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
            {/* High button */}
            <button
              className="btn-hover"
              disabled={busy}
              onClick={() => onAction(`tower_casino_${run.channelId}_high`)}
              style={{
                background: 'linear-gradient(135deg, #1e3a5f, #0a1628)',
                border: '2px solid #3b82f6aa', borderRadius: 12,
                padding: '18px 8px', cursor: busy ? 'not-allowed' : 'pointer',
                color: '#fff', display: 'flex', flexDirection: 'column',
                alignItems: 'center', gap: 6,
                boxShadow: '0 0 16px #3b82f622',
              }}
            >
              <span style={{ fontSize: 32 }}>🔼</span>
              <span style={{ fontWeight: 900, fontSize: 15 }}>猜大</span>
              <span style={{ fontSize: 11, color: '#60a5fa' }}>4・5・6</span>
              <span style={{ fontSize: 10, color: '#1d4ed8' }}>⚅ = JACKPOT!</span>
            </button>

            {/* Low button */}
            <button
              className="btn-hover"
              disabled={busy}
              onClick={() => onAction(`tower_casino_${run.channelId}_low`)}
              style={{
                background: 'linear-gradient(135deg, #052e16, #00140a)',
                border: '2px solid #22c55eaa', borderRadius: 12,
                padding: '18px 8px', cursor: busy ? 'not-allowed' : 'pointer',
                color: '#fff', display: 'flex', flexDirection: 'column',
                alignItems: 'center', gap: 6,
                boxShadow: '0 0 16px #22c55e22',
              }}
            >
              <span style={{ fontSize: 32 }}>🔽</span>
              <span style={{ fontWeight: 900, fontSize: 15 }}>猜小</span>
              <span style={{ fontSize: 11, color: '#4ade80' }}>1・2・3</span>
              <span style={{ fontSize: 10, color: '#166534' }}>⚀ = JACKPOT!</span>
            </button>
          </div>

          <button
            className="btn-hover"
            disabled={busy}
            onClick={() => onAction(`tower_casino_${run.channelId}_cancel`)}
            style={{
              background: '#0f172a', border: '1px solid #1e293b', borderRadius: 10,
              padding: '8px 16px', cursor: busy ? 'not-allowed' : 'pointer',
              color: '#475569', fontSize: 12, fontWeight: 700, textAlign: 'center',
            }}
          >
            😅 反悔！退回籌碼
          </button>
        </div>
      )}
    </div>
  );
}
