import { useState } from 'react';
import type { TowerRun } from '../types';
import { HpBar } from './HpBar';
import { TypeBadge } from './TypeBadge';
import { spriteUrl } from '../utils';

const DISMANTLE_FLAVOR = [
  (n: string) => `你把${n}召喚出來，然後笑著抱起了牠，看著牠純真的雙眼，你燦爛地拿出刀肢解了牠。`,
  (n: string) => `「${n}，你辛苦了。」你溫柔地說，手裡的刀隨即一閃，鮮血染紅了你的鞋。`,
  (n: string) => `你拉著${n}的爪子說「我們去一個特別的地方！」牠開心地跟著你走向角落……沒有再走出來。`,
  (n: string) => `${n}信任地仰望著你。你也低頭看著牠。然後你拿出了工具——快樂的事情即將發生。`,
  (n: string) => `今天的${n}特別乖。你蹲下來摸了摸牠的頭，說：「謝謝你的配合。」鮮血在地板上靜靜暈開。`,
  (n: string) => `「${n}，你知道我很喜歡你嗎？」你一邊微笑說著，一邊把刀靠近牠的頸側。牠搖了搖尾巴。`,
  (n: string) => `${n}還在回頭對你笑的時候，一切就都結束了。你低頭整理了一下衣袖上的污漬。`,
  (n: string) => `你遞給${n}一塊餅乾，等牠吃完後說「好孩子」，然後——效率就是一切。`,
  (n: string) => `「${n}，閉上眼睛。我要給你一個驚喜。」牠乖乖照做了。驚喜確實如期而至。`,
];

const CURSE_NAMES: Record<string, { name: string; emoji: string; desc: string }> = {
  curse_half_pp:     { name: '詛咒之語',   emoji: '💀', desc: '全部技能 MaxPP 減半' },
  curse_gold_tax:    { name: '貪婪詛咒',   emoji: '🪙', desc: '每層結束扣除 10💰' },
  curse_slow:        { name: '重力詛咒',   emoji: '🔩', desc: '全隊速度 -40%' },
  curse_weak_atk:    { name: '腐蝕之力',   emoji: '⚗️', desc: '全隊攻擊力 -25%' },
  curse_bleed:       { name: '流血詛咒',   emoji: '🩸', desc: '每回合扣 MaxHP×5%' },
  curse_fragile:     { name: '玻璃心',     emoji: '💔', desc: '全隊防禦力 -30%' },
  curse_blind:       { name: '蒙眼詛咒',   emoji: '👁️‍🗨️', desc: '無法看到敵人下一招' },
  curse_expensive:   { name: '奸商詛咒',   emoji: '🏪', desc: '商店所有價格×1.5' },
  curse_exp_drain:   { name: '知識吸取',   emoji: '📖', desc: '獲得 EXP 減少 50%' },
  curse_no_catch:    { name: '鐵籠詛咒',   emoji: '🔒', desc: '無法捕獲任何 Pokemon' },
  curse_hp_cap:      { name: '生命封印',   emoji: '❤️‍🔥', desc: '全隊最大 HP -20%' },
  curse_move_random: { name: '混亂咒語',   emoji: '🌀', desc: '每回合 20% 機率隨機使用技能' },
  curse_forget:      { name: '遺忘詛咒',   emoji: '🧠', desc: '每過一層隨機忘掉一個技能' },
  curse_weaken:      { name: '虛弱加身',   emoji: '⚠️', desc: '已強化過的技能威力減半' },
  curse_gold_drain:  { name: '黃金枷鎖',   emoji: '🔗', desc: '擊倒敵人時扣現有金幣10%' },
  curse_mirror:      { name: '角色互換',   emoji: '🔀', desc: '每奇數回合隨機使用技能' },
  curse_fragile2:    { name: '紙糊護甲',   emoji: '📄', desc: '每次受傷後防禦永久-3' },
  curse_hungry:      { name: '飢餓詛咒',   emoji: '🍽️', desc: '每回合技能PP額外消耗1點' },
  curse_unlucky:     { name: '厄運纏身',   emoji: '🎭', desc: '所有暴擊/捕獲等機率減少30%' },
  curse_decay:       { name: '腐敗詛咒',   emoji: '🦠', desc: '神器攻擊類加成減半' },
  curse_paranoia:    { name: '妄想症',     emoji: '👻', desc: '無法使用商店' },
  curse_silence:     { name: '沉默詛咒',   emoji: '🔇', desc: '威力最高的技能PP上限變為1' },
  curse_brittle:     { name: '易碎軀體',   emoji: '🪨', desc: '受到的所有傷害增加35%' },
  curse_backfire:    { name: '反噬詛咒',   emoji: '💢', desc: '攻擊後20%機率受到自身25%反傷' },
  curse_one_move:    { name: '殘缺記憶',   emoji: '🧠', desc: '每回合強制只能使用第一個技能' },
};

