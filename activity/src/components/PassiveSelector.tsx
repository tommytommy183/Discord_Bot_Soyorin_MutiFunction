import type { PassiveOption } from '../types';

interface Props {
  passives: PassiveOption[];
  onSelect: (passiveId: string) => void;
  busy: boolean;
}

export function PassiveSelector({ passives, onSelect, busy }: Props) {
  if (passives.length === 0) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', height: '100%', gap: 16, padding: 24 }}>
        <div style={{ fontSize: 32, animation: 'pulse 1.5s ease-in-out infinite' }}>✨</div>
        <div style={{ color: '#94a3b8', fontSize: 13 }}>載入職業中…</div>
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      {/* Header */}
      <div style={{
        padding: '16px 20px 12px',
        background: 'linear-gradient(180deg, #1a1f35 0%, #0a0e1a 100%)',
        borderBottom: '1px solid #1e293b', flexShrink: 0,
      }}>
        <div style={{
          fontFamily: "'Press Start 2P', monospace",
          fontSize: 10, color: '#a855f7', letterSpacing: '0.1em', marginBottom: 6,
        }}>CHOOSE CLASS</div>
        <div style={{ fontSize: 16, fontWeight: 900, color: '#fff' }}>🌟 選擇職業（三選一）</div>
        <div style={{ fontSize: 12, color: '#475569', marginTop: 4 }}>踏入爬塔前，選擇本次的被動技能！</div>
      </div>

      {/* Passive cards */}
      <div style={{ flex: 1, overflow: 'auto', padding: '16px', display: 'flex', flexDirection: 'column', gap: 12 }}>
        {passives.map((p, i) => (
          <button
            key={p.id}
            className="btn-hover anim-fade-in"
            disabled={busy}
            onClick={() => !busy && onSelect(p.id)}
            style={{
              background: 'linear-gradient(135deg, #1a1f35 0%, #0f172a 100%)',
              border: '1px solid #334155',
              borderRadius: 14,
              padding: '16px 18px',
              cursor: busy ? 'not-allowed' : 'pointer',
              textAlign: 'left',
              display: 'flex', alignItems: 'flex-start', gap: 14,
              transition: 'border-color 0.2s, box-shadow 0.2s',
              opacity: busy ? 0.6 : 1,
            }}
            onMouseEnter={e => {
              if (!busy) {
                (e.currentTarget as HTMLButtonElement).style.borderColor = '#a855f7';
                (e.currentTarget as HTMLButtonElement).style.boxShadow = '0 4px 20px #a855f733';
              }
            }}
            onMouseLeave={e => {
              (e.currentTarget as HTMLButtonElement).style.borderColor = '#334155';
              (e.currentTarget as HTMLButtonElement).style.boxShadow = '';
            }}
          >
            <div style={{
              fontSize: 36, lineHeight: 1, flexShrink: 0,
              filter: 'drop-shadow(0 2px 8px rgba(168,85,247,0.4))',
            }}>
              {p.emoji}
            </div>
            <div style={{ flex: 1 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6 }}>
                <span style={{
                  fontFamily: "'Press Start 2P', monospace",
                  fontSize: 8, color: '#6366f1', letterSpacing: '0.05em',
                }}>
                  {String(i + 1).padStart(2, '0')}
                </span>
                <span style={{ fontSize: 15, fontWeight: 900, color: '#fff' }}>{p.name}</span>
              </div>
              <div style={{ fontSize: 12, color: '#94a3b8', lineHeight: 1.6 }}>{p.desc}</div>
            </div>
            <div style={{ flexShrink: 0, alignSelf: 'center', color: '#475569', fontSize: 16 }}>›</div>
          </button>
        ))}
      </div>
    </div>
  );
}
