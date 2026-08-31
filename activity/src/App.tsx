import { useEffect, useRef, useState, useCallback } from 'react';
import { DiscordSDK } from '@discord/embedded-app-sdk';
import { api } from './api';
import type { TowerRun } from './types';
import { BattleScene } from './components/BattleScene';
import { PathSelector } from './components/PathSelector';
import { GenericChoices } from './components/GenericChoices';
import { GameOver } from './components/GameOver';

const CLIENT_ID = import.meta.env.VITE_DISCORD_CLIENT_ID ?? '';

function FloorDots({ current, max }: { current: number; max: number }) {
  const dots = Array.from({ length: max }, (_, i) => i + 1);
  return (
    <div style={{ display: 'flex', gap: 3, flexWrap: 'wrap', justifyContent: 'center' }}>
      {dots.map(n => (
        <div key={n} style={{
          width: n % 10 === 0 ? 10 : 6,
          height: n % 10 === 0 ? 10 : 6,
          borderRadius: '50%',
          background: n <= current ? (n % 10 === 0 ? '#ef4444' : '#6366f1') : '#334155',
          transition: 'background 0.3s',
        }} />
      ))}
    </div>
  );
}

export default function App() {
  const sdkRef = useRef<DiscordSDK | null>(null);
  const [phase, setPhase] = useState<'loading' | 'idle' | 'game' | 'error'>('loading');
  const [run, setRun] = useState<TowerRun | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [userId, setUserId] = useState('');
  const [userName, setUserName] = useState('');
  const [channelId, setChannelId] = useState('');
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // ── Discord SDK 初始化 ─────────────────────────────────────────────
  useEffect(() => {
    const sdk = new DiscordSDK(CLIENT_ID);
    sdkRef.current = sdk;

    (async () => {
      try {
        await sdk.ready();

        const { code } = await sdk.commands.authorize({
          client_id: CLIENT_ID,
          response_type: 'code',
          state: '',
          prompt: 'none',
          scope: ['identify', 'guilds'],
        });

        // 換 access_token（走你的後端 /api/auth/token）
        const tokenRes = await fetch('/api/auth/token', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ code }),
        });
        const { access_token } = await tokenRes.json();
        await sdk.commands.authenticate({ access_token });

        const meRes = await fetch('https://discord.com/api/users/@me', {
          headers: { Authorization: `Bearer ${access_token}` },
        });
        const me = await meRes.json();

        setUserId(me.id);
        setUserName(me.global_name ?? me.username ?? '玩家');
        setChannelId(sdk.channelId ?? '');
        setPhase('idle');
      } catch (e) {
        setError(String(e));
        setPhase('error');
      }
    })();
  }, []);

  // ── Polling：每 3 秒抓一次遊戲狀態 ──────────────────────────────
  const startPolling = useCallback((cId: string) => {
    if (pollRef.current) clearInterval(pollRef.current);
    pollRef.current = setInterval(async () => {
      const res = await api.getRun(cId);
      if (res.ok && res.data) {
        setRun(res.data);
        if (res.data.state === 'Victory' || res.data.state === 'Defeated') {
          clearInterval(pollRef.current!);
        }
      }
    }, 3000);
  }, []);

  useEffect(() => () => { if (pollRef.current) clearInterval(pollRef.current); }, []);

  // ── 開始遊戲 ──────────────────────────────────────────────────────
  async function handleStart() {
    setBusy(true);
    const res = await api.startRun(channelId, userId, userName);
    if (res.ok && res.data) {
      setRun(res.data);
      setPhase('game');
      startPolling(channelId);
    } else {
      setError(res.error ?? '無法開始遊戲');
    }
    setBusy(false);
  }

  // ── 執行動作 ──────────────────────────────────────────────────────
  async function handleAction(customId: string) {
    if (!channelId || busy) return;
    setBusy(true);
    const res = await api.action({ channelId, customId });
    if (res.ok && res.data) setRun(res.data);
    else setError(res.error ?? '操作失敗');
    setBusy(false);
  }

  // ── 重新開始 ──────────────────────────────────────────────────────
  async function handleRestart() {
    setRun(null);
    setPhase('idle');
  }

  // ─────────────── Render ───────────────────────────────────────────
  if (phase === 'loading') {
    return (
      <div style={center}>
        <div style={{ fontSize: 40 }}>⚔️</div>
        <div style={{ color: '#94a3b8', marginTop: 12 }}>連接中…</div>
      </div>
    );
  }

  if (phase === 'error') {
    return (
      <div style={center}>
        <div style={{ color: '#ef4444', fontSize: 14 }}>❌ {error}</div>
      </div>
    );
  }

  if (phase === 'idle') {
    return (
      <div style={{ ...center, gap: 24, padding: 32 }}>
        <div style={{ fontSize: 56 }}>🗼</div>
        <div style={{ fontSize: 26, fontWeight: 900, color: '#fff' }}>寶可夢爬塔</div>
        <div style={{ color: '#94a3b8', fontSize: 14, textAlign: 'center', maxWidth: 280 }}>
          帶上你的寶可夢隊伍，挑戰 20 層的迷宮塔。<br />
          每層選擇路線，打倒敵人、收集遺物、升級技能！
        </div>
        <button
          onClick={handleStart}
          disabled={busy}
          style={{
            background: 'linear-gradient(135deg,#6366f1,#a855f7)',
            color: '#fff', border: 'none', borderRadius: 12,
            padding: '14px 40px', fontSize: 17, fontWeight: 700,
            cursor: busy ? 'not-allowed' : 'pointer',
          }}
        >
          {busy ? '啟動中…' : '開始挑戰！'}
        </button>
      </div>
    );
  }

  if (!run) return null;

  if (run.state === 'Victory' || run.state === 'Defeated') {
    return (
      <div style={shell}>
        <GameOver run={run} onRestart={handleRestart} />
      </div>
    );
  }

  return (
    <div style={shell}>
      {/* 頂部 Header */}
      <div style={{
        display: 'flex', justifyContent: 'space-between', alignItems: 'center',
        padding: '8px 12px', background: '#0f172a',
        borderBottom: '1px solid #1e293b', flexShrink: 0,
      }}>
        <span style={{ color: '#fff', fontWeight: 700, fontSize: 14 }}>🗼 寶可夢爬塔</span>
        <FloorDots current={run.currentFloor} max={run.maxFloor} />
        <span style={{ color: '#fbbf24', fontSize: 13 }}>💰{run.gold}</span>
      </div>

      {/* 主內容 */}
      <div style={{ flex: 1, overflow: 'auto', padding: 14 }}>
        {run.state === 'InBattle' && (
          <BattleScene run={run} onAction={handleAction} busy={busy} />
        )}
        {run.state === 'SelectingPath' && (
          <PathSelector run={run} onAction={handleAction} busy={busy} />
        )}
        {!['InBattle', 'SelectingPath', 'Victory', 'Defeated'].includes(run.state) && (
          <GenericChoices run={run} onAction={handleAction} busy={busy} />
        )}
      </div>

      {/* loading overlay */}
      {busy && (
        <div style={{
          position: 'absolute', inset: 0,
          background: 'rgba(0,0,0,0.4)',
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          fontSize: 24,
        }}>
          ⏳
        </div>
      )}
    </div>
  );
}

const center: React.CSSProperties = {
  display: 'flex', flexDirection: 'column',
  alignItems: 'center', justifyContent: 'center',
  height: '100vh', background: '#0f172a', color: '#fff',
};

const shell: React.CSSProperties = {
  display: 'flex', flexDirection: 'column',
  height: '100vh', background: '#0f172a', color: '#fff',
  position: 'relative', overflow: 'hidden',
};