const BALL_LABELS: Record<string, { label: string; emoji: string }> = {
  normal: { label: '普通球', emoji: '⚪' },
  super:  { label: '超級球', emoji: '🔵' },
  ultra:  { label: '高級球', emoji: '🟡' },
  master: { label: '大師球', emoji: '🟣' },
};

const RELIC_NAMES: Record<string, { name: string; emoji: string; desc: string }> = {
  relic_shield:      { name: '守護之盾',   emoji: '🛡️',   desc: '每場戰鬥首次受到的攻擊無效化' },
  relic_hourglass:   { name: '時光沙漏',   emoji: '⏳',   desc: '每層進入時回復 5% HP' },
  relic_time_warp:   { name: '時空扭曲',   emoji: '🌀',   desc: '每場戰鬥開始時回復 3 PP' },
  relic_atk_up:      { name: '純純的數值', emoji: '💪',   desc: '全隊攻擊力+20%' },
  relic_def_up:      { name: '硬啦',       emoji: '🛡️',  desc: '全隊防禦力+20%' },
  relic_hp_up:       { name: '坦克引擎',   emoji: '💎',   desc: '全隊最大HP+25%' },
  relic_move_pow:    { name: '全ap跟你爆搂', emoji: '👁️', desc: '全部技能威力+20' },
  relic_move_pp:     { name: '魔力水晶',   emoji: '💧',   desc: '全部技能MaxPP+5並回滿' },
  relic_all_stats:   { name: 'X項之力',    emoji: '🎺',   desc: '全隊所有能力+15%' },
  relic_gold:        { name: '我就愛錢',   emoji: '💰',   desc: '立即獲得80金幣' },
  relic_exp:         { name: '爆考研究所', emoji: '📚',   desc: '立即獲得大量EXP' },
  relic_lifesteal:   { name: '嗜血者',     emoji: '🧛',   desc: '攻擊回復傷害的20%HP' },
  relic_thorns:      { name: '你是甲我反甲', emoji: '🌵', desc: '受傷時反彈傷害的25%' },
  relic_crit:        { name: '賭你不敢',   emoji: '💥',   desc: '攻擊15%機率造成雙倍傷害' },
  relic_poison:      { name: '毒牙',       emoji: '☠️',   desc: '每次攻擊額外造成15固定傷害' },
  relic_no_pp:       { name: '永動機',     emoji: '⚙️',   desc: '使用技能25%機率不消耗PP' },
  relic_enrage:      { name: '老子跟你爆搂', emoji: '😤', desc: 'HP低於30%時傷害×1.6' },
  relic_regen:       { name: '再生果實',   emoji: '🍎',   desc: '每回合回復MaxHP×3%' },
  relic_boss_dmg:    { name: '專打強者',   emoji: '🪞',   desc: '對Boss造成的傷害+50%' },
  relic_fullhp:      { name: '滿血的我，是最強的', emoji: '👑', desc: 'HP全滿時傷害+30%' },
  relic_amplify:     { name: '發瘋啦',     emoji: '🔍',   desc: '所有攻擊傷害+30%' },
  relic_blood:       { name: '血祭刃',     emoji: '🩸',   desc: '每回合自損HP但傷害×1.3' },
  relic_avenge:      { name: '復仇碎片',   emoji: '💔',   desc: '受傷累積3次後下次攻擊雙倍' },
  relic_kill_pp:     { name: '奪命符文',   emoji: '⚡',   desc: '擊倒敵人後所有技能回復3PP' },
  relic_phoenix:     { name: '不死鳥羽',   emoji: '🪶',   desc: '一次致命攻擊後以1HP存活' },
  relic_last_stand:  { name: '最後防線',   emoji: '🏴',   desc: 'HP低於20%時受傷減少50%' },
  relic_hunter:      { name: '獵人徽章',   emoji: '🎯',   desc: '捕獲率+30%' },
  relic_berserk:     { name: '背水一戰',   emoji: '🌊',   desc: 'HP低於50%時每回合技能回復2PP' },
  relic_no_def:      { name: '混沌之眼',   emoji: '🌀',   desc: '攻擊有20%機率完全無視防禦' },
  relic_will:        { name: '意志結晶',   emoji: '✨',   desc: '全技能PP歸零時自動回復一次' },
  relic_chain:       { name: '連鎖爆發',   emoji: '⛓️',   desc: '每擊倒一個敵人累積+5%傷害' },
  relic_executioner: { name: '劊子手',     emoji: '🪓',   desc: '敵人HP低於25%時傷害×2' },
  relic_mirror_coat: { name: '鏡面反射',   emoji: '🪞',   desc: '每場戰鬥有一次完全反射傷害' },
  relic_parasite:    { name: '寄生種子',   emoji: '🌱',   desc: '每擊倒一隻敵人永久+5最大HP' },
  relic_feast:       { name: '盛宴',       emoji: '🍖',   desc: '每場戰鬥勝利後回復50HP' },
  relic_double_edge: { name: '捨身衝撞',   emoji: '💨',   desc: '攻擊傷害+40%但每次攻擊自損15%' },
  relic_lucky_charm: { name: '幸運符',     emoji: '🍀',   desc: '所有隨機判定機率+15%' },
  relic_exp_boost:   { name: '學習加速器', emoji: '🎓',   desc: '每場戰鬥獲得的EXP×1.5' },
  relic_gold_mine:   { name: '金礦脈',     emoji: '⛏️',   desc: '每場戰鬥勝利額外獲得20💰' },
  relic_berserker_r: { name: '狂暴之心',   emoji: '❤️‍🔥', desc: 'HP低於50%時傷害+40%' },
  relic_swift:       { name: '迅捷之羽',   emoji: '🪽',   desc: '速度+30%' },
  relic_scholar:     { name: '學者之冠',   emoji: '🎩',   desc: '每升一級額外獲得所有技能+5PP' },
  relic_comeback:    { name: '逆轉勝負',   emoji: '🔄',   desc: 'HP低於10%時下一次攻擊傷害×3' },
  relic_shared_pain: { name: '共苦盟約',   emoji: '🤝',   desc: '受到傷害時對敵人反彈30%傷害' },
  relic_revenge:     { name: '復仇之刃',   emoji: '🗡️',   desc: '每次受傷蓄積，下次攻擊釋放蓄積量×50%額外傷害' },
  relic_multi_hit:   { name: '連擊衝擊',   emoji: '👊',   desc: '攻擊有30%機率再追加一擊（60%傷害）' },
  relic_gold_power:  { name: '財力轉換',   emoji: '💸',   desc: '每擁有100金幣增加8%攻擊傷害（上限+40%）' },
};

