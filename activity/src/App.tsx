import { useEffect, useRef, useState, useCallback } from 'react';
import { api } from './api';
import type { TowerRun } from './types';
import { BattleScene } from './components/BattleScene';
import { PathSelector } from './components/PathSelector';
import { GenericChoices } from './components/GenericChoices';
import { GameOver } from './components/GameOver';

const CLIENT_ID = import.meta.env.VITE_DISCORD_CLIENT_ID ?? '';
const REDIRECT_URI = import.meta.env.VITE_REDIRECT_URI ?? window.location.origin;

// ── Discord OAuth2 helpers ─────────────────────────────────────────
function buildAuthUrl(channelId: string) {
  const params = new URLSearchParams({
    client_id:     CLIENT_ID,
    redirect_uri:  REDIRECT_URI,
    response_type: 'code',
    scope:         'identify',
    state:         channelId,   // 把 channelId 藏在 state，callback 時用
  });
  return `https://discord.com/oauth2/authorize?${params}`;
}

async function exchangeCode(code: string): Promise<string> {
  const res = await fetch('/api/auth/token', {
    method:  'POST',
    headers: { 'Content-Type': 'application/json' },
    body:    JSON.stringify({ code, redirectUri: REDIRECT_URI }),
  });
  if (!res.ok) throw new Error(await res.text());
  const { access_token } = await res.json();
  return access_token as string;
}

async function fetchDiscordUser(token: string) {
  const res = await fetch('https://discord.com/api/users/@me', {
    headers: { Authorization: `Bearer ${token}` },
  });
  return res.json();
}

// ── Floor dots ────────────────────────────────────────────────────
function FloorDots({ current, max }: { current: number; max: number }) {
  return (
    <div style={{ display: 'flex', gap: 3, flexWrap: 'wrap', justifyContent: 'center' }}>
      {Array.from({ length: max }, (_, i) => i + 1).map(n => (
        <div key={n} style={{
          width: n % 10 === 0 ? 10 : 6, height: n % 10 === 0 ? 10 : 6,
          borderRadius: '50%',
          background: n <= current ? (n % 10 === 0 ? '#ef4444' : '#6366f1') : '#334155',
          transition: 'background 0.3s',
        }} />
      ))}
    </div>
  );
}

// ── Main App ──────────────────────────────────────────────────────
type Phase = 'loading' | 'need-auth' | 'game' | 'no-run' | 'error';

export default function App() {
  const [phase, setPhase]         = useState<Phase>('loading');
  const [run, setRun]             = useState<TowerRun | null>(null);
  const [busy, setBusy]           = useState(false);
  const [error, setError]         = useState('');
  const [channelId, setChannelId] = useState('');
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const startPolling = useCallback((cId: string) => {
    if (pollRef.current) clearInterval(pollRef.current);
    pollRef.current = setInterval(async () => {
      const res = await api.getRun(cId);
      if (res.ok && res.data) {
        setRun(res.data);
        if (res.data.state === 'Victory' || res.data.state === 'Defeated')
          clearInterval(pollRef.current!);
      }
    }, 3000);
  }, []);

  useEffect(() => () => { if (pollRef.current) clearInterval(pollRef.current); }, []);

  // ── 初始化：解析 URL params ────────────────────────────────────
  useEffect(() => {
    (async () => {
      const params = new URLSearchParams(window.location.search);
      const code    = params.get('code');
      const state   = params.get('state');   // channelId
      const channel = params.get('channel'); // 直接帶 channelId

      // ① OAuth callback：?code=xxx&state=channelId
      if (code && state) {
        // 清掉 URL 上的 code（不讓人看到）
        window.history.replaceState({}, '', `?channel=${state}`);
        try {
          const token = await exchangeCode(code);
          const user  = await fetchDiscordUser(token);
          setChannelId(state);
          // 直接拉遊戲狀態
          const res = await api.getRun(state);
          if (res.ok && res.data) {
            setRun(res.data);
            setPhase('game');
            startPolling(state);
          } else {
            setPhase('no-run');
          }
        } catch (e) {
          setError(String(e));
          setPhase('error');
        }
        return;
      }

      // ② 直接帶 channelId（從 bot 連結來）：?channel=xxx
      if (channel) {
        setChannelId(channel);
        const res = await api.getRun(channel);
        if (res.ok && res.data) {
          // 有 run → 要求登入
          setRun(res.data);
          setPhase('need-auth');
        } else {
          setPhase('no-run');
        }
        return;
      }

      // ③ 沒帶任何參數
      setPhase('no-run');
    })();
  }, [startPolling]);

  // ── 執行動作 ──────────────────────────────────────────────────
  async function handleAction(customId: string) {
    if (!channelId || busy) return;
    setBusy(true);
    const res = await api.action({ channelId, customId });
    if (res.ok && res.data) setRun(res.data);
    else setError(res.error ?? '操作失敗');
    setBusy(false);
  }

  // ─────────────── Render ───────────────────────────────────────
  if (phase === 'loading') return (
    <div style={center}><div style={{ fontSize: 40 }}>⚔️</div><div style={{ color:'#94a3b8', marginTop:12 }}>載入中…</div></div>
  );

  if (phase === 'error') return (
    <div style={center}><div style={{ color:'#ef4444', fontSize:14 }}>❌ {error}</div></div>
  );

  if (phase === 'no-run') return (
    <div style={{ ...center, gap:20, padding:32, textAlign:'center' }}>
      <div style={{ fontSize:56 }}>🗼</div>
      <div style={{ fontSize:24, fontWeight:900, color:'#fff' }}>Pokemon 爬塔</div>
      <div style={{ color:'#94a3b8', fontSize:14, maxWidth:280 }}>
        請先在 Discord 頻道輸入 <code style={{ background:'#1e293b', padding:'2px 6px', borderRadius:4 }}>/pokemon爬塔</code><br/>
        選好隊伍後，點 Bot 給的連結進入遊戲。
      </div>
    </div>
  );

  if (phase === 'need-auth') return (
    <div style={{ ...center, gap:20, padding:32, textAlign:'center' }}>
      <div style={{ fontSize:56 }}>🗼</div>
      <div style={{ fontSize:22, fontWeight:900, color:'#fff' }}>Pokemon 爬塔</div>
      {run && (
        <div style={{ color:'#94a3b8', fontSize:13 }}>
          {run.playerName} 的挑戰 · 第 {run.currentFloor}/{run.maxFloor} 層
        </div>
      )}
      <a
        href={buildAuthUrl(channelId)}
        style={{
          background: 'linear-gradient(135deg,#5865f2,#7289da)',
          color:'#fff', textDecoration:'none', borderRadius:12,
          padding:'14px 32px', fontSize:16, fontWeight:700,
          display:'inline-block',
        }}
      >
        🔑 用 Discord 登入後進入遊戲
      </a>
    </div>
  );

  if (!run) return null;

  if (run.state === 'Victory' || run.state === 'Defeated') return (
    <div style={shell}>
      <GameOver run={run} onRestart={() => { setRun(null); setPhase('no-run'); }} />
    </div>
  );

  return (
    <div style={shell}>
      {/* Header */}
      <div style={{
        display:'flex', justifyContent:'space-between', alignItems:'center',
        padding:'8px 12px', background:'#0f172a', borderBottom:'1px solid #1e293b', flexShrink:0,
      }}>
        <span style={{ color:'#fff', fontWeight:700, fontSize:14 }}>🗼 Pokemon 爬塔</span>
        <FloorDots current={run.currentFloor} max={run.maxFloor} />
        <span style={{ color:'#fbbf24', fontSize:13 }}>💰{run.gold}</span>
      </div>

      {/* 主內容 */}
      <div style={{ flex:1, overflow:'auto', padding:14 }}>
        {run.state === 'InBattle' && <BattleScene run={run} onAction={handleAction} busy={busy} />}
        {run.state === 'SelectingPath' && <PathSelector run={run} onAction={handleAction} busy={busy} />}
        {!['InBattle','SelectingPath','Victory','Defeated'].includes(run.state) && (
          <GenericChoices run={run} onAction={handleAction} busy={busy} />
        )}
      </div>

      {busy && (
        <div style={{ position:'absolute', inset:0, background:'rgba(0,0,0,0.4)', display:'flex', alignItems:'center', justifyContent:'center', fontSize:24 }}>
          ⏳
        </div>
      )}
    </div>
  );
}

const center: React.CSSProperties = {
  display:'flex', flexDirection:'column', alignItems:'center', justifyContent:'center',
  height:'100vh', background:'#0f172a', color:'#fff',
};
const shell: React.CSSProperties = {
  display:'flex', flexDirection:'column', height:'100vh',
  background:'#0f172a', color:'#fff', position:'relative', overflow:'hidden',
};