type Tab = 'team' | 'reserve' | 'items';

interface Props {
  run: TowerRun;
  isOpen: boolean;
  onClose: () => void;
  onAction?: (customId: string) => void;
}

export function Inventory({ run, isOpen, onClose, onAction }: Props) {
  const [tab, setTab] = useState<Tab>('team');
  // 肢解動畫狀態
  const [dismantleAnim, setDismantleAnim] = useState<{ idx: number; name: string; text: string } | null>(null);
  // 換位選擇：選了哪個備戰區 Pokémon，等玩家點隊伍成員
  const [swapReserveIdx, setSwapReserveIdx] = useState<number | null>(null);

  if (!isOpen) return null;

  const balls = run.balls ?? {};
  const hasBalls = Object.values(balls).some(v => v > 0);

  return (
    <div style={{
      position: 'absolute', inset: 0,
      background: 'rgba(0,0,0,0.7)',
      backdropFilter: 'blur(3px)',
      zIndex: 100,
      display: 'flex', alignItems: 'flex-end',
    }} onClick={onClose}>
      <div
        className="anim-fade-in"
        style={{
          width: '100%',
          maxHeight: '80vh',
          background: '#0f172a',
          borderRadius: '16px 16px 0 0',
          border: '1px solid #1e293b',
          borderBottom: 'none',
          display: 'flex', flexDirection: 'column',
          overflow: 'hidden',
        }}
        onClick={e => e.stopPropagation()}
      >
        {/* Header */}
        <div style={{
          padding: '14px 16px 0',
          borderBottom: '1px solid #1e293b',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
            <div style={{ fontWeight: 900, fontSize: 15, color: '#fff' }}>🎒 背包</div>
            <button
              onClick={onClose}
              style={{
                background: 'none', border: 'none', color: '#475569',
                fontSize: 18, cursor: 'pointer', lineHeight: 1,
              }}
            >✕</button>
          </div>
          {/* Tabs */}
          <div style={{ display: 'flex', gap: 2 }}>
            {(['team', 'reserve', 'items'] as Tab[]).map(t => {
              const reserveCount = run.reserve?.length ?? 0;
              const label = t === 'team' ? '👥 隊伍'
                : t === 'reserve' ? `🗄️ 備戰區${reserveCount > 0 ? ` (${reserveCount})` : ''}`
                : '🎯 道具';
              return (
                <button
                  key={t}
                  onClick={() => { setTab(t); setSwapReserveIdx(null); }}
                  style={{
                    background: tab === t ? '#1e293b' : 'none',
                    border: 'none',
                    borderRadius: '8px 8px 0 0',
                    padding: '8px 12px',
                    color: tab === t ? '#fff' : '#475569',
                    fontWeight: tab === t ? 700 : 400,
                    fontSize: 12,
                    cursor: 'pointer',
                  }}
                >{label}</button>
              );
            })}
          </div>
        </div>

        {/* Content */}
        <div style={{ flex: 1, overflowY: 'auto', padding: '12px 14px' }}>
          {tab === 'team' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              {run.team.map((p, i) => (
                <div key={i} style={{
                  background: '#07090f',
                  borderRadius: 10,
                  padding: '10px 12px',
                  border: `1px solid ${i === run.activeIndex ? '#6366f1' : '#1e293b'}`,
                  display: 'flex', gap: 10, alignItems: 'flex-start',
                }}>
                  <img
                    src={p.isShiny ? spriteUrl(p.pokeId, 'shiny') : spriteUrl(p.pokeId)}
                    alt={p.name}
                    style={{ width: 52, height: 52, imageRendering: 'pixelated', flexShrink: 0 }}
                  />
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 4 }}>
                      <span style={{ fontWeight: 900, fontSize: 13, color: '#fff' }}>
                        {p.displayName}
                      </span>
                      {p.isShiny && <span>✨</span>}
                      {i === run.activeIndex && (
                        <span style={{ fontSize: 9, color: '#6366f1', fontWeight: 700 }}>出戰中</span>
                      )}
                    </div>
                    <div style={{ display: 'flex', gap: 3, marginBottom: 5 }}>
                      {p.types.map(t => <TypeBadge key={t} type={t} />)}
                    </div>
                    <HpBar current={p.currentHP} max={p.maxHP} label="HP" />
                    {/* Moves */}
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4, marginTop: 6 }}>
                      {p.moves.map((m, mi) => (
                        <span key={mi} style={{
                          fontSize: 10, background: '#1e293b',
                          borderRadius: 4, padding: '2px 6px',
                          color: '#94a3b8',
                        }}>
                          {m.emoji} {m.name} <span style={{ color: '#475569' }}>({m.currentPP}/{m.maxPP})</span>
                        </span>
                      ))}
                    </div>
                    {/* Set as lead — only outside battle and not already first */}
                    {onAction && ['SelectingPath', 'Shopping', 'Resting', 'SelectingPowerUpgrade', 'SelectingRelic', 'SelectingCursedRelic'].includes(run.state) && i !== 0 && p.currentHP > 0 && (
                      <button
                        className="btn-hover"
                        onClick={() => { onAction(`tower_setlead_${run.channelId}_${i}`); onClose(); }}
                        style={{
                          marginTop: 8, background: 'linear-gradient(135deg, #1e1b4b, #312e81)',
                          border: '1px solid #6366f155', borderRadius: 7,
                          padding: '5px 12px', color: '#a5b4fc',
                          fontSize: 11, fontWeight: 700, cursor: 'pointer',
                          display: 'flex', alignItems: 'center', gap: 5,
                        }}
                      >
                        ⭐ 設為首發
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}

          {tab === 'reserve' && (() => {
            const reserve = run.reserve ?? [];
            const canAct = ['SelectingPath', 'Shopping', 'Resting', 'SelectingPowerUpgrade', 'SelectingRelic', 'SelectingCursedRelic'].includes(run.state);
            return (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                {reserve.length === 0 && (
                  <div style={{ color: '#334155', fontSize: 13, textAlign: 'center', paddingTop: 20 }}>
                    備戰區是空的<br /><span style={{ fontSize: 11 }}>捕捉到第 4 隻以上的寶可夢會自動放入這裡</span>
                  </div>
                )}

                {/* 換位模式提示 */}
                {swapReserveIdx !== null && (
                  <div style={{
                    background: '#0f2040', border: '1px solid #3b82f6',
                    borderRadius: 8, padding: '8px 12px', fontSize: 12, color: '#93c5fd',
                    display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                  }}>
                    <span>選擇隊伍中要換出的成員 ↓</span>
                    <button onClick={() => setSwapReserveIdx(null)} style={{ background: 'none', border: 'none', color: '#64748b', cursor: 'pointer', fontSize: 14 }}>✕</button>
                  </div>
                )}

                {/* 換位時顯示隊伍選擇 */}
                {swapReserveIdx !== null && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                    {run.team.map((p, pi) => (
                      <button
                        key={pi}
                        className="btn-hover"
                        onClick={() => {
                          onAction?.(`tower_reserve_swap_${run.channelId}_${swapReserveIdx}_${pi}`);
                          setSwapReserveIdx(null);
                          onClose();
                        }}
                        style={{
                          background: pi === run.activeIndex ? '#1e1b4b' : '#07090f',
                          border: `1px solid ${pi === run.activeIndex ? '#6366f1' : '#334155'}`,
                          borderRadius: 8, padding: '8px 12px',
                          color: '#e2e8f0', cursor: 'pointer', textAlign: 'left',
                          display: 'flex', alignItems: 'center', gap: 8,
                        }}
                      >
                        <img src={spriteUrl(p.pokeId)} alt={p.name} style={{ width: 36, height: 36, imageRendering: 'pixelated' }} />
                        <div>
                          <div style={{ fontWeight: 700, fontSize: 12 }}>{p.displayName} {pi === run.activeIndex && <span style={{ color: '#818cf8', fontSize: 10 }}>出戰中</span>}</div>
                          <div style={{ fontSize: 10, color: '#64748b' }}>HP {p.currentHP}/{p.maxHP}</div>
                        </div>
                      </button>
                    ))}
                  </div>
                )}

                {/* 備戰區列表 */}
                {swapReserveIdx === null && reserve.map((p, ri) => (
                  <div key={ri} style={{
                    background: '#07090f', borderRadius: 10, padding: '10px 12px',
                    border: '1px solid #1e293b',
                    display: 'flex', gap: 10, alignItems: 'flex-start',
                  }}>
                    <img
                      src={p.isShiny ? spriteUrl(p.pokeId, 'shiny') : spriteUrl(p.pokeId)}
                      alt={p.name}
                      style={{ width: 52, height: 52, imageRendering: 'pixelated', flexShrink: 0 }}
                    />
                    <div style={{ flex: 1, minWidth: 0 }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 4 }}>
                        <span style={{ fontWeight: 900, fontSize: 13, color: '#fff' }}>{p.displayName}</span>
                        {p.isShiny && <span>✨</span>}
                      </div>
                      <div style={{ display: 'flex', gap: 3, marginBottom: 5 }}>
                        {p.types.map(t => <TypeBadge key={t} type={t} />)}
                      </div>
                      <HpBar current={p.currentHP} max={p.maxHP} label="HP" />
                      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4, marginTop: 6 }}>
                        {p.moves.map((m, mi) => (
                          <span key={mi} style={{ fontSize: 10, background: '#1e293b', borderRadius: 4, padding: '2px 6px', color: '#94a3b8' }}>
                            {m.emoji} {m.name}
                          </span>
                        ))}
                      </div>
                      {canAct && (
                        <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
                          {/* 換入 */}
                          <button
                            className="btn-hover"
                            onClick={() => setSwapReserveIdx(ri)}
                            style={{
                              background: 'linear-gradient(135deg, #1e1b4b, #312e81)',
                              border: '1px solid #6366f155', borderRadius: 7,
                              padding: '5px 12px', color: '#a5b4fc',
                              fontSize: 11, fontWeight: 700, cursor: 'pointer',
                            }}
                          >🔄 換入隊伍</button>
                          {/* 肢解 */}
                          <button
                            className="btn-hover"
                            onClick={() => {
                              const flavorFn = DISMANTLE_FLAVOR[Math.floor(Math.random() * DISMANTLE_FLAVOR.length)];
                              setDismantleAnim({ idx: ri, name: p.displayName, text: flavorFn(p.displayName) });
                              setTimeout(() => {
                                onAction?.(`tower_reserve_dismantle_${run.channelId}_${ri}`);
                                setDismantleAnim(null);
                              }, 2800);
                            }}
                            style={{
                              background: 'linear-gradient(135deg, #3d0000, #7f1d1d)',
                              border: '1px solid #ef444455', borderRadius: 7,
                              padding: '5px 12px', color: '#fca5a5',
                              fontSize: 11, fontWeight: 700, cursor: 'pointer',
                            }}
                          >🔪 肢解</button>
                        </div>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            );
          })()}

          {tab === 'items' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
              {/* Balls */}
              <div>
                <div style={{ fontSize: 11, color: '#475569', fontWeight: 700, marginBottom: 6, letterSpacing: '0.05em' }}>精靈球</div>
                {!hasBalls ? (
                  <div style={{ color: '#334155', fontSize: 12 }}>無</div>
                ) : (
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
                    {Object.entries(balls).filter(([, v]) => v > 0).map(([k, v]) => {
                      const info = BALL_LABELS[k] ?? { label: k, emoji: '⚪' };
                      return (
                        <div key={k} style={{
                          background: '#07090f', borderRadius: 8,
                          border: '1px solid #1e293b',
                          padding: '8px 12px',
                          display: 'flex', alignItems: 'center', gap: 6,
                        }}>
                          <span style={{ fontSize: 18 }}>{info.emoji}</span>
                          <div>
                            <div style={{ fontSize: 11, color: '#94a3b8' }}>{info.label}</div>
                            <div style={{ fontSize: 14, fontWeight: 900, color: '#fff' }}>×{v}</div>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>

              {/* Relics */}
              <div>
                <div style={{ fontSize: 11, color: '#475569', fontWeight: 700, marginBottom: 6, letterSpacing: '0.05em' }}>遺物</div>
                {run.relicIds.length === 0 ? (
                  <div style={{ color: '#334155', fontSize: 12 }}>無遺物</div>
                ) : (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                    {run.relicIds.map(id => {
                      const info = RELIC_NAMES[id];
                      return (
                        <div key={id} style={{
                          background: '#07090f', borderRadius: 8,
                          border: '1px solid #2d1b4e',
                          padding: '8px 12px',
                          display: 'flex', alignItems: 'center', gap: 8,
                        }}>
                          <span style={{ fontSize: 22 }}>{info?.emoji ?? '🔮'}</span>
                          <div>
                            <div style={{ fontSize: 12, fontWeight: 700, color: '#c084fc' }}>
                              {info?.name ?? id}
                            </div>
                            <div style={{ fontSize: 11, color: '#64748b' }}>
                              {info?.desc ?? id}
                            </div>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>

              {/* Cursed relics */}
              {run.cursedRelicIds.length > 0 && (
                <div>
                  <div style={{ fontSize: 11, color: '#ef4444', fontWeight: 700, marginBottom: 6, letterSpacing: '0.05em' }}>詛咒遺物</div>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                    {run.cursedRelicIds.map(id => {
                      const ci = CURSE_NAMES[id];
                      return (
                        <div key={id} style={{
                          background: '#1a0000', borderRadius: 8,
                          border: '1px solid #7f1d1d',
                          padding: '8px 12px',
                          display: 'flex', alignItems: 'center', gap: 8,
                        }}>
                          <span style={{ fontSize: 22 }}>{ci?.emoji ?? '💀'}</span>
                          <div>
                            <div style={{ fontSize: 12, fontWeight: 700, color: '#fca5a5' }}>{ci?.name ?? id}</div>
                            <div style={{ fontSize: 11, color: '#7f1d1d' }}>{ci?.desc ?? id}</div>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* 🔪 肢解動畫 overlay */}
      {dismantleAnim && (
        <div style={{
          position: 'absolute', inset: 0, zIndex: 200,
          background: 'rgba(0,0,0,0.88)',
          display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
          gap: 18, padding: 28,
          animation: 'fadeIn 0.2s ease both',
        }}>
          {/* 血花效果 */}
          <div style={{ position: 'relative', width: 90, height: 90 }}>
            {[...Array(8)].map((_, i) => (
              <div key={i} style={{
                position: 'absolute', top: '50%', left: '50%',
                width: 8 + (i % 3) * 4, height: 8 + (i % 3) * 4,
                borderRadius: '50%',
                background: `hsl(${355 + i * 5}, 85%, ${35 + i * 3}%)`,
                transform: `translate(-50%,-50%) rotate(${i * 45}deg) translateY(${-22 - i * 4}px)`,
                animation: `impactBurst ${0.4 + i * 0.06}s ${i * 0.05}s ease-out both`,
              }} />
            ))}
            <div style={{
              position: 'absolute', top: '50%', left: '50%',
              transform: 'translate(-50%,-50%)',
              fontSize: 44, lineHeight: 1,
              animation: 'flash 0.3s 3',
            }}>🔪</div>
          </div>
          {/* 獵奇文字 */}
          <div style={{
            fontSize: 13, color: '#fca5a5', lineHeight: 1.8,
            textAlign: 'center', maxWidth: 280,
            textShadow: '0 0 12px #ef444488',
          }}>
            {dismantleAnim.text}
          </div>
          <div style={{
            fontSize: 11, color: '#6b7280', fontStyle: 'italic',
          }}>肢解中…</div>
        </div>
      )}
    </div>
  );
}
